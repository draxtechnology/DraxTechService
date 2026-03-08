
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Timers;

namespace DraxTechnology.Panels
{
    internal class PanelNetworkManager : AbstractPanel
    {
        #region constants
        const byte kzerobyte = 0x00;
        const byte kackbyte = 0x06;
        const byte kheartbeatdelayseconds = 60;
        const int kchunksize = 59;
        // Response timeout
        private const int RESPONSE_TIMEOUT = 90; // mSec = 2 Sec
        private const int RESPONSE_TIMEOUT_EXTENDED = 30; // mSec = 9.5 Sec
        private const int RESPONSE_TIMEOUT_SUPER_EXTENDED = 32;
        #endregion

        private bool connectionLostNotified = false;
        private DateTime lastSuccessfulResponse = DateTime.MinValue;
        private System.Timers.Timer pollTimer;
        private int consecutiveFailures = 0;
        private int g_intResponseTimeout = RESPONSE_TIMEOUT;

        public override string FakeString
        {
            get
            {
                // two messages are sent, so we return the same message twice
                string msg = "";
                return msg;
            }
        }

        public PanelNetworkManager(string baselogfolder, string identifier) : base(baselogfolder, identifier, "GenMan", "GEN")
        {
        }

        public override void Parse(byte[] buffer)
        {
        }

        private void send_response_amx_and_serial(int evnum, string message1, string message2, string message3 = "")
        {
            string friendlymessage = message2 + (message3.Length > 0 ? (" " + message3) : "");

            // Signal the event back to the main service, so that it can be logged
            this.NotifyClient(friendlymessage, false);

            if (serialsend(new byte[] { kzerobyte, kackbyte, kzerobyte, kackbyte }))
            {
                byte[] bytesToLog = new byte[] { kzerobyte, kackbyte, kzerobyte, kackbyte };
                string hex = BitConverter.ToString(bytesToLog); // "00-06-00-06"
                this.NotifyClient("ACK Sent: " + hex, false);

                CSAMXSingleton.CS.SendAlarmToAMX(evnum, message1, message2, message3);
                CSAMXSingleton.CS.FlushMessages();
            }
        }
        private void send_response_amx_disable(int evnum, string message1, string message2, string message3, bool on)
        {
            string friendlymessage = message2 + (message3.Length > 0 ? (" " + message3) : "");

            // Signal the event back to the main service, so that it can be logged
            this.NotifyClient(friendlymessage, false);

            CSAMXSingleton.CS.SendAlarmToAMX_disable(evnum, message1, message2, message3, on);
            CSAMXSingleton.CS.FlushMessages();
        }




        public override void StartUp(int fakemode)
        {

        }


        private void PollTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            // First, check if port is still physically present
            if (!IsPortStillAvailable())
            {
                int evnum = CSAMXSingleton.CS.MakeInputNumber(1 + Offset, 0, 0, 0, true);
                CSAMXSingleton.CS.SendAlarmToAMX(evnum, "Master Panel Offline or Not Responding", "", "");
                CSAMXSingleton.CS.FlushMessages();
                connectionLostNotified = true;
            }

            // Check if port is still open
            if (serialport?.IsOpen != true)
            {
                int evnum = CSAMXSingleton.CS.MakeInputNumber(1 + Offset, 0, 0, 0, true);
                CSAMXSingleton.CS.SendAlarmToAMX(evnum, "Master Panel Offline or Not Responding", "", "");
                CSAMXSingleton.CS.FlushMessages();
                connectionLostNotified = true;
            }

            // Check response timeout
            if (lastSuccessfulResponse != DateTime.MinValue && (DateTime.Now - lastSuccessfulResponse).TotalSeconds > g_intResponseTimeout)
            {
                int evnum = CSAMXSingleton.CS.MakeInputNumber(1 + Offset, 0, 0, 0, true);
                CSAMXSingleton.CS.SendAlarmToAMX(evnum, "Master Panel Offline or Not Responding", "", "");
                CSAMXSingleton.CS.FlushMessages();
                connectionLostNotified = true;
            }
        }
        private bool IsPortStillAvailable()
        {
            // Check if the COM port still exists in the system
            string[] availablePorts = SerialPort.GetPortNames();
            return availablePorts.Contains(serialport.PortName);
        }
        public override void Evacuate(string passedvalues)
        {
        }
        public override void Alert(string passedvalues)
        {
        }
        public override void EvacuateNetwork(string passedvalues)
        {
        }
        public override void Silence(string passedvalues)
        {
        }
        public override void MuteBuzzers(string passedvalues)
        {
        }
        public override void Reset(string passedvalues)
        {
        }
        public override void DisableDevice(string passedvalues)
        {
        }
        public override void EnableDevice(string passedvalues)
        {
        }
        public override void DisableZone(string passedvalues)
        {
        }
        public override void EnableZone(string passedvalues)
        {
        }
        public override void Analogue(string passedvalues)
        {
        }
        private void send_message(ActionType action, NwmData type, string passedvalues)
        { 
        }


        public override void SerialPort_Datareceived(object sender, SerialDataReceivedEventArgs e)
        {
        }

    }

}