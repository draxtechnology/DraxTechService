using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace DraxTechnology.Panels
{
    internal class PanelNotifier : AbstractPanelId3k
    {
        // Device addresses ≥ this are modules rather than physical sensor devices.
        // The disable/enable remap in send_message uses it as the gate, and the
        // >IE…M.. wire format subtracts it to get the module index.
        private const int kModuleAddressMin = 100;

        private readonly List<(int zone, int p2, int p3, int p4, int p1)> _disabledZones = new();

        public override string FakeString =>
            /* Notifier
            >IS0001C000000000000BE7\r
            >IE0220611450330000000BDD\r
            >IE0102411527000100001S01030000000000"OFFICE P1?DEV ROOM ZONE 1"1A7\r */
            ">IE0220611450330000000BDD\r";
        public override string PanelVersion => "1.0.0.0";

        public PanelNotifier(string baselogfolder, string identifier)
            : base(baselogfolder, identifier, "NOTMan", "NOT")
        {
            gbHalfDuplex = false;
            UseHalfDuplexGatedSend = true;

            if (!String.IsNullOrEmpty(identifier))
            {
                heartbeat_timer = new Timer(heartbeat_timer_callback, this.Identifier, 1000, kHeartbeatDelaySeconds * 1000);
            }
        }

        // ----------------------------------------------------------------
        // Notifier-specific events — handled before the shared switch
        // ----------------------------------------------------------------
        protected override bool HandlePanelSpecificEvent(int eventcode, Id3kParseState st, ref bool on)
        {
            switch ((enmNotEventType)eventcode)
            {
                case enmNotEventType.FireDisabled:
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    st.gAlarmType     = enmNotAlarmType.NOTTestModeFire.ToString();
                    st.bDontSendToAMX = true;
                    st.getDeviceText  = false;
                    return true;

                case enmNotEventType.FireCleared:
                    st.gAlarmType     = enmNotAlarmType.NOTFire.ToString();
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    st.getDeviceText  = false;
                    st.bDontSendToAMX = true;
                    return true;

                case enmNotEventType.MissingCleared:
                    st.gAlarmType     = enmNotAlarmType.NOTFault.ToString();
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    st.getDeviceText  = false;
                    st.bDontSendToAMX = true;
                    return true;

                case enmNotEventType.Deviceenabled:
                    Console.WriteLine(DateTime.Now + ": " + "Device " + (int)st.giAddressNumber + " Enabled");
                    st.gAlarmType  = enmNotAlarmType.NOTIsolate.ToString();
                    // st.gsTextField already holds the parsed text field from the frame (sTextField).
                    // Original VB / Notifier code set a synthetic string then overwrote with sTextField;
                    // the state bag is pre-populated with sTextField so we just leave it.
                    on = false;
                    return true;

                case enmNotEventType.Devicedisabled:
                    st.gAlarmType  = enmNotAlarmType.NOTIsolate.ToString();
                    // st.gsTextField already holds sTextField — leave it (same VB overwrite pattern).
                    Console.WriteLine(DateTime.Now + ": " + "Device " + (int)st.giAddressNumber + " Disabled");
                    return true;

                case enmNotEventType.SounderDisabled:  // 159
                    st.gAlarmType = enmNotAlarmType.NOTIsolate.ToString();
                    st.gsTextField = "Sounder Disabled";
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SounderEnabled:  // 169
                    st.gAlarmType = enmNotAlarmType.NOTIsolate.ToString();
                    st.gsTextField = "Sounder Enabled";
                    on = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PowerRestart:  // 146
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Power Restart";
                    st.getDeviceText    = false;
                    st.giAddressNumber  = 98;
                    st.bOneShotReset    = true; // VB NOTNetManager.bas:2074
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.Evacuate:  // 138
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 1;
                    st.gsTextField      = "Evacuate";
                    st.getDeviceText    = false;
                    st.bOneShotReset    = true;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.EnableZone:  // 136
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Entire Zone " + st.zone + " Enabled";
                    st.getDeviceText    = false;
                    st.loop             = 15;
                    on                  = false;
                    st.giAddressNumber  = st.zone;
                    Console.WriteLine(DateTime.Now + ": " + "Entire Zone " + st.zone + " Enabled");
                    return true;

                case enmNotEventType.DisableZone:  // 137
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Entire Zone " + st.zone + " Disabled";
                    st.getDeviceText    = false;
                    st.loop             = 15;
                    st.giAddressNumber  = st.zone;
                    Console.WriteLine(DateTime.Now + ": " + "Entire Zone " + st.zone + " Disabled");
                    return true;

                case enmNotEventType.SystemDayMode:  // 172
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 7;
                    st.gsTextField      = "System Day Mode";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SystemNightMode:  // 173
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 71;
                    st.gsTextField      = "System Night Mode";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                // ReSoundSounder — Notifier does NOT set bOneShotReset (Pearl does, via base)
                case enmNotEventType.ReSoundSounder:  // 157
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 29;
                    st.gsTextField      = "Re-Sound Sounders";
                    st.getDeviceText    = false;
                    // bOneShotReset intentionally NOT set (base would set it for Pearl)
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                // Notifier suppresses these network-status events (Pearl only suppresses a subset)
                case enmNotEventType.NetworkGeneralReset:
                case enmNotEventType.NetworkSilenceSounders:
                case enmNotEventType.NetworkGeneralMuteSounder:
                case enmNotEventType.NetworkZoneInTestMode:
                case enmNotEventType.NetworkZoneInTest:
                case enmNotEventType.NetworkZoneInFault:
                    st.bDontSendToAMX = true;
                    return true;

                // Notifier-specific relay/sounder-circuit faults not in shared switch
                case enmNotEventType.SounderCircuit1RelayFault:
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 84;
                    st.gsTextField      = "Sounder Circuit 1 Relay";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SounderCircuit2RelayFault:
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 85;
                    st.gsTextField      = "Sounder Circuit 2 Relay";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                default:
                    return false;
            }
        }

        // ----------------------------------------------------------------
        // Post-switch: Notifier's _disabledZones bookkeeping
        // ----------------------------------------------------------------
        protected override void HandlePostSwitchDispatch(
            int evnum, Id3kParseState st, int p1, bool on,
            decimal zone, string zonetext, int eventcode,
            int p2, int p3, int p4)
        {
            // Shared isolate double-send
            base.HandlePostSwitchDispatch(evnum, st, p1, on, zone, zonetext, eventcode, p2, p3, p4);

            // Track DisableZone so EnableZone can send AMX resets for each disabled device
            if ((enmNotEventType)eventcode == enmNotEventType.DisableZone)
                _disabledZones.Add(((int)zone, p2, p3, p4, p1));

            if ((enmNotEventType)eventcode == enmNotEventType.EnableZone)
            {
                foreach (var entry in _disabledZones.Where(e => e.zone == (int)zone).ToList())
                {
                    if (!st.bDontSendToAMX)
                    {
                        int offEvnum = CSAMXSingleton.CS.MakeInputNumber(entry.p2, entry.p3, entry.p4, entry.p1, false);
                        CSAMXSingleton.CS.SendResetToAMX(offEvnum, st.gsTextField, "", "");
                    }
                }
                _disabledZones.RemoveAll(e => e.zone == (int)zone);
            }
        }

        // ----------------------------------------------------------------
        // send_message — Notifier wire format: body + CRC + \r, via HalfDuplexSend
        // ----------------------------------------------------------------
        public override void send_message(ActionType action, string passedvalues)
        {
            ParsePassedValues(passedvalues, out int node, out int loop, out int zone, out int device);

            // Diagnostic — confirm the module gate receives the right device value.
            // Remove once module-disable path is verified end-to-end.
            this.NotifyClient($"send_message: action={action} device={device}");

            if (device >= kModuleAddressMin)
            {
                action = action switch
                {
                    ActionType.kDISABLEDEVICE => ActionType.kDISABLEMODULE,
                    ActionType.kENABLEDEVICE  => ActionType.kENABLEMODULE,
                    _                          => action,
                };
            }

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

            if (action == ActionType.kDISABLEDEVICE)
                message = $">IE{node:D2}024{iDayOfWeek}{sHH}{sMM}{sSS}{loop:D2}{zone:D5}S{device:D2}";

            if (action == ActionType.kENABLEDEVICE)
                message = $">IE{node:D2}023{iDayOfWeek}{sHH}{sMM}{sSS}{loop:D2}{zone:D5}S{device:D2}";

            if (action == ActionType.kDISABLEMODULE)
                message = $">IE{node:D2}024{iDayOfWeek}{sHH}{sMM}{sSS}{loop:D2}{zone:D5}M{(device - kModuleAddressMin):D2}";

            if (action == ActionType.kENABLEMODULE)
                message = $">IE{node:D2}023{iDayOfWeek}{sHH}{sMM}{sSS}{loop:D2}{zone:D5}M{(device - kModuleAddressMin):D2}";

            if (action == ActionType.kDISABLEZONE)
                message = $">IE00137{iDayOfWeek}{sHH}{sMM}{sSS}00{zone:D5}";

            if (action == ActionType.kENABLEZONE)
                message = $">IE00136{iDayOfWeek}{sHH}{sMM}{sSS}00{zone:D5}";

            if (string.IsNullOrEmpty(message)) return;

            // VB6 computes checksum over the body without the leading ">"
            string sChecksum = CreateNOTChecksum(message.Substring(1));
            string frame = message + sChecksum + "\r";

            if (UseHalfDuplexGatedSend)
                HalfDuplexSend(frame);
            else
                serialsend(frame);

            Console.WriteLine(DateTime.Now + ": " + frame.Replace("\r", "") + " Sent to panel");
        }
    }
}
