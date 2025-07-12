using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Threading;


namespace Drax360Service.Panels
{
    internal abstract class AbstractPanel
    {

        #region constants
        protected const byte kheartbeatintinitialdelayseconds = 60;
        protected const byte kheartbeatdelayseconds = 60;
        
        #endregion 
        protected List<byte> buffer = new List<byte>();

        public SerialPort Port = null;
        public event EventHandler Fire;
        public string Identifier = "";
        protected Timer heartbeat_timer = null;
        

        #region constructors
        public AbstractPanel(string identifier)
        {
            this.Identifier = identifier;
        }
        #endregion

        #region public properties
        
        // returns the log file or ini file name
        public abstract string GetFileName { get; }

        // returns some sample data to send to the panel
        public abstract string FakeString { get; }

        #endregion

        #region public methods
        public abstract void OnStartUp();
        public abstract void Evacuate(string passedvalues);
        public abstract void EvacuateNetwork(string passedvalues);
        public abstract void Alert(string passedvalues);
        public abstract void Silence(string passedvalues);
        public abstract void MuteBuzzers(string passedvalues);
        public abstract void Reset(string passedvalues);
        public abstract void DisableDevice(string passedvalues);
        public abstract void EnableDevice(string passedvalues);
        public abstract void DisableZone(string passedvalues);
        public abstract void EnableZone(string passedvalues);
        public void FireFire(string msg)
        {
            EventHandler handler = Fire;

            if (handler != null) handler(this, new CustomEventArgs(msg));
        }
        public virtual void Parse(byte[] buffer)
        {
            this.buffer.AddRange(buffer);
        }
        #endregion

        #region private methods
        protected virtual void heartbeat_timer_callback(object sender)
        {
            Console.WriteLine("Sent Heartbeat");
        }
        protected void sendserial(byte[] tosend)
        {
            if (Port == null) return;
            if (!Port.IsOpen) return;
            Port.Write(tosend, 0, tosend.Length);
        }
        protected void sendserial(string tosend)
        {
            byte[] buffer = Encoding.ASCII.GetBytes(tosend);
            sendserial(buffer);
        }

        #endregion
    }
}
