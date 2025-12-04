
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static Drax360Service.Panels.PanelTaktis;

namespace Drax360Service.Panels
{
    internal class PanelSyncro : AbstractPanel
    {
        #region constants

        const int MAXINPUTSTRINGS = 5;
        const byte kheartbeatdelayseconds = 60;
            
        #endregion

        public string[] Ip = new string[MAXINPUTSTRINGS];
        public int giZoneNumber = 0;
        public string gsTextField = "";
        public string gsDeviceText = "";
        public string gsZoneText = "";
        public int giDeviceAddress = 0;
        public int giLoopNumber = 0;
        public bool LocalInputUnit = false;
        public int KSFUseLoop = 0;
        public int index = 0;
        private int miMsgID = 0;

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
                KSFUseLoop = base.GetSetting<int>(ksettingsetupsection, "UseLoop");
            }
        }

        public override void Parse(byte[] buffer)
        {
            base.Parse(buffer);
            int bufferlength = buffer.Length;
            bool foundcharacter = false;

            for (int i = 0; i < bufferlength; i++)
            {
                char ch = (char)buffer[i];
                int asc = buffer[i].ToString()[0];  // ASCII code of first digit of the byte
                byte raw = buffer[i];

                if (ch == 5 || ch == 13)  // Panel Heartbeat do nothing
                {
                }
                else
                {
                    if (ch == 10)   // End of line
                    {
                        base.NotifyClient($"{ch} - {asc} - {raw}");

                        if (foundcharacter || index > 2)  // ignore blank lines at start
                        {
                            index++;
                        }
                        if (index >= MAXINPUTSTRINGS)
                        {
                            processmessage();
                            for (int n = 0; n < MAXINPUTSTRINGS; n++)
                            {
                                Ip[n] = ""; // Ensure all lines clear
                            }
                            index = 0;
                            foundcharacter = false;
                        }

                        // Ip[index] += ch;
                    }
                    else
                    {
                        base.NotifyClient($"{ch} - {asc} - {raw}");

                        Ip[index] += ch;
                        foundcharacter = true;
                    }
                }
            }
        }
        private bool processmessage()
        {
            int NumLines = 0;
            string sMessage = "";
            int giNodeNumber = 1;
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
            NumLines = NumLines - 1; // Adjust for zero-based index
            if (NumLines < 2)
            {
                // Abort if there were not enough lines 
                return false;
            }

            if (Ip[NumLines - 1].Contains("CLEARED") || Ip[NumLines].Contains("CLEARED"))
            {
                base.NotifyClient("Set as OFF");
                on = false;
            }
            else
            {
                base.NotifyClient("Set as ON");
                on = true;
            }

            if (Ip[NumLines - 1].IndexOf("I/O INPUT", StringComparison.OrdinalIgnoreCase) >= 0 && Ip[NumLines - 1].IndexOf("ADR=", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                LocalInputUnit = true;
            }
            else
            {
                LocalInputUnit = false;
            }

            string tEvType = "";
            string tTime = "";

            if (!LocalInputUnit)
            {
                int n = Ip[NumLines].IndexOf("CLEARED", StringComparison.OrdinalIgnoreCase);
                if (n < 0)
                    n = Ip[NumLines].IndexOf("DISABLEMENT", StringComparison.OrdinalIgnoreCase);

                if (n < 0 && Ip[NumLines].Length > 4)
                {
                    // Scan for dd/mm/yy
                    for (n = 1; n <= Ip[NumLines].Length - 4; n++)
                    {
                        if (Ip[NumLines][n - 1] == '/' &&
                            Ip[NumLines][n + 2] == '/')
                        {
                            break;
                        }
                    }

                    if (Ip[NumLines].Trim().ToLower() == "trouble")
                    {
                        tEvType = Ip[NumLines];
                    }
                    else
                    {
                        tEvType = Ip[NumLines].Substring(0, n - 3).Trim();
                    }

                    tTime = Ip[NumLines].Substring(n - 3).Trim();

                    // JM 28/04/25 remove the date/time from the text
                    Ip[NumLines] = tEvType;
                }
                else
                {
                    // PMS 25/05/2011 — using n-2 instead of n-3
                    if (n == 0)  // JM 22/08/95
                    {
                        if (Ip[NumLines].Length > 4)
                        {
                            // Re-scan for dd/mm/yy
                            for (n = 1; n <= Ip[NumLines].Length - 4; n++)
                            {
                                if (Ip[NumLines][n - 1] == '/' &&
                                    Ip[NumLines][n + 2] == '/')
                                {
                                    break;
                                }
                            }

                            tEvType = Ip[NumLines].Substring(0, n - 3).Trim();
                            tTime = Ip[NumLines].Substring(n - 3).Trim();
                        }
                        else
                        {
                            tEvType = Ip[NumLines].Trim();
                        }
                    }
                    else
                    {
                        tEvType = Ip[NumLines].Substring(0, n - 1).Trim();
                        tTime = Ip[NumLines].Substring(n).Trim();
                    }
                }

                // PMS 25/05/2011 — remove "CLEARED"

                tTime = Regex.Replace(tTime, "CLEARED", "", RegexOptions.IgnoreCase).Trim();


                // Remove "MESSAGE"
                if (tTime.IndexOf("MESSAGE", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    tTime = tTime.Substring(7).Trim();
                }
            }
            else
            {
                // LocalInputUnit = True
                if (Ip[NumLines].Length > 16)
                {
                    tEvType = Ip[NumLines].Substring(0, Ip[NumLines].Length - 16).Trim();
                    tEvType = Regex.Replace(tEvType, "CLEARED", "", RegexOptions.IgnoreCase).Trim();

                    tTime = Ip[NumLines].Substring(Ip[NumLines].Length - 16);
                }
            }


            // Extract the node number for lines 2 to 4

            // Default
            int tNode = 1;
            for (int i = 0; i <= 4; i++)
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
            giDeviceAddress = GetSyncroDevice();

            string gsTextField = "";
            string gAlarmType = "";
            int tIpType = 0;
            bool bColInputFix = false;

            if (LocalInputUnit)
            {
                tIpType = 7;        // Tech Alarm
            }
            else
            {
                switch (tEvType.ToUpper())
                {
                    case "FIRE":
                        tIpType = 0;
                        break;

                    case "PRE-ALARM":
                    case "PRE ALARM":
                        tIpType = 2;
                        break;

                    case "DISABLEMENT":
                        if (Ip[0].ToUpper() == "DISABLED LOOP" ||
                            Ip[0].ToUpper() == "AUDIBLE OUTS DISABLED" ||
                            Ip[0].ToUpper() == "BUZZER DISABLED")
                        {
                            tIpType = 15;
                        }
                        else
                        {
                            tIpType = 4;
                        }
                        break;

                    case "ATTENTION":
                        tIpType = 0;
                        break;

                    case "EVACUATE":
                        tIpType = 15;
                        break;

                    case "ALERT":
                        tIpType = 15;
                        break;

                    case "TESTMODE":
                        tIpType = 6;
                        break;

                    case "FAULT":
                        tIpType = 8;
                        break;

                    case "STATUS":
                        tIpType = 15;
                        break;

                    case "SECURITY":
                        tIpType = 0;
                        break;

                    case "MAINTENANCE":
                        tIpType = 10;
                        break;

                    case "TECH ALARM":
                        tIpType = 7;
                        break;

                    case "TECH":
                    case "TECHNICAL":
                    case "TECH-ALARM":
                    case "SUPERVISORY":
                        tIpType = 6;
                        break;

                    case "ACK.ALARM":
                        tIpType = 15;
                        break;

                    case "RESET":
                        tIpType = 15;
                        break;

                    case "TEST":
                        tIpType = 6;
                        break;

                    case "SUPER":
                    case "CARBON MONOXIDE":
                    case "AUXILIARY":
                        tIpType = 3;
                        break;

                    case "SILENCE ALARM":
                        tIpType = 15;
                        break;

                    case "TROUBLE":
                        tIpType = 8;
                        break;

                    case "TEST MODE":
                        tIpType = 15;
                        break;

                    case "USER":
                        bColInputFix = true;
                        tIpType = 7;
                        break;

                    default:
                        if (LocalInputUnit)
                        {
                            tIpType = 7;   // Tech Alarm
                        }
                        else
                        {
                            if (Ip[2].ToUpper().StartsWith("PROG.") || Ip[3].ToUpper().StartsWith("PROG."))
                            {
                                tIpType = 15;
                            }
                            else if (Ip[2].ToUpper().StartsWith("PANEL ACK.") || Ip[3].ToUpper().StartsWith("ACK. ALARM"))
                            {
                                tIpType = 15;
                            }
                            else if (Ip[2].ToUpper().StartsWith("FIRE"))
                            {
                                tIpType = 0;
                                tEvType = "FIRE";
                            }
                            else
                            {
                                // tIpType = CheckUserMessages(tEvType);  TODO
                            }
                        }
                        break;
                }

            }
            // The Event Message could be in line 1 or line 2, so check both
            for (int n = 0; n < 2; n++)
            {
                switch (Ip[n].ToUpper())
                {
                    case "EVACUATE":
                    case "EVACUATE BUTTON":
                    case "EVACUATE EVACUATE":
                        tIpType = 15;
                        gsTextField = "Evacuate";
                        giDeviceAddress = 104;
                        break;
                    case "BUZZER DISABLED":
                        tIpType = 15;
                        gsTextField = "Buzzer Disabled";
                        giDeviceAddress = 101;
                        break;
                    case "RESET":
                    case "PANEL RESET":
                        tIpType = 15;
                        gsTextField = "Panel Reset";
                        giDeviceAddress = 105;
                        break;
                    case "INPUT RESOUND":
                        tIpType = 15;
                        gsTextField = "Panel Resound";
                        giDeviceAddress = 107;
                        break;
                    case "INPUT ACK":
                        tIpType = 15;
                        gsTextField = "Panel ACK";
                        giDeviceAddress = 108;
                        break;
                    case "PNL ACK.ALARM":
                    case "PANEL ACK.ALARM":
                        tIpType = 15;
                        gsTextField = "Silence Alarm";
                        giDeviceAddress = 113;
                        break;
                    case "LOOP OPEN CIRCUIT":
                        tIpType = 15;
                        gsTextField = "Loop Open Circuit";
                        giDeviceAddress = 115 + giLoopNumber;
                        break;
                    case "INPUT ACTIVATED":
                        if (tIpType == 15)
                        {
                            switch (GetSyncroDevType().ToUpper())
                            {
                                case "EVACUATE":
                                case "PANEL EVACUATE":
                                case "EVACUATE BUTTON":
                                    gsTextField = "Panel Evacuate";
                                    giDeviceAddress = 104;
                                    break;
                                case "PANEL RESET":
                                    gsTextField = "Panel Reset";
                                    giDeviceAddress = 105;
                                    break;
                            }
                        }
                        break;

                    case "INTERNAL FAULT":
                        gsTextField = "Internal Fault";
                        if (sNodeDesc.IndexOf("FIRECELL", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            // found
                        }

                        break;

                    case "INPUT SHORT CIRCUIT":
                        gsTextField = "Input Short Circuit";
                        break;

                    case "HEAD MISSING (HEAD REMOVED FROM BASE)":
                        gsTextField = "Head Missing";
                        break;

                    case "DISABLED DEVICE":
                        break;


                }
            }

            int p1 = 0;
            int evnum = 0;

            try
            {
                enmNotAlarmType enumValue = (enmNotAlarmType)Enum.Parse(typeof(enmNotAlarmType), tIpType.ToString());
                p1 = (int)(enumValue);
            }
            catch (Exception ex)
            {
                this.NotifyClient("gAlarmType " + gAlarmType + " " + ex.Message, false);
                Console.WriteLine($"Unexpected error: {ex.Message}");
            }

            base.NotifyClient("Send to AMX: Node = " + giNodeNumber + " Loop = " + giLoopNumber + " Address = " + giDeviceAddress);

            evnum = CSAMXSingleton.CS.MakeInputNumber(giNodeNumber, giLoopNumber, giDeviceAddress, p1, on);
            send_response_amx_and_serial(evnum, gsTextField, gsDeviceText, gsZoneText);
            return true;
        }

        private int GetSyncroZone()
        {
            int x, n;
            int result = 255; 

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
            {}

            return result;
        }

        private int GetSyncroDevice()
        {
            int x, n;
            int result = 255; 
            giDeviceAddress = result;

            try
            {
                // Search lines 2–6
                for (x = 0; x <= 6; x++)
                {
                    string line = Ip[x] ?? "";

                    n = line.IndexOf("ADR=", StringComparison.OrdinalIgnoreCase);
                    if (n >= 0)
                    {
                        result = Convert.ToInt32(line.Substring(n + 4,3));
                        giDeviceAddress = result;
                        break;
                    }
                }

                // If still 255, check line 1
                if (giDeviceAddress == 255)
                {
                    string line1 = Ip[1] ?? "";

                    n = line1.IndexOf("ADR=", StringComparison.OrdinalIgnoreCase);
                    if (n >= 0)
                    {
                        result = Convert.ToInt32(line1.Substring(n + 4));
                        giDeviceAddress = result;
                    }
                }
            }
            catch
            {}

            return result;
        }

        private string GetSyncroDevType()
        {
            string result = "";  // default
            int n;

            try
            {
 
                for (int x = 2; x <= 6; x++)
                {
                    string line = Ip[x] ?? "";

                    n = line.IndexOf("ZONE", StringComparison.OrdinalIgnoreCase);
                    if (n > 0)
                    {
                        result = line.Substring(0, n).Trim();
                        break;
                    }
                }
            }
            catch
            {}

            return result;
        }


        private void send_response_amx_and_serial(int evnum, string message1, string message2, string message3 = "")
        {
            string friendlymessage = message2 + (message3.Length > 0 ? (" " + message3) : "");

            // Signal the event back to the main service, so that it can be logged
            this.NotifyClient(friendlymessage, false);

            CSAMXSingleton.CS.SendAlarmToAMX(evnum, message1, message2, message3);
            CSAMXSingleton.CS.FlushMessages();
        }

        private string makechecksum(string[] paryMessage)
        {
            int checksum = 0;

            try
            {
                for (int i = 1; i < paryMessage.Length - 1; i++)
                {
                    if (!string.IsNullOrEmpty(paryMessage[i]))
                    {
                        checksum += Convert.ToInt32(paryMessage[i]);
                    }
                }

                checksum = checksum % 256;

                return checksum.ToString();
            }
            catch
            {
                return "0";
            }
        }


        protected override void heartbeat_timer_callback(object sender)
        {
            base.heartbeat_timer_callback(sender);

 //           send_message(ActionType.KHandShake, NwmData.AlarmToAmx, "0,0,0,0");
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

            string text = action.ToString();


            string[] gbaryDataToTX = new string[6];
            if (action == ActionType.kRESET)
            {
                gbaryDataToTX = new string[6];
                gbaryDataToTX[0] = "219";
                gbaryDataToTX[1] = GetNextMsgID().ToString();
                gbaryDataToTX[2] = node.ToString();
                gbaryDataToTX[3] = "77";
                gbaryDataToTX[4] = "0";


                string sChecksum = makechecksum(gbaryDataToTX);
                gbaryDataToTX[5] = sChecksum;
            }

            if (action == ActionType.kEVACTUATE)
            {
                gbaryDataToTX = new string[6];
                gbaryDataToTX[0] = "219";
                gbaryDataToTX[1] = GetNextMsgID().ToString();
                gbaryDataToTX[2] = node.ToString();
                gbaryDataToTX[3] = "72";
                gbaryDataToTX[4] = "0";

                string sChecksum = makechecksum(gbaryDataToTX);
                gbaryDataToTX[5] = sChecksum;
            }

            if (action == ActionType.KHandShake)
            {
                gbaryDataToTX = new string[6];
                gbaryDataToTX[0] = "219";
                gbaryDataToTX[1] = GetNextMsgID().ToString();
                gbaryDataToTX[2] = node.ToString();
                gbaryDataToTX[3] = "86";
                gbaryDataToTX[4] = "0";

                string sChecksum = makechecksum(gbaryDataToTX);
                gbaryDataToTX[5] = sChecksum;
            }

            if (action == ActionType.kDISABLEDEVICE)
            {
                gbaryDataToTX = new string[10];
                gbaryDataToTX[0] = "219";
                gbaryDataToTX[1] = GetNextMsgID().ToString();
                gbaryDataToTX[2] = node.ToString();
                gbaryDataToTX[3] = "70";
                gbaryDataToTX[4] = "4";
                gbaryDataToTX[5] = device.ToString();
                gbaryDataToTX[6] = "0";
                gbaryDataToTX[7] = "0";
                gbaryDataToTX[8] = (loop-1).ToString();

                string sChecksum = makechecksum(gbaryDataToTX);
                gbaryDataToTX[9] = sChecksum;
            }

            if (action == ActionType.kENABLEDEVICE)
            {
                gbaryDataToTX = new string[10];
                gbaryDataToTX[0] = "219";
                gbaryDataToTX[1] = GetNextMsgID().ToString();
                gbaryDataToTX[2] = node.ToString();
                gbaryDataToTX[3] = "71";
                gbaryDataToTX[4] = "4";
                gbaryDataToTX[5] = device.ToString();
                gbaryDataToTX[6] = "0";
                gbaryDataToTX[7] = "0";
                gbaryDataToTX[8] = (loop-1).ToString();

                string sChecksum = makechecksum(gbaryDataToTX);
                gbaryDataToTX[9] = sChecksum;
            }

            if (action == ActionType.kDISABLEZONE)
            {
                gbaryDataToTX = new string[8];
                gbaryDataToTX[0] = "219";
                gbaryDataToTX[1] = GetNextMsgID().ToString();
                gbaryDataToTX[2] = node.ToString();
                gbaryDataToTX[3] = "90";
                gbaryDataToTX[4] = "2";
                gbaryDataToTX[5] = "0";
                gbaryDataToTX[6] = zone.ToString();

                string sChecksum = makechecksum(gbaryDataToTX);
                gbaryDataToTX[7] = sChecksum;
            }

            if (action == ActionType.kENABLEZONE)
            {
                gbaryDataToTX = new string[8];
                gbaryDataToTX[0] = "219";
                gbaryDataToTX[1] = GetNextMsgID().ToString();
                gbaryDataToTX[2] = node.ToString();
                gbaryDataToTX[3] = "91";
                gbaryDataToTX[4] = "2";
                gbaryDataToTX[5] = "0";
                gbaryDataToTX[6] = zone.ToString();

                string sChecksum = makechecksum(gbaryDataToTX);
                gbaryDataToTX[7] = sChecksum;
            }

            if (action == ActionType.KAnalogueData)
            {
                gbaryDataToTX = new string[8];
                gbaryDataToTX[0] = "219";
                gbaryDataToTX[1] = GetNextMsgID().ToString();
                gbaryDataToTX[2] = node.ToString();
                gbaryDataToTX[3] = "68";
                gbaryDataToTX[4] = "2";
                gbaryDataToTX[5] = loop.ToString();
                gbaryDataToTX[6] = device.ToString();

                string sChecksum = makechecksum(gbaryDataToTX);
                gbaryDataToTX[7] = sChecksum;
            }

            serialsendstring(gbaryDataToTX);

          //  node = node + this.Offset;
          //  SendEvent("Syncro", type, inputtype, text, on, node, loop, device);
            }

        public int GetNextMsgID()
        {
            miMsgID++;

            if (miMsgID > 170)
                miMsgID = 1;

            return miMsgID;
        }

        //public override void SerialPort_Datareceivedold(object sender, SerialDataReceivedEventArgs e)
        //{
        //    System.Threading.Thread.Sleep(500);

        //    int bytestoread = serialport.BytesToRead;
        //    if (bytestoread == 0) return;

        //    byte[] readbytes = new byte[bytestoread];
        //    int numberread = serialport.Read(readbytes, 0, bytestoread);
        //    if (numberread == 0) return;

        //    Parse(readbytes);
        //}

        private readonly List<byte> _serialBuffer = new List<byte>();
        private readonly byte[] _messageTerminator = new byte[] { 0x0D, 0x0A }; // \r\n

        public override void SerialPort_Datareceived(object sender, SerialDataReceivedEventArgs e)
        {
            int bytesToRead = serialport.BytesToRead;
            if (bytesToRead == 0) return;

            byte[] buffer = new byte[bytesToRead];
            int read = serialport.Read(buffer, 0, bytesToRead);
            if (read == 0) return;

            lock (_serialBuffer)
            {
                _serialBuffer.AddRange(buffer);

                while (true)
                {
                    int endIndex = FindTerminator(_serialBuffer, _messageTerminator);
                    if (endIndex == -1) break; // no full message yet

                    // Extract the complete message including the terminator
                    byte[] completeMsg = _serialBuffer.Take(endIndex + _messageTerminator.Length).ToArray();

                    // Remove it from buffer
                    _serialBuffer.RemoveRange(0, endIndex + _messageTerminator.Length);

                    // Parse the complete message
                    Parse(completeMsg);
                }
            }
        }

        /// <summary>
        /// Finds the first occurrence of the terminator in the buffer.
        /// Returns the index of the first terminator byte, or -1 if not found.
        /// </summary>
        private int FindTerminator(List<byte> buffer, byte[] terminator)
        {
            for (int i = 0; i <= buffer.Count - terminator.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < terminator.Length; j++)
                {
                    if (buffer[i + j] != terminator[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match) return i;
            }

            return -1;
        }

    }
}