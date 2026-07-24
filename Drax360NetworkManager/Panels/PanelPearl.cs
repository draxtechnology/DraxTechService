using System;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace DraxTechnology.Panels
{
    internal class PanelPearl : AbstractPanelId3k
    {
        public override string FakeString =>
            /* Pearl
            >IS0001C000000000000BE7\r
            >IE0220611450330000000BDD\r
            >IE0102411527000100001S01030000000000"OFFICE P1?DEV ROOM ZONE 1"1A7\r */
            ">IE0220611450330000000BDD\r";
        public override string PanelVersion => "1.0.0.0";

        public PanelPearl(string baselogfolder, string identifier)
            : base(baselogfolder, identifier, "PRLMan", "PRL")
        {
            gbHalfDuplex           = false;
            UseHalfDuplexGatedSend = false;

            if (!String.IsNullOrEmpty(identifier))
            {
                heartbeat_timer = new Timer(heartbeat_timer_callback, this.Identifier, 1000, kHeartbeatDelaySeconds * 1000);
            }
        }

        // ----------------------------------------------------------------
        // Pearl adds case 26 (UnmonitoredRelayOutput) to the device-type table
        // ----------------------------------------------------------------
        public override void GetDeviceTypeText(int piDeviceType)
        {
            base.GetDeviceTypeText(piDeviceType);
            if (piDeviceType == 26)
            {
                gDeviceType  = EnmDeviceType.UnmonitoredRelayOutput;
                gsDeviceText = "Unmonitored Relay Output";
            }
        }

        // ----------------------------------------------------------------
        // Pearl-specific events — handled before the shared switch
        // ----------------------------------------------------------------
        protected override bool HandlePanelSpecificEvent(int eventcode, Id3kParseState st, ref bool on)
        {
            switch ((enmPRLEventType)eventcode)
            {
                case enmPRLEventType.FireDisabled:
                    st.gsTextField = "Fire Disabled";
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    st.gAlarmType  = enmPRLAlarmType.TestModeFire.ToString();
                    // Pearl does NOT set bDontSendToAMX for FireDisabled (Notifier does)
                    return true;

                case enmPRLEventType.FireCleared:
                    st.gAlarmType  = enmPRLAlarmType.Fire.ToString();
                    st.gsTextField = "Fire Cleared";
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    // Pearl does NOT set bDontSendToAMX for FireCleared (Notifier does)
                    return true;

                case enmPRLEventType.MissingCleared:
                    st.gAlarmType  = enmPRLAlarmType.Fault.ToString();
                    st.gsTextField = "Missing Cleared";
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    // Pearl does NOT set bDontSendToAMX for MissingCleared (Notifier does)
                    return true;

                case enmPRLEventType.Deviceenabled:
                    Console.WriteLine(DateTime.Now + ": Device " + (int)st.giAddressNumber + " Enabled");
                    st.gAlarmType  = enmPRLAlarmType.Isolate.ToString();
                    st.gsTextField = "Device " + (int)st.giAddressNumber + " Enabled";
                    on = false;
                    return true;

                case enmPRLEventType.Devicedisabled:
                    st.gAlarmType  = enmPRLAlarmType.Isolate.ToString();
                    st.gsTextField = "Device " + (int)st.giAddressNumber + " Disabled";
                    Console.WriteLine(DateTime.Now + ": Device " + (int)st.giAddressNumber + " Disabled");
                    return true;

                case enmPRLEventType.SounderEnabled:  // 169 — Pearl: StatusEvent/addr=6; Notifier: NOTIsolate
                    st.gAlarmType       = enmPRLAlarmType.StatusEvent.ToString();
                    st.giAddressNumber  = 6;
                    st.gsTextField      = "Sounder Enabled";
                    return true;

                case enmPRLEventType.SounderDisabled:  // 159 — Pearl: StatusEvent/addr=6; Notifier: NOTIsolate
                    st.gAlarmType       = enmPRLAlarmType.StatusEvent.ToString();
                    st.giAddressNumber  = 6;
                    st.gsTextField      = "Sounder Disabled";
                    return true;

                case enmPRLEventType.PowerRestart:  // 146
                    st.gAlarmType       = enmPRLAlarmType.StatusEvent.ToString();
                    st.giAddressNumber  = 98;
                    st.gsTextField      = "Power Restart";
                    st.bOneShotReset    = true;
                    return true;

                case enmPRLEventType.Evacuate:  // 138 — Pearl does NOT set bOneShotReset (Notifier does)
                    st.gAlarmType       = enmPRLAlarmType.StatusEvent.ToString();
                    st.giAddressNumber  = 1;
                    st.gsTextField      = "Evacuate";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmPRLEventType.EnableZone:  // 136 — Pearl uses DisableZone alarm type
                    st.gAlarmType  = enmPRLAlarmType.DisableZone.ToString();
                    st.gsTextField = "Zone " + st.zone + " Enabled";
                    on = false;
                    Console.WriteLine(DateTime.Now + ": Zone " + st.zone + " Enabled");
                    return true;

                case enmPRLEventType.DisableZone:  // 137 — Pearl uses DisableZone alarm type
                    st.gAlarmType  = enmPRLAlarmType.DisableZone.ToString();
                    st.gsTextField = "Zone " + st.zone + " Disabled";
                    Console.WriteLine(DateTime.Now + ": Zone " + st.zone + " Disabled");
                    return true;

                // Pearl suppresses only these three (Notifier suppresses more)
                case enmPRLEventType.NetworkSilenceSounders:  // 154
                case enmPRLEventType.NetworkZoneInFault:      // 199
                case enmPRLEventType.NetworkZoneInTest:       // 194
                    st.bDontSendToAMX = true;
                    return true;

                default:
                    return false;
            }
        }

        // ----------------------------------------------------------------
        // send_message — Pearl wire format: body + \r only (no checksum appended)
        // ----------------------------------------------------------------
        public override void send_message(ActionType action, string passedvalues)
        {
            ParsePassedValues(passedvalues, out int node, out int loop, out int zone, out int device);

            DateTime now       = DateTime.Now;
            int iDayOfWeek     = (int)now.DayOfWeek + 1;
            string sHH         = now.Hour.ToString("D2");
            string sMM         = now.Minute.ToString("D2");
            string sSS         = now.Second.ToString("D2");

            string message = "";

            if (action == ActionType.kEVACTUATE)
                message = $">IE00138{iDayOfWeek}{sHH}{sMM}{sSS}{loop:D2}{zone:D5}";

            if (action == ActionType.kRESET)
                message = $">IE{node:D2}129{iDayOfWeek}{sHH}{sMM}{sSS}{loop:D2}{zone:D5}";

            if (action == ActionType.kRESETNETWORK)
                message = $">IE00129{iDayOfWeek}{sHH}{sMM}{sSS}{loop:D2}{zone:D5}";

            if (action == ActionType.kSILENCE)
                message = $">IE{node:D2}131{iDayOfWeek}{sHH}{sMM}{sSS}{loop:D2}{zone:D5}";

            // Pearl DISABLE/ENABLE DEVICE appends trailing "00" (Notifier does not)
            if (action == ActionType.kDISABLEDEVICE)
                message = $">IE{node:D2}024{iDayOfWeek}{sHH}{sMM}{sSS}{loop:D2}{zone:D5}S{device:D2}00";

            if (action == ActionType.kENABLEDEVICE)
                message = $">IE{node:D2}023{iDayOfWeek}{sHH}{sMM}{sSS}{loop:D2}{zone:D5}S{device:D2}00";

            if (action == ActionType.kDISABLEMODULE)
                message = $">IE{node:D2}024{iDayOfWeek}{sHH}{sMM}{sSS}{loop:D2}{zone:D5}M{device:D2}00";

            if (action == ActionType.kENABLEMODULE)
                message = $">IE{node:D2}023{iDayOfWeek}{sHH}{sMM}{sSS}{loop:D2}{zone:D5}M{device:D2}00";

            if (action == ActionType.kDISABLEZONE)
                message = $">IE00137{iDayOfWeek}{sHH}{sMM}{sSS}00{zone:D5}";

            if (action == ActionType.kENABLEZONE)
                message = $">IE00136{iDayOfWeek}{sHH}{sMM}{sSS}00{zone:D5}";

            if (string.IsNullOrEmpty(message)) return;

            // Pearl does not append a checksum — just terminate with \r
            string frame = message + "\r";

            foreach (char ch in frame)
                SendChar(ch);

            Console.WriteLine(DateTime.Now + ": " + frame.Replace("\r", "") + " Sent to panel");
        }

        // Extended Device Status Request (099-048 section 3.3.4.1). The panel
        // answers asynchronously with ">ISE", decoded in AbstractPanelId3k.
        // Full-duplex request format: no checksum, just the \r terminator.
        public override void Analogue(string passedvalues)
        {
            ParsePassedValues(passedvalues, out int node, out int loop, out _, out int device);
            string frame = Id3kExtendedDeviceStatus.BuildStatusRequestBody(node, loop, device) + "\r";

            foreach (char ch in frame)
                SendChar(ch);

            Console.WriteLine(DateTime.Now + ": " + frame.Replace("\r", "") + " Sent to panel");
        }
    }
}
