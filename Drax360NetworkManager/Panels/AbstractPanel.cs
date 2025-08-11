using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Runtime;
using System.Security;
using System.Text;
using System.Threading;

namespace Drax360Service.Panels
{
    internal abstract class AbstractPanel
    {

        #region Constants
        protected const byte kHeartbeatInitialDelaySeconds = 60;
        protected const byte kHeartbeatDelaySeconds = 60;
        protected const string ksettingsetupsection = "SETUP";

        #endregion

        #region private fields
        protected readonly List<byte> buffer = new List<byte>();
        protected Timer heartbeat_timer;
        
        

        #endregion

        #region Properties
        protected SerialPort serialport { get; set; }
        public event EventHandler Fire;
        public event EventHandler OutsideEvents;
        public string Identifier { get; private set; }
        public string GetFileName { get; private set; }

        // Abstract properties

        public abstract string FakeString { get; }
        #endregion

        #region Constructors
        public AbstractPanel(string identifier, string inifile)
        {
            Identifier = identifier;
            this.GetFileName = inifile;
            
            
        }
        #endregion

        #region Public Methods
        public abstract void StartUp(int fakemode);
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

        public void NotifyClient(string message,bool notifyui)
        {
            OutsideEvents?.Invoke(this, new CustomEventArgs(message,notifyui));
        }

        public void SendEvent(string panel, NwmData type, string text, int node = 0, int loop = 0, int device = 0)
        {
            // Notify the client application
            OutsideEvents?.Invoke(this, new CustomEventArgs(text,true));

            if (type == NwmData.AlarmToAmx || type == NwmData.ResetToNwm)
            {
                amxalarm(text, node, loop, device);
            }
            else
            {
                amxsend(type, text, node, loop, device);
            }
        }
        public void SendEvent(string panel, NwmData type, int inputtype, string text, int node = 0, int loop = 0, int device = 0)
        {
            EventHandler handler = Fire;

            if (handler != null) handler(this, new CustomEventArgs(text,true));

            if (type == NwmData.AlarmToAmx || type == NwmData.ResetToNwm)
            {
                amxalarm(text, inputtype, node, loop, device);
            }
            else
            {
                amxsend(type, text, inputtype, node, loop, device);
            }
        }
        public void Shutdown()
        {
            if (serialport != null)
            {
                try
                {
                    serialport.Close();
                }
                catch
                {

                }
                serialport.Dispose();
                serialport = null;
            }
        }


        public virtual void Parse(byte[] buffer)
        {
            this.buffer.AddRange(buffer);
        }

        public virtual void SerialPort_DatareceivedRichard(object sender, SerialDataReceivedEventArgs e)
        {
            System.Threading.Thread.Sleep(1000);
            int bytestoread = serialport.BytesToRead;
            if (bytestoread == 0) return;

            byte[] readbytes = new byte[bytestoread];
            int numberread = serialport.Read(readbytes, 0, bytestoread);
            if (numberread == 0) return;
           
            Parse(readbytes);
        }

        public virtual void SerialPort_Datareceived(object sender, SerialDataReceivedEventArgs e)
        {
            const int kchunksize = 59; // your fixed packet size
            const int maxWaitMs = 50;  // how long to wait for remaining bytes
            const int pollDelayMs = 2; // how often to check

            int waited = 0;
            while (serialport.BytesToRead < kchunksize && waited < maxWaitMs)
            {
                System.Threading.Thread.Sleep(pollDelayMs);
                waited += pollDelayMs;
            }

            int bytestoread = serialport.BytesToRead;
            if (bytestoread == 0) return;

            byte[] readbytes = new byte[bytestoread];
            int numberread = serialport.Read(readbytes, 0, bytestoread);
            if (numberread == 0) return;

            Parse(readbytes);
        }

        public T GetSetting<T>(string section, string name)
        {
            return SettingsSingleton.Instance(this.GetFileName).GetSetting<T>(section, name);
        }

        #endregion

        #region Protected Methods
        protected virtual void heartbeat_timer_callback(object sender)
        {
            Console.WriteLine("Sent Heartbeat");
        }

        protected void serialsend(byte[] toSend)
        {
            if (serialport?.IsOpen == true)
            {
                serialport.Write(toSend, 0, toSend.Length);
            }
        }
        protected void serialsend(string toSend)
        {
            serialsend(Encoding.ASCII.GetBytes(toSend));
        }
        #endregion

        #region Private Methods
       
        private void amxsend(NwmData type, string text, int inputtype, int node = 0, int loop = 0, int device = 0)
        {
            int amxoffset = 0; // 0 amxlight

            int evnum = CSAMXSingleton.CS.MakeInputNumber(node + amxoffset, loop, device, inputtype);

            CSAMXSingleton.CS.WriteData(type, evnum, text, "", "");
            CSAMXSingleton.CS.FlushMessages();
        }

        private void amxalarm(string text, int inputtype, int node = 0, int loop = 0, int device = 0)
        {
            int amxoffset = 0; // 0 amxlight

            int evnum = CSAMXSingleton.CS.MakeInputNumber(node + amxoffset, loop, device, inputtype);

            CSAMXSingleton.CS.SendAlarmToAMX(evnum, "", "", text);
            CSAMXSingleton.CS.FlushMessages();
        }

        #endregion
    }
}
