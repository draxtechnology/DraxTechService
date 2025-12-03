
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;

namespace Drax360Service.Panels
{
    internal class PanelSyncro : AbstractPanel
    {
        #region constants

        const int MAXINPUTSTRINGS = 6;
        const byte kheartbeatdelayseconds = 60;
        const int KSFUseLoop = 1;   // TODO: Make configurable
        #endregion

        public string[] Ip = new string[MAXINPUTSTRINGS];
        public int giZoneNumber = 0;
        public string gsTextField = "";
        public string gsDeviceText = "";
        public string gsZoneText = "";
        public int giAddressNumber = 0;
        public int giLoopNumber = 0;
        public bool LocalInputUnit = false; // TODO

        public override string FakeString
        {
            get
            {
                // two messages are sent, so we return the same message twice
                string msg = "";

                return msg;
            }
        }

        public PanelSyncro(string baselogfolder, string identifier) : base(baselogfolder, identifier, "KsfMan", "KSF")
        {
            if (!String.IsNullOrEmpty(identifier))
            {
                heartbeat_timer = new System.Threading.Timer(heartbeat_timer_callback, this.Identifier, 500, kheartbeatdelayseconds * 1000);
                this.Offset = base.GetSetting<int>(ksettingsetupsection, "giAmx1Offset");
            }
        }

        public override void Parse(byte[] buffer)
        {

            base.Parse(buffer);
            int bufferlength = buffer.Length;
            int index = 0;

            for (int i = 0; i < bufferlength; i++)
            {
                char ch = (char)buffer[i];
                int asc = buffer[i].ToString()[0];  // ASCII code of first digit of the byte
                byte raw = buffer[i];

                if (raw == 5 || raw == 13)  // Panel Heartbeat & carriage return do nothing
                {
                }
                else
                {
                    if (raw == 10)   // End of line
                    {
                        base.NotifyClient($"{ch} - {asc} - {raw}");

                        index++;
                        if (index >= MAXINPUTSTRINGS)
                        {
                            processmessage();
                            for (int n = 0; n < MAXINPUTSTRINGS; n++)
                            {
                                Ip[n] = ""; // Ensure all lines clear
                            }
                            index = 0;
                        }

                        Ip[index] += ch;
                    }
                    else
                    {
                        base.NotifyClient($"{ch} - {asc} - {raw}");

                        Ip[index] += ch;
                    }
                }
            }
        }
        private bool processmessage()
        {
            int NumLines = 0;
            string sMessage = "";
            int giNodeNumber = 0;
            string sNodeDesc = "";
            bool on = true;

            for (int n = 0; n < MAXINPUTSTRINGS; n++)
            {
                Ip[n] = Ip[n]?.Trim() ?? ""; // Trim and handle nulls

                if (Ip[n].Length != 0)
                {
                    // Fix for tech alarm (case-insensitive)
                    Ip[n] = System.Text.RegularExpressions.Regex.Replace(Ip[n], "TECH ALARM", "TECH-ALARM", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    Ip[n] = System.Text.RegularExpressions.Regex.Replace(Ip[n], "ACK. ALARM", "ACK.ALARM", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    NumLines = n + 1;
                }

                sMessage += "|" + Ip[n];
                base.NotifyClient("Line " + (n + 1) + " : " + Ip[n]);
            }

            if (Ip[NumLines - 1].Contains("CLEARED"))
            {
                base.NotifyClient("Set as OFF");
                on = false;
            }
            else
            {
                base.NotifyClient("Set as ON");
            }

            if (NumLines < 2)
            {
                // Abort if there were not enough lines 
                return false;
            }

            // Extract the node number for lines 2 to 4

            // Default
            int tNode = 1;
            for (int i = 2; i <= 4; i++)
            {
                string line = Ip[i] ?? "";

                int n = line.IndexOf("NODE=", StringComparison.OrdinalIgnoreCase);
                if (n >= 0)
                {
                    // Get substring after NODE=
                    string after = line.Substring(n + 5);

                    string numPart = after.Replace(" ", "X");

                    tNode = int.Parse(new string(numPart.TakeWhile(char.IsDigit).ToArray()));

                    giNodeNumber = tNode;

                    int start = n + 5 + tNode.ToString().Length;
                    sNodeDesc = line.Substring(start);

                    break;
                }

                // --- ND= ---
                n = line.IndexOf("ND=", StringComparison.OrdinalIgnoreCase);
                if (n >= 0)
                {
                    string after = line.Substring(n + 3);
                    string numPart = after.Replace(" ", "X");

                    tNode = int.Parse(new string(numPart.TakeWhile(char.IsDigit).ToArray()));

                    giNodeNumber = tNode;

                    int start = n + 3 + tNode.ToString().Length;
                    sNodeDesc = line.Substring(start);

                    break;
                }
            }


            GetSyncroZone();

            // The Event Message could be in line 1 or line 2, so check both
            for (int n = 0; n < 2; n++)
            {
                switch (Ip[n].ToUpper())
                {
                    case "EVACUATE":
                    case "EVACUATE BUTTON":
                        gsTextField = "Evacuate";
                        giAddressNumber = 104;
                        break;
                    case "INPUT ACTIVATED":
                        gsTextField = "Panel Reset";
                        giAddressNumber = 105;
                        break;
                    case "LOOP OPEN CIRCUIT":
                        gsTextField = "Loop Open Circuit";
                        giAddressNumber = 115 + giLoopNumber;
                        break;
                }
            }


            base.NotifyClient("Send to AMX: Node = " + giNodeNumber + " Loop = " + giLoopNumber + " Address = " + giAddressNumber);

            int evnum = CSAMXSingleton.CS.MakeInputNumber(giNodeNumber, giLoopNumber, giAddressNumber, 15, on);
            send_response_amx_and_serial(evnum, gsTextField, gsDeviceText, gsZoneText);
            return true;
        }

        private int GetSyncroZone()
        {
            int x, n;
            int result = 255; // Default VB6 behaviour

            try
            {
                if (LocalInputUnit)
                {
                    if (KSFUseLoop == 0)
                        result = 255;
                    else
                        result = 5;

                    giZoneNumber = result;
                }
                else
                {
                    result = 255;   // Default

                    for (x = 2; x <= 6; x++)
                    {
                        string line = Ip[x] ?? "";

                        // When KSFUseLoop != 0 this actually returns LOOP number
                        if (KSFUseLoop == 0)
                        {
                            // Look for "ZONE "
                            n = line.IndexOf("ZONE ", StringComparison.OrdinalIgnoreCase);

                            // J.M 16/04/2010  
                            if (n < 0)
                                n = line.IndexOf("ZONE", StringComparison.OrdinalIgnoreCase);

                            if (n >= 0)
                            {
                                result = Convert.ToInt32(line.Substring(n + 5));
                                giZoneNumber = result;
                                break;
                            }
                        }
                        else
                        {
                            // KSFUseLoop != 0 → look for LOOP=
                            n = line.IndexOf("LOOP=", StringComparison.OrdinalIgnoreCase);
                            if (n >= 0)
                            {
                                string numPart = line.Substring(n + 5).Split(' ')[0];
                                int loopValue = Convert.ToInt32(numPart);
                                giLoopNumber = loopValue;

                                // JM 21/01/26 override to zone number 255
                                result = 255;
                                break;
                            }

                            // JM 28/08/24 - Elite RS: LP=
                            n = line.IndexOf("LP=", StringComparison.OrdinalIgnoreCase);
                            if (n >= 0)
                            {
                                int loopValue = Convert.ToInt32(line.Substring(n + 3));
                                giLoopNumber = loopValue;
                                break;
                            }

                            // Check first line for LP=
                            if (Ip[1] != null &&
                                Ip[1].IndexOf("LP=", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                n = Ip[1].IndexOf("LP=", StringComparison.OrdinalIgnoreCase);
                                int loopValue = Convert.ToInt32(Ip[1].Substring(n + 3));
                                giLoopNumber = loopValue;
                                break;
                            }
                        }
                    }
                }

                // SECOND PASS — try ZONE if still 255  
                if (result == 255)
                {
                    for (x = 2; x <= 6; x++)
                    {
                        string line = Ip[x] ?? "";

                        n = line.IndexOf("ZONE", StringComparison.OrdinalIgnoreCase);
                        if (n >= 0)
                        {
                            result = Convert.ToInt32(line.Substring(n + 5));
                            giZoneNumber = result;
                            break;
                        }
                    }
                }
            }
            catch
            {
                // VB6: On Error Resume Next → swallow errors
            }

            return result;
        }


        private void send_response_amx_and_serial(int evnum, string message1, string message2, string message3 = "")
        {
            string friendlymessage = message2 + (message3.Length > 0 ? (" " + message3) : "");

            // Signal the event back to the main service, so that it can be logged
            this.NotifyClient(friendlymessage, false);

            CSAMXSingleton.CS.SendAlarmToAMX(evnum, message1, message2, message3);
            CSAMXSingleton.CS.FlushMessages();

            //    serialsend(new byte[] { kzerobyte, kackbyte, kzerobyte, kackbyte });
            //    byte[] bytesToLog = new byte[] { kzerobyte, kackbyte, kzerobyte, kackbyte };
            //    string hex = BitConverter.ToString(bytesToLog); // "00-06-00-06"
            //    this.NotifyClient("ACK Sent: " + hex, false);
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
            int setttingbaudrate = base.GetSetting<int>(ksettingsyncrosection, "BaudRate");
            string settingparity = base.GetSetting<string>(ksettingsyncrosection, "Parity");
            int settingdatabits = base.GetSetting<int>(ksettingsyncrosection, "DataBits");
            int settingstopbits = base.GetSetting<int>(ksettingsyncrosection, "StopBits");

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
            System.Threading.Thread.Sleep(500);

            int bytestoread = serialport.BytesToRead;
            if (bytestoread == 0) return;

            byte[] readbytes = new byte[bytestoread];
            int numberread = serialport.Read(readbytes, 0, bytestoread);
            if (numberread == 0) return;

            Parse(readbytes);
        }
    }
}