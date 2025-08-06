using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace Drax360Service.Panels
{
    internal abstract class AbstractPanel
    {

        #region Constants
        protected const byte kHeartbeatInitialDelaySeconds = 60;
        protected const byte kHeartbeatDelaySeconds = 60;
        #endregion

        #region Fields
        protected readonly List<byte> buffer = new List<byte>();
        protected Timer heartbeat_timer;
        #endregion

        #region Properties
        public SerialPort Port { get; set; }
        public event EventHandler Fire;
        public event EventHandler OutsideEvents;
        public string Identifier { get; private set; }

        // Abstract properties
        public abstract string GetFileName { get; }
        public abstract string FakeString { get; }
        #endregion

        #region Constructors
        public AbstractPanel(string identifier)
        {
            Identifier = identifier;
        }
        #endregion

        #region Public Methods
        public abstract void OnStartUp();
        public abstract void Evacuate(string passedValues);
        public abstract void EvacuateNetwork(string passedValues);
        public abstract void Alert(string passedValues);
        public abstract void Silence(string passedValues);
        public abstract void MuteBuzzers(string passedValues);
        public abstract void Reset(string passedValues);
        public abstract void DisableDevice(string passedValues);
        public abstract void EnableDevice(string passedValues);
        public abstract void DisableZone(string passedValues);
        public abstract void EnableZone(string passedValues);

        public void NotifyClient(string message)
        {
            OutsideEvents?.Invoke(this, new CustomEventArgs(message));
        }

        public void SendEvent(string panel, NwmData type, string text, int node = 0, int loop = 0, int device = 0)
        {
            // Notify the client application
            OutsideEvents?.Invoke(this, new CustomEventArgs(text));

            if (type == NwmData.AlarmToAmx || type == NwmData.ResetToNwm)
            {
                AmxAlarm(text, node, loop, device);
            }
            else
            {
                AmxSend(type, text, node, loop, device);
            }
        }
        public void SendEvent(string panel, NwmData type, int inputtype, string text, int node = 0, int loop = 0, int device = 0)
        {
            EventHandler handler = Fire;

            if (handler != null) handler(this, new CustomEventArgs(text));

            if (type == NwmData.AlarmToAmx || type == NwmData.ResetToNwm)
            {
                AmxAlarm(text, inputtype, node, loop, device);
            }
            else
            {
                AmxSend(type, text, inputtype, node, loop, device);
            }
        }


        public virtual void Parse(byte[] buffer)
        {
            this.buffer.AddRange(buffer);
        }
        #endregion

        #region Protected Methods
        protected virtual void heartbeat_timer_callback(object sender)
        {
            Console.WriteLine("Sent Heartbeat");
        }

        protected void sendserial(byte[] toSend)
        {
            if (Port?.IsOpen == true)
            {
                Port.Write(toSend, 0, toSend.Length);
            }
        }
        protected void sendserial(string toSend)
        {
            sendserial(Encoding.ASCII.GetBytes(toSend));
        }
        #endregion

        #region Private Methods

        private void AmxSend(NwmData type, string text, int inputtype, int node = 0, int loop = 0, int device = 0)
        {
            int amxoffset = 0; // 0 amxlight

            int evnum = CSAMXSingleton.CS.MakeInputNumber(node + amxoffset, loop, device, inputtype, true);

            CSAMXSingleton.CS.WriteData(type, evnum, text, "", "", true);
            CSAMXSingleton.CS.FlushMessages();
        }

        private void AmxAlarm(string text, int inputtype, int node = 0, int loop = 0, int device = 0)
        {
            int amxoffset = 0; // 0 amxlight

            int evnum = CSAMXSingleton.CS.MakeInputNumber(node + amxoffset, loop, device, inputtype, true);

            CSAMXSingleton.CS.SendAlarmToAMX(evnum, "", "", text);
            CSAMXSingleton.CS.FlushMessages();
        }

        #endregion
    }
}
