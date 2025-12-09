
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;

namespace Drax360Service.Panels
{
    internal class PanelMorleyZX : AbstractPanel
    {
        #region constants
        const byte kzerobyte = 0x00;
        const byte kackbyte = 0x06;
        const byte kheartbeatdelayseconds = 60;
        const int kchunksize = 59;
        #endregion

        public override string FakeString
        {
            get
            {
                // two messages are sent, so we return the same message twice
                string msg = "";

                return msg;
            }
        }



        public PanelMorleyZX(string baselogfolder, string identifier) : base(baselogfolder, identifier, "KsfMan", "KSF")
        {
            if (!String.IsNullOrEmpty(identifier))
            {
                heartbeat_timer = new System.Threading.Timer(heartbeat_timer_callback, this.Identifier, 500, kheartbeatdelayseconds * 1000);
                this.Offset = base.GetSetting<int>(ksettingsetupsection, "giAmx1Offset");
            }
        }

        public override void Parse(byte[] buffer)
        {

        }

        private bool processmessage(byte[] chunk)
        {
            string hex = BitConverter.ToString(chunk);
            this.NotifyClient("Received: " + hex, false);


            return true;
        }

        private void send_response_amx_and_serial(int evnum, string message1, string message2, string message3 = "")
        {
            string friendlymessage = message2 + (message3.Length > 0 ? (" " + message3) : "");

            // Signal the event back to the main service, so that it can be logged
            this.NotifyClient(friendlymessage, false);

            CSAMXSingleton.CS.SendAlarmToAMX(evnum, message1, message2, message3);
            CSAMXSingleton.CS.FlushMessages();

            serialsend(new byte[] { kzerobyte, kackbyte, kzerobyte, kackbyte });
            byte[] bytesToLog = new byte[] { kzerobyte, kackbyte, kzerobyte, kackbyte };
            string hex = BitConverter.ToString(bytesToLog); // "00-06-00-06"
            this.NotifyClient("ACK Sent: " + hex, false);
        }

        private void CalculateCheckSum(string[] paryMessage, out int piMSB, out int piLSB)
        {
            piMSB = 0;
            piLSB = 0;

            try
            {
                int iMsgCheckSum = 0;

                for (int i = 0; i < paryMessage.Length - 2; i++)
                {
                    if (!string.IsNullOrEmpty(paryMessage[i]) && (int)paryMessage[i][0] != 0)
                    {
                        iMsgCheckSum += (int)paryMessage[i][0];
                    }
                }

                piMSB = iMsgCheckSum / 256;
                piLSB = iMsgCheckSum % 256;
            }
            catch (Exception)
            {
                this.NotifyClient("Checksumvalidation Error:", false);
                this.NotifyClient("piMSB: " + piMSB, false);
                this.NotifyClient("piLSB: " + piLSB, false);
            }
        }

        protected override void heartbeat_timer_callback(object sender)
        {
            base.heartbeat_timer_callback(sender);
            serialsend("");
            //serialsend(new byte[] { kzerobyte, kackbyte, kzerobyte, kackbyte });
        }

        public override void StartUp(int fakemode)
        {
            int setttingbaudrate = 9600;
            string settingparity = "None";
            int settingdatabits = 8;
            int settingstopbits = 1;

            if (fakemode > 0)
            {
                return;
            }

            // we are a real serial port 
            serialport = new SerialPort(this.Identifier);
            serialport.BaudRate = setttingbaudrate;

            Parity parity = Parity.None;
            string friendlyparity = settingparity.Substring(0, 1).ToUpper();
            if (friendlyparity == "E")
                parity = Parity.Even;
            if (friendlyparity == "O")
                parity = Parity.Odd;

            serialport.Parity = parity;

            serialport.DataBits = settingdatabits;
            serialport.StopBits = (StopBits)settingstopbits;
            serialport.Handshake = Handshake.None;
            serialport.DataReceived += SerialPort_Datareceived;
            if (serialport.IsOpen)
            {
                serialport.Close();
            }
            base.NotifyClient("Attempting Open " + serialport.PortName, false);
            serialport.Encoding = System.Text.Encoding.ASCII;
            serialport.DtrEnable = true;

            serialport.ReadBufferSize = 8000;
            serialport.WriteBufferSize = 200;

            serialport.ReadTimeout = 500;
            serialport.ParityReplace = (byte)0;
            serialport.ReceivedBytesThreshold = 8;
            try
            {
                serialport.Open();
            }
            catch (Exception e)
            {
                base.NotifyClient("Failed To Open " + serialport.PortName + " " + e.ToString(), false);
            }

            if (serialport.IsOpen)
            {
                serialport.DiscardInBuffer();
                serialport.DiscardOutBuffer();
            }
        }
        public override void Evacuate(string passedvalues)
        {
            send_message(ActionType.kEVACTUATE, NwmData.AlarmToAmx, passedvalues);
        }
        public override void Alert(string passedvalues)
        {
        }
        public override void EvacuateNetwork(string passedvalues)
        {
            send_message(ActionType.kEVACTUATENETWORK, NwmData.AlarmToAmx, passedvalues);
        }
        public override void Silence(string passedvalues)
        {
            send_message(ActionType.kSILENCE, NwmData.AlarmToAmx, passedvalues);
        }
        public override void MuteBuzzers(string passedvalues)
        {
            send_message(ActionType.kMUTEBUZZERS, NwmData.AlarmToAmx, passedvalues);
        }
        public override void Reset(string passedvalues)
        {
            send_message(ActionType.kRESET, NwmData.AlarmToAmx, passedvalues);
        }
        public override void DisableDevice(string passedvalues)
        {
            send_message(ActionType.kDISABLEDEVICE, NwmData.IsolationToAmx, passedvalues);
        }
        public override void EnableDevice(string passedvalues)
        {
            send_message(ActionType.kENABLEDEVICE, NwmData.IsolationToAmx, passedvalues);
        }
        public override void DisableZone(string passedvalues)
        {
            send_message(ActionType.kDISABLEZONE, NwmData.IsolationToAmx, passedvalues);
        }
        public override void EnableZone(string passedvalues)
        {
            send_message(ActionType.kENABLEZONE, NwmData.IsolationToAmx, passedvalues);
        }
        public override void Analogue(string passedvalues)
        {
            throw new NotImplementedException();
        }
        private void send_message(ActionType action, NwmData type, string passedvalues)
        {
            string[] parts = passedvalues.Split(',');

            int node = 1, loop = 0, zone = 0, device = 0, giDomainNumber = 0, inputtype = 0;

            if (parts.Length > 0) int.TryParse(parts[0], out node);
            if (parts.Length > 1) int.TryParse(parts[1], out loop);
            if (parts.Length > 2) int.TryParse(parts[2], out zone);
            if (parts.Length > 3) int.TryParse(parts[3], out device);

            DateTime now = DateTime.Now;

            int sHour = now.Hour;
            int sMinute = now.Minute;
            int sSecond = now.Second;

            int sYear = int.Parse(now.ToString("yy"));   // Two-digit year
            int sMonth = int.Parse(now.ToString("MM"));  // Two-digit month
            int sDay = int.Parse(now.ToString("dd"));    // Two-digit day
            int sDayWeek = ((int)now.DayOfWeek + 6) % 7 + 1;// Sunday = 1, Monday = 2, etc.
            bool on = true;


            serialsend("");

            node = node + this.Offset;
            string text = "";
            SendEvent("Gent", type, inputtype, text, on, node, loop, device);
        }


        public override void SerialPort_Datareceived(object sender, SerialDataReceivedEventArgs e)
        {
            const int kchunksize = 59;  // packet size
            const int maxWaitMs = 500;  // how long to wait for remaining bytes
            const int pollDelayMs = 10; // how often to check

            lastDataReceived = DateTime.Now;
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

    }

}