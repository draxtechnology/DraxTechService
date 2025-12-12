
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Timers;
using System.Xml.Linq;
using static Drax360Service.Panels.PanelTaktis;

namespace Drax360Service.Panels
{
    internal class PanelSyncro : AbstractPanel
    {
        #region constants

        const int MAXINPUTSTRINGS = 5;
        const byte kheartbeatdelayseconds = 1;

        #endregion

        public string[] Ip = new string[MAXINPUTSTRINGS];
        public string[] UserMessages = new string[16];
        public int[] UserTypes = new int[16];
        public int giZoneNumber = 0;
        public int giDeviceSubAddress = 0;
        public string gsTextField = "";
        public string gsDeviceText = "";
        public string gsZoneText = "";
        public int giDeviceAddress = 0;
        public int giLoopNumber = 0;
        public bool LocalInputUnit = false;
        public int KSFUseLoop = 0;
        public int index = 0;
        public int giAnalogRequestLoop = 0;
        private int miMsgID = 0;
        private List<string> garyzoneDisableList = new List<string>();

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

                if (ch == 5)  // Panel Heartbeat do nothing
                {
                }
                else
                {
                    if (ch == 13)
                    {
                        base.NotifyClient($"{ch} - {asc} - {raw}");
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
                            if (index >= MAXINPUTSTRINGS || (index == 4 && Ip[0]?.ToUpper() == "DISABLED ZONE"))
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
        }
        private bool processmessage()
        {
            int NumLines = 0;
            string sMessage = "";
            string gsZoneText = "";
            gsDeviceText = "";
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
            int i = 0;
            for (i = 0; i <= 4; i++)
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
            giDeviceSubAddress = GetSyncroDeviceSubAddress();
            giDeviceAddress = GetSyncroDevice();

            string gsTextField = Ip[0];
            string gAlarmType = "";
            gsZoneText = "";
            int tIpType = 0;
            bool bColInputFix = false;

            // If the node ID was in the third, then there was a location text in the first
            // Also add the devtype text with a ' | ' (ascii 124) separator
            if (LocalInputUnit)
            {
                if (!string.IsNullOrEmpty(Ip[0]))
                {
                    gsZoneText = Ip[0] + "|LOCAL I/O UNIT";
                }
                else
                {
                    gsZoneText = "LOCAL I/O UNIT";
                }
            }
            else
            {
                if (i == 3)
                {
                    gsTextField = Ip[0];
                    string devType = GetSyncroDevType();
                    if (!string.IsNullOrEmpty(devType))
                    {
                        gsTextField += "|" + devType;
                    }
                }
                else if (i == 2)
                {

                    // There is no location text
                    string devType = GetSyncroDevType();
                    if (!string.IsNullOrEmpty(devType))
                    {
                        gsZoneText = devType;
                    }
                }
            }

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
                                tIpType = CheckUserMessages(tEvType);
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
                    case "ACCESS LEVEL 1":
                        tIpType = 15;
                        gsTextField = "Access Level 1";
                        giLoopNumber = 0;
                        giDeviceAddress = 1;
                        break;
                    case "ACCESS LEVEL 2":
                        tIpType = 15;
                        gsTextField = "Access Level 2";
                        giLoopNumber = 0;
                        giDeviceAddress = 2;
                        break;
                    case "ACCESS LEVEL 3":
                        tIpType = 15;
                        gsTextField = "Access Level 3";
                        giLoopNumber = 0;
                        giDeviceAddress = 3;
                        break;
                    case "ACKNOWLEDGE":
                        tIpType = 15;
                        gsTextField = "Acknowledge";
                        giLoopNumber = 0;
                        giDeviceAddress = 95;
                        break;
                    case "ALL SOUNDERS DISABLED":
                        tIpType = 15;
                        gsTextField = "All Sounders Disabled";
                        giLoopNumber = 0;
                        giDeviceAddress = 4;
                        break;
                    case "ATTENTION":
                        tIpType = 15;
                        gsTextField = "Attemtion";
                        giLoopNumber = 0;
                        giDeviceAddress = 5;
                        break;
                    case "AUTOLEARN":
                        tIpType = 15;
                        gsTextField = "Autolearn";
                        giLoopNumber = 0;
                        giDeviceAddress = 6;
                        break;
                    case "AUX 24V FUSE FAULT":
                        tIpType = 15;
                        gsTextField = "Aux 24V Fuse Fault";
                        giLoopNumber = 0;
                        giDeviceAddress = 7;
                        break;
                    case "BAD DATA FAULT":
                        tIpType = 15;
                        gsTextField = "Bad Data Fault";
                        giLoopNumber = 0;
                        if (sNodeDesc == null || sNodeDesc.IndexOf("FIRECELL", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            giDeviceAddress = 8;
                        }
                        break;
                    case "BATTERY DISCONNECTED":
                        tIpType = 15;
                        gsTextField = "Battery Disconnected";
                        giLoopNumber = 0;
                        giDeviceAddress = 9;
                        break;
                    case "BATTERY VOLTAGE TOO HIGH":
                        tIpType = 15;
                        gsTextField = "Battery Voltage Too High";
                        giLoopNumber = 0;
                        giDeviceAddress = 10;
                        break;
                    case "CALIBRATION ERROR":
                        tIpType = 15;
                        gsTextField = "Calibration Error";
                        giLoopNumber = 0;
                        giDeviceAddress = 11;
                        break;
                    case "CALIBRATION FAILED FAULT":
                        tIpType = 15;
                        gsTextField = "Calibration Failed Fault";
                        giLoopNumber = 0;
                        giDeviceAddress = 12;
                        break;
                    case "CAUSE & EFFECT ACTIVE":
                        tIpType = 15;
                        gsTextField = "Cause & Effect Active";
                        giLoopNumber = 0;
                        giDeviceAddress = 13;
                        break;
                    case "CE DISABLEMENT":
                        tIpType = 15;
                        gsTextField = "CE Disablement";
                        giLoopNumber = 0;
                        giDeviceAddress = 14;
                        break;
                    case "DAY/NIGHT DISABLEMENT":
                        tIpType = 15;
                        gsTextField = "Day/Night Disablement";
                        giLoopNumber = 0;
                        giDeviceAddress = 15;
                        break;
                    case "DETECTOR REMOVED":
                        tIpType = 15;
                        gsTextField = "Detector Removed";
                        break;
                    case "DEVICE INITIALISING":
                    case "INITIALIZING DEVICE":
                        tIpType = 15;
                        gsTextField = "Device Initialising";
                        giDeviceAddress = 16;
                        break;
                    case "DISABLED DEVICE":
                        break;
                    case "DISABLED LOOP":
                        tIpType = 15;
                        gsTextField = "Device Loop";
                        break;
                    case "DISABLEMENT":
                        tIpType = 15;
                        gsTextField = "Disabled";
                        if (tEvType == "TROUBLE")
                        {
                            gsTextField = "Disabled Trouble";
                            giDeviceAddress = 109;
                        }
                        break;
                    case "AUDIBLE OUTS DISABLED":
                        tIpType = 15;
                        gsTextField = "Audible Outs Disabled Node " + giNodeNumber;
                        giDeviceAddress = 119;
                        break;
                    case "DISABLED PANEL INPUT":
                        tIpType = 15;
                        gsTextField = "Disabled Panel Input";
                        giDeviceAddress = 17;
                        break;
                    case "DISABLED PANEL OUTPUT":
                        tIpType = 15;
                        gsTextField = "Disabled Panel Output";
                        giDeviceAddress = 18;
                        break;
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
                    case "LOOP OPEN CIRCUIT":
                        tIpType = 15;
                        gsTextField = "Loop Open Circuit";
                        giDeviceAddress = 115 + giLoopNumber;
                        break;
                    case "LOOP SHORT CIRCUIT":
                        tIpType = 15;
                        gsTextField = "Loop Short Circuit";
                        giDeviceAddress = 120 + giLoopNumber;
                        break;
                    case "LOOP WIRING FAULT. PRESS ? FOR DETAILS":
                        tIpType = 15;
                        gsTextField = "Wiring Fault";
                        giDeviceAddress = 125 + giLoopNumber;
                        break;
                    case "LOW BATTERY VOLTAGE":
                        tIpType = 15;
                        gsTextField = "Low Battery Voltage";
                        giDeviceAddress = 35;
                        break;
                    case "BACK UP BATTERY LOW":
                        tIpType = 15;
                        gsTextField = "Back Up Battery Low";
                        break;
                    case "MAINS FAILED":
                        tIpType = 15;
                        gsTextField = "Mains Failed";
                        giDeviceAddress = 36;
                        break;
                    case "MAINTENANCE FAULT":
                        tIpType = 15;
                        gsTextField = "Maintenance Fault";
                        break;
                    case "MONITORED OUTPUT FAULT":
                        tIpType = 15;
                        gsTextField = "Monitored Output Fault";
                        giDeviceAddress = 38;
                        break;
                    case "MONITORED OUTPUT TROUBLE":
                        tIpType = 15;
                        gsTextField = "Monitored Output Trouble";
                        giDeviceAddress = 38;
                        break;
                    case "NEGATIVE EARTH FAULT":
                        tIpType = 15;
                        gsTextField = "Negative Earth Fault";
                        giDeviceAddress = 39;
                        break;
                    case "NEW CONFIG DOWNLOADED FROM PC":
                        tIpType = 15;
                        gsTextField = "New Config Downloaded From PC";
                        giDeviceAddress = 40;
                        break;
                    case "OPTICAL & HEAT ELEMENT FAULTY":
                        tIpType = 15;
                        gsTextField = "Opt+Heat Element Faulty";
                        giDeviceAddress = 42;
                        break;
                    case "OUTPUT 1 OPEN CIRCUIT":
                        tIpType = 15;
                        gsTextField = "Output 1 Open Circuit";
                        giDeviceAddress = 44;
                        break;
                    case "OUTPUT 1 SHORT CIRCUIT":
                        tIpType = 15;
                        gsTextField = "Output 1 Short Circuit";
                        giDeviceAddress = 45;
                        break;
                    case "OUTPUT 2 OPEN CIRCUIT":
                        tIpType = 15;
                        gsTextField = "Output 2 Open Circuit";
                        giDeviceAddress = 46;
                        break;
                    case "OUTPUT 2 SHORT CIRCUIT":
                        tIpType = 15;
                        gsTextField = "Output 2 Short Circuit";
                        giDeviceAddress = 47;
                        break;
                    case "AC POWER FAILURE":
                        tIpType = 15;
                        gsTextField = "AC Power Failure";
                        giDeviceAddress = 48;
                        break;
                    case "POWER FAILURE":
                        tIpType = 15;
                        gsTextField = "Power Failure";
                        giDeviceAddress = 48;
                        break;
                    case "PRE ALARM":
                        gsTextField = "Pre Alarm";
                        break;
                    case "PROCESSOR WATCH DOG OPERATED":
                        tIpType = 15;
                        gsTextField = "Processor Watch Dog Operated";
                        giDeviceAddress = 49;
                        break;
                    case "RAM CHECKSUM FAULT":
                        tIpType = 15;
                        gsTextField = "Ram Checksum Fault";
                        giDeviceAddress = 50;
                        break;
                    case "REMOTE FAULT":
                        tIpType = 15;
                        gsTextField = "Remote Fault";
                        giDeviceAddress = 51;
                        break;
                    case "I/O MODULE NOT FITTED":
                        tIpType = 15;
                        gsTextField = "I/O Module not fitted";
                        giDeviceAddress = 37;
                        break;
                    case "ROM CHECKSUM FAULT":
                        tIpType = 15;
                        gsTextField = "Rom Checksum Fault";
                        giDeviceAddress = 52;
                        break;
                    case "SLAVE LINE 1 FAULT":
                        tIpType = 15;
                        gsTextField = "Slave Line 1 Fault";
                        giDeviceAddress = 53;
                        break;
                    case "SLAVE LINE 2 FAULT":
                        tIpType = 15;
                        gsTextField = "Slave Line 2 Fault";
                        giDeviceAddress = 54;
                        break;
                    case "SLAVE LINE OPEN CIRCUIT":
                        tIpType = 15;
                        gsTextField = "Slave Line Open Circuit";
                        //giDeviceAddress = 55;
                        break;
                    case "SLAVE LINE SHORT CIRCUIT":
                        tIpType = 15;
                        gsTextField = "Slave Line Short Circuit";
                        giDeviceAddress = 56;
                        break;
                    case "SYSTEM INITIALISING":
                        tIpType = 15;
                        gsTextField = "System Initialising";
                        giDeviceAddress = 106;
                        break;
                    case "TEST MODE":
                        tIpType = 15;
                        gsTextField = "Test Mode";
                        giDeviceAddress = 102;
                        break;
                    case "UNEXPECTED DEVICE":
                        tIpType = 15;
                        gsTextField = "Unexpected Device";
                        giDeviceAddress = 57;
                        break;
                    case "WRITE ENABLE SWITCH ON":
                        tIpType = 15;
                        gsTextField = "Write Enable Switch On";
                        giDeviceAddress = 59;
                        break;
                    case "WRONG DEVICE TYPE":
                        tIpType = 15;
                        gsTextField = "Wrong Device Type";
                        giDeviceAddress = 60;
                        break;
                    case "NETWORK NODE MISSING":
                        tIpType = 15;
                        gsTextField = "Network Node " + tNode + " Missing";
                        giDeviceAddress = 61;
                        break;
                    case "INPUT ACTIVATED":
                        if (tIpType == (int)enmPRLAlarmType.StatusEvent)
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
                                case "PANEL RESOUND":
                                    gsTextField = "Panel Resound";
                                    giDeviceAddress = 107;
                                    break;
                                case "PANEL ACK":
                                    gsTextField = "Panel ACK";
                                    giDeviceAddress = 108;
                                    break;
                                case "PNL ACK.ALARM":
                                case "PANEL ACK.ALARM":
                                    tIpType = 15;
                                    gsTextField = "Silence Alarm";
                                    giDeviceAddress = 113;
                                    break;
                                case "INPUT/OUTPUT":
                                case "CHQ-PCM":
                                    if (Ip[4].ToUpper().Contains("EVACUATE") || Ip[4].ToUpper().Contains("ALERT"))
                                    {
                                        if (giZoneNumber == 255)
                                        {
                                            giZoneNumber = 0;
                                        }
                                        switch (giDeviceSubAddress)
                                        {
                                            case 0:
                                                tIpType = 0;
                                                gsTextField = "Evacuate Zone " + giZoneNumber;
                                                break;
                                            case 1:
                                                tIpType = 3;
                                                gsTextField = "Evacuate Zone " + giZoneNumber;
                                                break;
                                            case 2:
                                                tIpType = 5;
                                                gsTextField = "Evacuate Zone " + giZoneNumber;
                                                break;
                                            case 3:
                                                tIpType = 11;
                                                gsTextField = "Evacuate Zone " + giZoneNumber;
                                                break;
                                            case 4:
                                                tIpType = 12;
                                                gsTextField = "Evacuate Zone " + giZoneNumber;
                                                break;
                                        }
                                    }
                                    break;
                                case "INPUT UNIT":
                                    if (Ip[3].ToUpper().Contains("EVACUATE"))
                                    {
                                        if (giDeviceSubAddress > 0)
                                        {
                                            tIpType = 3;
                                            gsTextField = "Evacuate Zone " + giZoneNumber;
                                        }
                                    }
                                    break;
                                case "SERIAL INPUT":
                                    if (Ip[3].ToUpper().Contains("EVACUATE"))
                                    {
                                        if (giDeviceSubAddress > 0)
                                        {
                                            tIpType = 15;
                                            gsTextField = "Evacuate";
                                            giDeviceAddress = 104;
                                        }
                                    }
                                    if (Ip[3].ToUpper().Contains("ALERT"))
                                    {
                                        if (giDeviceSubAddress > 0)
                                        {
                                            tIpType = 15;
                                            gsTextField = "Alert";
                                            giDeviceAddress = 107;
                                        }
                                    }
                                    break;
                                default:
                                    if (Ip[n + 2].ToUpper().Contains("PROG."))
                                    {
                                        tIpType = 15;
                                        gsTextField = "Prog";
                                        giDeviceAddress = 90;
                                    }
                                    if (Ip[3].ToUpper().Contains("EVACUATE"))
                                    {
                                        if (giDeviceSubAddress > 0)
                                        {
                                            tIpType = 15;
                                            gsTextField = "Evacuate Button";
                                            giDeviceAddress = 104;
                                        }
                                    }
                                    if (Ip[3].ToUpper().Contains("ALERT"))
                                    {
                                        if (giDeviceSubAddress > 0)
                                        {
                                            tIpType = 15;
                                            gsTextField = "Alert";
                                            giDeviceAddress = 107;
                                        }
                                    }
                                    if (Ip[3].ToUpper().Contains("ACK. ALARM") || Ip[3].ToUpper().Contains("ACK.ALARM"))
                                    {
                                        if (giDeviceSubAddress > 0)
                                        {
                                            tIpType = 15;
                                            gsTextField = "Silence";
                                            giDeviceAddress = 108;
                                        }
                                    }
                                    break;
                            }
                        }
                        else
                        {
                            if (tIpType == (int)enmPRLAlarmType.TestModeFire)
                            {
                                if (Ip[0].ToUpper().Contains("FAULT"))
                                {
                                    if (GetSyncroDeviceSubAddress() > 0)
                                    {
                                        gsTextField += "SA " + GetSyncroDeviceSubAddress();
                                    }
                                }
                                else
                                {
                                    if (tEvType == "SUPERVISORY")
                                    {
                                        if (GetSyncroDeviceSubAddress() > 0)
                                        {
                                            gsTextField += "SA " + GetSyncroDeviceSubAddress();
                                        }
                                        switch (giDeviceSubAddress)
                                        {
                                            case 0:
                                                tIpType = 0;
                                                gsTextField = "Supervisory Zone " + giZoneNumber;
                                                break;
                                            case 1:
                                                tIpType = 3;
                                                gsTextField = "Supervisory Zone " + giZoneNumber;
                                                break;
                                            case 2:
                                                tIpType = 5;
                                                gsTextField = "Supervisory Zone " + giZoneNumber;
                                                break;
                                            case 3:
                                                tIpType = 11;
                                                gsTextField = "Supervisory Zone " + giZoneNumber;
                                                break;
                                            case 4:
                                                tIpType = 12;
                                                gsTextField = "Supervisory Zone " + giZoneNumber;
                                                break;
                                        }
                                    }
                                    else
                                    {
                                        if (GetSyncroDeviceSubAddress() > 0)
                                        {
                                            gsTextField += "SA " + GetSyncroDeviceSubAddress();
                                        }
                                        switch (giDeviceSubAddress)
                                        {
                                            case 0:
                                                tIpType = 0;
                                                gsTextField = "Tech Alarm";
                                                break;
                                            case 1:
                                                tIpType = 3;
                                                gsTextField = "Tech Alarm";
                                                break;
                                            case 2:
                                                tIpType = 5;
                                                gsTextField = "Tech Alarm";
                                                break;
                                            case 3:
                                                tIpType = 11;
                                                gsTextField = "Tech Alarm";
                                                break;
                                            case 4:
                                                tIpType = 12;
                                                gsTextField = "Tech Alarm";
                                                break;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                if (tEvType == "SECURITY")
                                {
                                    if (tIpType == (int)enmPRLAlarmType.Fire)
                                    {
                                        if (GetSyncroDeviceSubAddress() > 0)
                                        {
                                            gsTextField += "SA " + GetSyncroDeviceSubAddress();
                                        }
                                        switch (giDeviceSubAddress)
                                        {
                                            case 0:
                                                tIpType = 0;
                                                gsTextField = "Security";
                                                break;
                                            case 1:
                                                tIpType = 3;
                                                gsTextField = "Security";
                                                break;
                                            case 2:
                                                tIpType = 5;
                                                gsTextField = "Security";
                                                break;
                                            case 3:
                                                tIpType = 11;
                                                gsTextField = "Security";
                                                break;
                                            case 4:
                                                tIpType = 12;
                                                gsTextField = "Security";
                                                break;
                                        }
                                    }
                                }
                                else if (tEvType == "AUXILIARY")
                                {
                                    if (tIpType == (int)enmPRLAlarmType.Fire)
                                    {
                                        if (GetSyncroDeviceSubAddress() > 0)
                                        {
                                            gsTextField += "SA " + GetSyncroDeviceSubAddress();
                                        }
                                        switch (giDeviceSubAddress)
                                        {
                                            case 0:
                                                tIpType = 0;
                                                gsTextField = "Auxiliary Zone " + giZoneNumber;
                                                break;
                                            case 1:
                                                tIpType = 3;
                                                gsTextField = "Auxiliary Zone " + giZoneNumber;
                                                break;
                                            case 2:
                                                tIpType = 5;
                                                gsTextField = "Auxiliary Zone " + giZoneNumber;
                                                break;
                                            case 3:
                                                tIpType = 11;
                                                gsTextField = "Auxiliary Zone " + giZoneNumber;
                                                break;
                                            case 4:
                                                tIpType = 12;
                                                gsTextField = "Auxiliary Zone " + giZoneNumber;
                                                break;
                                        }
                                    }
                                }
                                else if (tEvType == "FIRE" || tEvType == "ALERT" || tEvType == "FAULT" || tEvType == "PRE-ALARM")
                                {
                                    string sSubText;

                                    if (tIpType == (int)enmPRLAlarmType.Fire)
                                    {
                                        sSubText = "Fire";
                                    }
                                    else if (tIpType == (int)enmPRLAlarmType.PreAlarm)
                                    {
                                        sSubText = "Pre-Alarm";
                                    }
                                    else if (tIpType == (int)enmPRLAlarmType.Fault)
                                    {
                                        sSubText = "Fault";
                                    }
                                    else
                                    {
                                        sSubText = Ip[2];
                                    }

                                    switch (giDeviceSubAddress)
                                    {
                                        case 1:
                                            tIpType = 3;
                                            gsTextField = "SA 1 " + Ip[1];
                                            break;
                                        case 2:
                                            tIpType = 5;
                                            gsTextField = "SA 2 " + Ip[1];
                                            break;
                                        case 3:
                                            tIpType = 11;
                                            gsTextField = "SA 4 " + Ip[1];
                                            break;
                                        case 4:
                                            tIpType = 12;
                                            gsTextField = "SA 4 " + Ip[1];
                                            break;
                                        default:   // Standard Fire no Sub Address
                                            tIpType = 0;
                                            gsTextField = Ip[3] + "- " + Ip[2] + " " + Ip[4];
                                            break;
                                    }

                                    if (tIpType == (int)enmPRLAlarmType.Fire || tIpType == (int)enmPRLAlarmType.PreAlarm || tIpType == (int)enmPRLAlarmType.Fault)
                                    {
                                        gsZoneText += " ZONE " + giZoneNumber + "-Input Activated " + tEvType;
                                        gsDeviceText = sNodeDesc.Trim();
                                    }
                                }
                                else if (tEvType == "SUPER")
                                {
                                    tIpType = (int)enmPRLAlarmType.StatusEvent;
                                    gsTextField = "Supervisory";
                                    giDeviceAddress = 109;
                                }
                                else if (tEvType == "CARBON MONOXIDE")
                                {
                                }
                                else if (tEvType == "AUXILIARY")
                                {
                                }
                            }
                        }
                        
                        break;

                    case "DISABLED ZONE":
                        GetSyncroZone();
                        if (giDeviceAddress == 255 || giZoneNumber > 0)
                        {
                            if (on)
                            {
                                AddToZoneDisableList(tNode + this.Offset, giZoneNumber, ref giDeviceAddress, ref giLoopNumber);
                            }
                            else
                            {
                                RemoveFromZoneDisableList(tNode + this.Offset, giZoneNumber, ref giDeviceAddress, ref giLoopNumber);
                            }
                            gsTextField = "Disable Zone " + giZoneNumber;

                            tIpType = 13;
                        }
                        break;

                    case "DISCONNECTED FAULT":
                        gsDeviceText = "Disconnected Fault";
                        tIpType = 8;
                        break;

                    case "DISCONNECTED TROUBLE":
                        gsDeviceText = "Disconnected Trouble";
                        tIpType = 8;
                        break;

                    case "DOUBLE ADDRESS":
                        gsDeviceText = "Disconnected Trouble";
                        tIpType = 15;
                        break;

                    case "E2 DIS":
                        gsDeviceText = "E2 Dis";
                        giDeviceAddress = 21;
                        break;

                    case "E3 DIS":
                        gsDeviceText = "E3 Dis";
                        giDeviceAddress = 22;
                        break;

                    case "E3 FAULT":
                        gsDeviceText = "E3 Fault";
                        giDeviceAddress = 23;
                        break;

                    case "E4 FAULT":
                        gsDeviceText = "E4 Fault";
                        giDeviceAddress = 24;
                        break;

                    case "E5 FAULT":
                        gsDeviceText = "E5 Fault";
                        giDeviceAddress = 25;
                        break;

                    case "E6 FAULT":
                        gsDeviceText = "E6 Fault";
                        giDeviceAddress = 26;
                        break;

                    case "E7 FAULT":
                        gsDeviceText = "E7 Fault";
                        giDeviceAddress = 27;
                        break;

                    case "EARTH FAULT":
                        gsDeviceText = "Earth Fault";
                        giDeviceAddress = 28;
                        break;

                    case "INTERNAL FAULT":
                        gsDeviceText = "Internal Fault";
                        if (sNodeDesc.IndexOf("FIRECELL", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            // found
                        }
                        break;

                    case "INPUT SHORT CIRCUIT":
                        gsDeviceText = "Input Short Circuit";
                        break;

                    case "HEAD MISSING (HEAD REMOVED FROM BASE)":
                        gsDeviceText = "Head Missing";
                        break;

                    case "INPUT OPEN CIRCUIT":
                        gsDeviceText = "Input Open Circuit";
                        tIpType = 15;
                        break;

                    case "INPUT CLEARED":
                        gsDeviceText = "Input Cleared";
                        tIpType = 15;
                        break;

                    default:
                        if (n == 1)
                        {
                            if (Ip[1].ToUpper().Contains("ELITE RS PANEL"))
                            {
                                if (tEvType == "SILENCE ALARM")
                                {
                                    tIpType = 15;
                                    gsTextField = "Silence Alarm";
                                    giDeviceAddress = 103;
                                }
                            }
                            else
                            {
                                if (Ip[1].ToUpper().Contains("GENERAL"))
                                {
                                    if (tEvType == "FIRE")
                                    {
                                        switch (giDeviceSubAddress)
                                        {
                                            case 0:
                                                tIpType = 0;
                                                gsTextField = "Fire " + giZoneNumber;
                                                break;
                                            case 1:
                                                tIpType = 3;
                                                gsTextField = "Fire " + giZoneNumber;
                                                break;
                                            case 2:
                                                tIpType = 5;
                                                gsTextField = "Fire " + giZoneNumber;
                                                break;
                                            case 3:
                                                tIpType = 11;
                                                gsTextField = "Fire " + giZoneNumber;
                                                break;
                                            case 4:
                                                tIpType = 12;
                                                gsTextField = "Fire " + giZoneNumber;
                                                break;
                                        }
                                    }
                                    tIpType = 15;
                                    gsTextField = "Silence Alarm";
                                    giDeviceAddress = 103;
                                }
                                else
                                {
                                    this.NotifyClient("Unknown Event " + sMessage + " - " + Ip[0].ToUpper() + " " + Ip[1].ToUpper());
                                }
                            }
                        }
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
            }

            if (giDeviceAddress == 255)   // TODO
            {
                giDeviceAddress = 1; // default
                this.NotifyClient("Device 255 so now " + giDeviceAddress, false);
            }
            base.NotifyClient("Send to AMX: Node = " + (giNodeNumber + this.Offset) + " Loop = " + giLoopNumber + " Address = " + giDeviceAddress);

            evnum = CSAMXSingleton.CS.MakeInputNumber(giNodeNumber, giLoopNumber, giDeviceAddress, p1, on);
            if (tIpType == (int)enmPRLAlarmType.Isolate)  // If Disable Device neeed to also send another event to AMX to increase the Isolation count
            {
                send_response_amx_disable(evnum, gsTextField, gsZoneText, gsDeviceText);
            }
            send_response_amx_and_serial(evnum, gsTextField, gsZoneText, gsDeviceText);
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

                    for (x = 2; x < Ip.Length; x++)
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

                // SECOND PASS — try ZONE with no space if still 255  
                if (result == 255)
                {
                    for (x = 1; x <= 6; x++)
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
            { }

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
                        result = Convert.ToInt32(line.Substring(n + 4, 3));
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
            { }

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
            { }

            return result;
        }

        public int GetSyncroDeviceSubAddress()
        {
            try
            {
                for (int i = 1; i <= 6; i++)
                {
                    string line = Ip[i];

                    if (string.IsNullOrEmpty(line))
                        continue;

                    if (line.IndexOf("ADR=", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string tString = line.Length >= 11 ? line.Substring(0, 11) : line;

                        // find the dot
                        int dotIndex = tString.IndexOf('.');

                        if (dotIndex >= 0 && dotIndex < tString.Length - 1)
                        {
                            // everything after the dot
                            string numberPart = tString.Substring(dotIndex + 1);

                            if (int.TryParse(numberPart, out int subAddress))
                                return subAddress;
                        }

                        return 0;   // default if no dot or not numeric
                    }
                }
            }
            catch
            {}

            return 0;   // default
        }


        public void AddToZoneDisableList(int piNode, int piZone, ref int piDeviceAddress, ref int piLoop)
        {
            bool inputNumberFound = false;
            int listCount = garyzoneDisableList.Count;

            // Set loop number (0-based)
            piLoop = (listCount / 255);
            string sPanelZone = piNode.ToString("00") + piZone.ToString("000") + piLoop.ToString("000");

            // Check if already in list
            for (int i = 0; i < listCount; i++)
            {
                if (garyzoneDisableList[i] == sPanelZone)
                {
                    inputNumberFound = true;
                    break;
                }
            }

            if (!inputNumberFound)
            {
                garyzoneDisableList.Add(sPanelZone);
                this.NotifyClient($"Add to Zone Disable array: Count {listCount}, InputNumber: {sPanelZone}");
                piDeviceAddress = listCount + 1;
            }
            else
            {
                // Already added
                piDeviceAddress = -1;
            }

            this.NotifyClient($"Zone Disable Array: {sPanelZone}");
            this.NotifyClient($"Add to Zone Disable List Count: {garyzoneDisableList.Count}");
        }

        public void RemoveFromZoneDisableList(int piNode, int piZone, ref int piDeviceAddress, ref int piLoop)
        {
            int listCount = garyzoneDisableList.Count;

            // Set loop number (0-based)
            piLoop = (listCount / 255);
            string sPanelZone = piNode.ToString("00") + piZone.ToString("000") + piLoop.ToString("000");

            // Search and remove
            for (int i = 0; i < listCount; i++)
            {
                if (garyzoneDisableList[i] == sPanelZone)
                {
                    garyzoneDisableList[i] = ""; // mark as cleared
                    piDeviceAddress = i + 1;
                    this.NotifyClient($"Zone Disable Found in list - Index/Device Address = {piDeviceAddress}");
                    break;
                }
            }

            // Clean up empty entries
            garyzoneDisableList.RemoveAll(s => string.IsNullOrEmpty(s));

            if (garyzoneDisableList.Count == 0)
            {
                this.NotifyClient("- Zone Disable List empty");
            }
        }

        private void send_response_amx_and_serial(int evnum, string message1, string message2, string message3 = "")
        {
            string friendlymessage = message2 + (message3.Length > 0 ? (" " + message3) : "");

            // Signal the event back to the main service, so that it can be logged
            this.NotifyClient(friendlymessage, false);

            CSAMXSingleton.CS.SendAlarmToAMX(evnum, message1, message2, message3);
            CSAMXSingleton.CS.FlushMessages();
        }
        private void send_response_amx_disable(int evnum, string message1, string message2, string message3 = "")
        {
            string friendlymessage = message2 + (message3.Length > 0 ? (" " + message3) : "");

            // Signal the event back to the main service, so that it can be logged
            this.NotifyClient(friendlymessage, false);

            CSAMXSingleton.CS.SendAlarmToAMX_disable(evnum, message1, message2, message3);
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

            // send_message(ActionType.KHandShake, NwmData.AlarmToAmx, "0,0,0,0");
        }

        public override void StartUp(int fakemode)
        {
            int setttingbaudrate = base.GetSetting<int>(ksettingsyncrosection, "BaudRate");
            string settingparity = base.GetSetting<string>(ksettingsyncrosection, "Parity");
            int settingdatabits = base.GetSetting<int>(ksettingsyncrosection, "DataBits");
            int settingstopbits = base.GetSetting<int>(ksettingsyncrosection, "StopBits");

            // Load the User Message List
            for (int n = 0; n < 16; n++)
            {
                int idx = n + 1;

                UserMessages[n] = base.GetSetting<string>(ksettingsetupsection, $"UserText{idx}");

                string typeString = base.GetSetting<string>(ksettingsetupsection, $"UserType{idx}");
                UserTypes[n] = int.TryParse(typeString, out int value) ? value : 0;

                if (UserTypes[n] < 0 || UserTypes[n] > 16)
                {
                    UserTypes[n] = 0;
                }
            }

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
            send_message(ActionType.KANALOGUEDATA, NwmData.IsolationToAmx, passedvalues);
        }
        private void send_message(ActionType action, NwmData type, string passedvalues)
        {
            string[] parts = passedvalues.Split(',');

            int node = 1, loop = 0, zone = 0, device = 0, inputtype = 0;

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

            //string text = action.ToString();
            string text = "";

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
                inputtype = 15;
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
                inputtype = 15;
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
                gbaryDataToTX[8] = (loop - 1).ToString();

                string sChecksum = makechecksum(gbaryDataToTX);
                gbaryDataToTX[9] = sChecksum;
                inputtype = 4;
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
                gbaryDataToTX[8] = (loop - 1).ToString();

                string sChecksum = makechecksum(gbaryDataToTX);
                gbaryDataToTX[9] = sChecksum;
                inputtype = 4;
                on = false;
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
                inputtype = 8;
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
                inputtype = 8;
                on = false;
            }

            if (action == ActionType.KANALOGUEDATA)
            {
                gbaryDataToTX = new string[8];
                gbaryDataToTX[0] = "219";
                gbaryDataToTX[1] = GetNextMsgID().ToString();
                gbaryDataToTX[2] = node.ToString();
                gbaryDataToTX[3] = "68";
                gbaryDataToTX[4] = "2";
                gbaryDataToTX[5] = (loop - 1).ToString();
                gbaryDataToTX[6] = device.ToString();

                giAnalogRequestLoop = (loop - 1);

                string sChecksum = makechecksum(gbaryDataToTX);
                gbaryDataToTX[7] = sChecksum;

                base.NotifyClient("Send Analogue to Syncro Node " + node + " Loop " + (loop - 1) + " Device " + device, false);
            }

            serialsendstring(gbaryDataToTX);

            if (action != ActionType.KANALOGUEDATA)
            {
                node = node + this.Offset;
                SendEvent("Syncro", type, inputtype, text, on, node, loop, device);
            }
        }

        public int GetNextMsgID()
        {
            miMsgID++;

            if (miMsgID > 170)
                miMsgID = 1;

            return miMsgID;
        }

        public int CheckUserMessages(string evtString)
        {
            for (int n = 0; n < 16; n++)
            {
                if (!string.IsNullOrEmpty(UserMessages[n]))
                {
                    if (UserMessages[n] == evtString)
                    {
                        return UserTypes[n];
                    }
                }
            }

            // Default
            return -1;
        }

        private readonly List<byte> _buffer = new List<byte>();
        private readonly byte[] _terminator = { 0x0D, 0x0A, 0x0D, 0x0A }; // \r\n\r\n

        public override void SerialPort_Datareceived(object sender, SerialDataReceivedEventArgs e)
        {
            Thread.Sleep(500); // wait for more data
            int bytesToRead = serialport.BytesToRead;
            if (bytesToRead <= 0) return;

            byte[] incoming = new byte[bytesToRead];
            int read = serialport.Read(incoming, 0, bytesToRead);
            if (read <= 0) return;

            lock (_buffer)
            {
                _buffer.AddRange(incoming);
                ExtractMessages();
            }
        }

        private void ExtractMessages()
        {
            while (true)
            {
                int pos = FindPattern(_buffer, _terminator);
                if (pos == -1)
                {
                    // Now deal with specific message types
                    if (_buffer.Count >= 4 && _buffer[3].ToString() == "68")
                    {
                        int DeviceAnalogueValue = _buffer[7];
                        int deviceNode = _buffer[2];
                        int DeviceLoop = giAnalogRequestLoop + 1;
                        base.NotifyClient("Analogue Node Received: " + deviceNode, false);
                        base.NotifyClient("Analogue Address Received: " + _buffer[6], false);
                        base.NotifyClient("Analogue Value Received: " + DeviceAnalogueValue, false);
                        string sLavFileName = GetAnalogStoreName(deviceNode, DeviceLoop);
                    }
                    else
                    {
                        if (_buffer.Count >= 5 && _buffer[4].ToString() == "68")
                        {
                            int DeviceAnalogueValue = _buffer[8];
                            int deviceNode = _buffer[2];
                            int DeviceLoop = giAnalogRequestLoop + 1;
                            base.NotifyClient("Analogue Node Received: " + _buffer[3], false);
                            base.NotifyClient("Analogue Address Received: " + _buffer[7], false);
                            base.NotifyClient("Analogue Value Received: " + DeviceAnalogueValue, false);
                            string sLavFileName = GetAnalogStoreName(deviceNode, DeviceLoop);
                        }
                        else
                        {
                            return;  // no complete message yet
                        }
                    }
                }

                int end = pos + _terminator.Length;
                byte[] message = _buffer.Take(end).ToArray();

                _buffer.RemoveRange(0, end);
                Parse(message);
            }
        }

        private int FindPattern(List<byte> buffer, byte[] pattern)
        {
            for (int i = 0; i <= buffer.Count - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (buffer[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        public string GetAnalogStoreName(int iNode, int iLoop)
        {
            return $@"\History\Analog\{iNode:000#}{iLoop:00#}.LAV";
        }

    }
}