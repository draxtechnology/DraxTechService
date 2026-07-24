using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace DraxTechnology.Panels
{
    // Base class for the Notifier ID3000 / Pearl family of panels, which share
    // the same ID3K serial protocol. Subclasses override HandlePanelSpecificEvent
    // for any events that diverge between panel types, and override send_message
    // because the wire format differs (Notifier appends a checksum, Pearl does not).
    internal abstract class AbstractPanelId3k : AbstractPanel
    {
        // ----------------------------------------------------------------
        // Shared fields
        // ----------------------------------------------------------------
        protected bool gbHalfDuplex = false;
        protected bool gbSectoring = false;
        protected int gsSectorNo;
        protected string gsDeviceText = "";
        protected EnmDeviceType gDeviceType;

        // ----------------------------------------------------------------
        // Parse-state bag — populated during Parse, read by post-switch dispatch
        // ----------------------------------------------------------------
        protected sealed class Id3kParseState
        {
            public string  gAlarmType       = "";
            public decimal giAddressNumber  = 0;
            public string  gsTextField      = "";
            public bool    getDeviceText    = true;
            public bool    bDontSendToAMX  = false;
            public bool    bOneShotReset   = false;
            public int     loop;
            public decimal zone;
        }

        // ----------------------------------------------------------------
        // Constructor — forwards to AbstractPanel
        // ----------------------------------------------------------------
        protected AbstractPanelId3k(string baselogfolder, string identifier, string inifile, string extension)
            : base(baselogfolder, identifier, inifile, extension)
        {
            if (!string.IsNullOrEmpty(identifier))
                InitAnalogueStore();
        }

        // ----------------------------------------------------------------
        // Parse — shared ID3K frame decode
        // ----------------------------------------------------------------
        public override void Parse(byte[] buffer)
        {
            base.Parse(buffer);

            byte[] ourmessage = this.buffer.ToArray();
            int foundat = -1;
            for (int i = 0; i < ourmessage.Length; i++)
            {
                if (ourmessage[i] == '\r') { foundat = i; break; }
            }
            if (foundat <= 0) return;
            this.buffer.Clear();

            string strmsg = Encoding.UTF8.GetString(ourmessage, 0, foundat);
            if (!strmsg.StartsWith(">")) return;
            string cmd = strmsg.Substring(1, 2);

            Console.WriteLine(DateTime.Now + ": " + strmsg.Replace("\r", "") + " Received from Panel");

            if (cmd == "IS")
            {
                string ack = ">IACK\r";
                foreach (char ch in ack) SendChar(ch);
                Console.WriteLine(DateTime.Now + ": " + ">IACK Sent to Panel");
            }

            // Extended Device Status response (099-048 section 3.3.4.3). Its
            // ">IS" prefix means the IACK branch above has already answered it.
            // Per the document this arrives on Pearl / protocol version 0013
            // panels only.
            if (strmsg.StartsWith(">ISE") && Id3kExtendedDeviceStatus.TryParse(strmsg, out Id3kExtendedDeviceStatus extStatus))
                HandleExtendedDeviceStatus(extStatus);

            if (cmd != "IE") return;

            // ---- Field extraction (1-based offsets from VB, converted to 0-based) ----
            int panel     = Convert.ToInt32(Encoding.UTF8.GetString(ourmessage, 3, 2));  // pos 4-5
            int eventcode = Convert.ToInt32(Encoding.UTF8.GetString(ourmessage, 5, 3));  // pos 6-8
            // dayofweek / hours / minutes / seconds parsed but not routed
            int loop      = Convert.ToInt32(Encoding.UTF8.GetString(ourmessage, 15, 2)); // pos 16-17
            decimal zone  = 0;
            decimal.TryParse(Encoding.UTF8.GetString(ourmessage, 17, 5), out zone);      // pos 18-22

            string sensor = Encoding.UTF8.GetString(ourmessage, 22, 1);                  // pos 23
            int address   = 0;
            int.TryParse(
                Encoding.UTF8.GetString(ourmessage, 23, 2),                               // pos 24-25
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out address);

            int iDevicetype = 0;
            if (ourmessage.Length > 26)
                int.TryParse(Encoding.UTF8.GetString(ourmessage, 25, 2), out iDevicetype); // pos 26-27

            // ---- Sectoring ---------------------------------------------------
            if (CheckForSectoring(eventcode, loop))
            {
                gsSectorNo = loop;
                loop = 0;
                gbSectoring = true;
            }
            else
            {
                gsSectorNo = 0;
                gbSectoring = false;
            }

            gsDeviceText = "";
            if (!gbSectoring)
                GetDeviceTypeText(iDevicetype);

            // ---- Checksum (informational only — not gating dispatch) ----------
            string sChecksum;
            if (gbHalfDuplex)
            {
                sChecksum  = Encoding.UTF8.GetString(new byte[] { ourmessage[ourmessage.Length - 4] });
                sChecksum += Encoding.UTF8.GetString(new byte[] { ourmessage[ourmessage.Length - 3] });
                sChecksum += Encoding.UTF8.GetString(new byte[] { ourmessage[ourmessage.Length - 2] });
                sChecksum += Encoding.UTF8.GetString(new byte[] { ourmessage[ourmessage.Length - 1] });
            }
            else
            {
                sChecksum  = Encoding.UTF8.GetString(new byte[] { ourmessage[ourmessage.Length - 2] });
                sChecksum += Encoding.UTF8.GetString(new byte[] { ourmessage[ourmessage.Length - 1] });
            }
            bool bValidChecksum = CheckSumValidation(sChecksum, ourmessage);

            // ---- Text field --------------------------------------------------
            string sTextField = ExtractTextField(ourmessage);

            // ---- State bag ---------------------------------------------------
            var st = new Id3kParseState
            {
                gsTextField     = sTextField,
                giAddressNumber = address,
                loop            = loop,
                zone            = zone,
            };
            bool on = true;

            // ---- Event dispatch ----------------------------------------------
            bool handledByBase = HandleSharedEvent(eventcode, st, ref on);
            if (!handledByBase)
            {
                bool handledBySubclass = HandlePanelSpecificEvent(eventcode, st, ref on);
                if (!handledBySubclass)
                    base.NotifyClient("Unknown Event " + ((enmNotEventType)eventcode));
            }

            // Carry loop/zone mutations back from state bag
            loop = st.loop;
            zone = st.zone;

            Console.WriteLine(DateTime.Now + ": Event " + eventcode + " Zone " + zone + " Address " + st.giAddressNumber);

            gsDeviceText = "";
            if (st.getDeviceText && !gbSectoring)
                GetDeviceTypeText(iDevicetype);

            // ---- Alarm-type resolution --------------------------------------
            // enmNotAlarmType and enmPRLAlarmType have identical numeric values.
            // We resolve against enmNotAlarmType first; if that fails (Pearl uses
            // the short-name form) we fall through to enmPRLAlarmType.
            if (string.IsNullOrEmpty(st.gAlarmType))
                st.gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();

            int p1;
            if (Enum.TryParse(st.gAlarmType, out enmNotAlarmType notValue))
            {
                p1 = (int)notValue;
            }
            else if (Enum.TryParse(st.gAlarmType, out enmPRLAlarmType prlValue))
            {
                p1 = (int)prlValue;
            }
            else
            {
                this.NotifyClient("gAlarmType " + st.gAlarmType + " not a valid alarm type", false);
                p1 = (int)enmNotAlarmType.NOTStatusEvent;
            }

            // Module-address inbound offset (sensor == 'M')
            if (sensor.ToLower() == "m")
                st.giAddressNumber += GetModuleAddressOffset();

            int p2 = panel + this.Offset;
            int p3 = loop;
            int p4 = Convert.ToInt32(st.giAddressNumber);

            string zonetext = zone > 0 ? "Zone " + zone : "";

            int evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1, on);

            if (!st.bDontSendToAMX)
            {
                HandlePostSwitchDispatch(evnum, st, p1, on, zone, zonetext, eventcode, p2, p3, p4);

                this.NotifyClient("Sending gsTextField: " + st.gsTextField + " gsDeviceText: " + gsDeviceText + " zonetext: " + zonetext, false);
                send_response_amx_and_serial(evnum, st.gsTextField, gsDeviceText, zonetext);
            }

            if (st.bOneShotReset && evnum != 0 && !st.bDontSendToAMX)
            {
                base.NotifyClient("OneShot - Force EVM Attribute 13");
                CSAMXSingleton.CS.ForceEvmAttribute(evnum, 13, 1);
                CSAMXSingleton.CS.FlushMessages();
            }
        }

        // ----------------------------------------------------------------
        // Text field extraction — handles the 0xFE terminator used by ID3K
        // ----------------------------------------------------------------
        private static string ExtractTextField(byte[] ourmessage)
        {
            if (ourmessage == null || ourmessage.Length <= 37)
                return "";

            int start = (ourmessage[36] == 254) ? 37 : 36;
            int end   = Array.IndexOf(ourmessage, (byte)254, start);
            if (end < 0) end = ourmessage.Length;

            string s = Encoding.UTF8.GetString(ourmessage, start, end - start);
            int quoteIndex = s.IndexOf('"');
            if (quoteIndex >= 0)
                s = s.Substring(0, quoteIndex).Trim();
            return s;
        }

        // ----------------------------------------------------------------
        // Shared event switch — events with identical behaviour on both panels
        // Returns true if handled here; false means caller should try the subclass hook.
        // ----------------------------------------------------------------
        private bool HandleSharedEvent(int eventcode, Id3kParseState st, ref bool on)
        {
            switch ((enmNotEventType)eventcode)
            {
                case enmNotEventType.Fire:
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    st.gAlarmType = enmNotAlarmType.NOTFire.ToString();
                    return true;

                case enmNotEventType.TestFire:
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    st.gAlarmType = enmNotAlarmType.NOTFire.ToString();
                    return true;

                case enmNotEventType.NoReplyMissing:
                    st.gAlarmType    = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField   = "Device Missing";
                    st.getDeviceText = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.TypeMisMatch:
                    st.gAlarmType    = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField   = "Type Mismatch";
                    st.getDeviceText = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PreAlarm:
                    st.gAlarmType    = enmNotAlarmType.NOTPreAlarm.ToString();
                    st.gsTextField   = "Pre Alarm";
                    st.getDeviceText = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.RemovedDisabled:
                    st.gAlarmType    = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField   = "Removed Under Disablement";
                    st.getDeviceText = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.FaultCleared:
                    st.gAlarmType    = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField   = "Fault Cleared";
                    st.getDeviceText = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SensorModuleFault:
                    st.gAlarmType    = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField   = "Sensor Fault";
                    st.getDeviceText = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.Deviceenabled:
                    Console.WriteLine(DateTime.Now + ": Device " + (int)st.giAddressNumber + " Enabled");
                    st.gAlarmType  = enmNotAlarmType.NOTIsolate.ToString();
                    st.gsTextField = "Device " + (int)st.giAddressNumber + " Enabled";
                    on = false;
                    return true;

                case enmNotEventType.Devicedisabled:
                    st.gAlarmType  = enmNotAlarmType.NOTIsolate.ToString();
                    st.gsTextField = "Device " + (int)st.giAddressNumber + " Disabled";
                    Console.WriteLine(DateTime.Now + ": Device " + (int)st.giAddressNumber + " Disabled");
                    return true;

                case enmNotEventType.SystemReset:
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Panel Reset";
                    st.giAddressNumber  = 9;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ModuleLoadShortCircuit:
                    st.gAlarmType    = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField   = "Module Load Short Circuit";
                    st.getDeviceText = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.OutputModuleTestDeActivation:
                    st.gAlarmType    = enmNotAlarmType.NOTOutputActivate.ToString();
                    st.gsTextField   = "Module Activation";
                    st.getDeviceText = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.OutputModuleTestActivation:
                    st.gAlarmType    = enmNotAlarmType.NOTOutputActivate.ToString();
                    st.gsTextField   = "Module DeActivation";
                    st.getDeviceText = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.DuplicateAddress:
                    st.gAlarmType    = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField   = "Duplicate Address";
                    st.getDeviceText = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.AUXSet:
                    st.gAlarmType    = enmNotAlarmType.NOTNonFireAlarm.ToString();
                    st.gsTextField   = "AUX Set";
                    st.getDeviceText = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.AuxCleared:
                    st.gAlarmType    = enmNotAlarmType.NOTNonFireAlarm.ToString();
                    st.gsTextField   = "Aux Cleared";
                    st.getDeviceText = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.TechnicalAlarm:
                    st.gAlarmType    = enmNotAlarmType.NOTNonFireAlarm.ToString();
                    st.gsTextField   = "Technical Alarm";
                    st.getDeviceText = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PowerSupplyFault:
                    st.gAlarmType    = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField   = "Power Supply Fault";
                    st.getDeviceText = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.LoopBoosterFault:
                    st.gAlarmType    = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField   = "Loop Booster Fault";
                    st.getDeviceText = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ThermalAlarm:
                    st.gAlarmType       = enmNotAlarmType.NOTNonFireAlarm.ToString();
                    st.giAddressNumber  = 0;
                    st.gsTextField      = "Thermal Alarm";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.TerminateTest:         // 130
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 96;
                    st.gsTextField      = "End Zone " + st.zone + " Test";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SilenceSounder:        // 131
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 10;
                    st.gsTextField      = "Alarms Silenced";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.MuteBuzzer:            // 132
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 18;
                    st.gsTextField      = "Internal Buzzer Muted";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.StartZoneTest:         // 135
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 96;
                    st.gsTextField      = "Start Zone " + st.zone + " Test";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SysClockAdjust:        // 139
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 30;
                    st.gsTextField      = "System Clock Adjust";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.EditChangesConfirmed:  // 140
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 31;
                    st.gsTextField      = "Edited Changes Confirmed";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SuspectedLoopBreak:    // 143
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Suspected Loop Break " + st.loop;
                    st.giAddressNumber  = 20 + st.loop;
                    st.loop             = 0;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.CommsFail:             // 147
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 32;
                    st.gsTextField      = "Comms Fail";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                // 099-048 3.5.2 panel/system events (added 2026-07-24).
                // Each event owns a unique AMX pseudo-address (110-213, clear
                // of the 0-102 legacy band); enable/disable style pairs share
                // one address, the clearing side sending on = false. 441
                // shares 67 with the existing Remote Fire Output Test (176).
                case enmNotEventType.AllViewSensorsReplacedOnLoop:  // 133
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "All VIEW Sensors Replaced, Loop " + st.loop;
                    st.giAddressNumber  = 110;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PasscodeWarning:  // 134
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Passcode Warning";
                    st.giAddressNumber  = 111;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ControlMatrixEntryCreated:  // 141
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Control Matrix Entry Created";
                    st.giAddressNumber  = 112;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ControlMatrixEntryDeleted:  // 142
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Control Matrix Entry Deleted";
                    st.giAddressNumber  = 113;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.EditedChangesCancelled:  // 144
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Edited Changes Cancelled";
                    st.giAddressNumber  = 114;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.TestActivationSounderRelayCircuit:  // 145
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Test Activation Sounder or Relay Circuit";
                    st.giAddressNumber  = 115;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.NetworkStationNameChanged:  // 151
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Network Station Name Changed";
                    st.giAddressNumber  = 116;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.NetworkConfigurationChanged:  // 152
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Network Configuration Changed";
                    st.giAddressNumber  = 117;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.RemoteInitiatedTestZone:  // 158
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Remote Initiated Test Zone " + st.zone;
                    st.giAddressNumber  = 118;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.RemoteFireOutputDisabled:  // 160
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Remote Fire Output Disabled";
                    st.giAddressNumber  = 119;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.MuteInternalBuzzer:  // 161
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Mute Internal Buzzer";
                    st.giAddressNumber  = 120;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ControlOutputsDisabled:  // 162
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Control Outputs Disabled";
                    st.giAddressNumber  = 121;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.InvestigationDelayExtendedCircuit:  // 164
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Investigation Delay Extended";
                    st.giAddressNumber  = 122;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.RemoteFireOutputActivated:  // 165
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Remote Fire Output Activated";
                    st.giAddressNumber  = 123;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ControlOutputsEnabled:  // 166
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Control Outputs Enabled";
                    st.giAddressNumber  = 121;
                    on                  = false;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.RemoteFireOutputEnabled:  // 168
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Remote Fire Output Enabled";
                    st.giAddressNumber  = 119;
                    on                  = false;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PanelMainCoverRemoved:  // 174
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Panel Main Cover Removed";
                    st.giAddressNumber  = 124;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PanelMainCoverReplaced:  // 175
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Panel Main Cover Replaced";
                    st.giAddressNumber  = 124;
                    on                  = false;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.RemoteFireOutputDeactivated:  // 177
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Remote Fire Output Deactivated";
                    st.giAddressNumber  = 123;
                    on                  = false;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.FireControlDevicesDisabled:  // 179
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Fire Control Devices Disabled";
                    st.giAddressNumber  = 125;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.FireControlDevicesEnabled:  // 180
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Fire Control Devices Enabled";
                    st.giAddressNumber  = 125;
                    on                  = false;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SSTDevicesDisabled:  // 187
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "SST Devices Disabled";
                    st.giAddressNumber  = 126;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SSTDevicesEnabled:  // 188
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "SST Devices Enabled";
                    st.giAddressNumber  = 126;
                    on                  = false;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PanelExpansionCoverRemoved:  // 189
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Panel Expansion Cover Removed";
                    st.giAddressNumber  = 127;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PanelExpansionCoverReplaced:  // 190
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Panel Expansion Cover Replaced";
                    st.giAddressNumber  = 127;
                    on                  = false;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.RepeaterCommsFail:  // 201
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Repeater Comms Fail";
                    st.giAddressNumber  = 128;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.TestDeactivationSounderRelayCircuit:  // 202
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Test De-activation Sounder or Relay Circuit";
                    st.giAddressNumber  = 115;
                    on                  = false;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.AllSoundersTestedOnLoop:  // 215
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "All Sounders Tested, Loop " + st.loop;
                    st.giAddressNumber  = 129;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.BackupFireLineActivated:  // 235
                    st.gAlarmType       = enmNotAlarmType.NOTFire.ToString();
                    st.gsTextField      = "Backup Fire Line Activated";
                    st.giAddressNumber  = 130;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SectorAssignmentError:  // 236
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Sector Assignment Error";
                    st.giAddressNumber  = 131;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PanelAskedToSuspendUnsolicited:  // 246
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Panel Asked to Suspend Unsolicited Messages";
                    st.giAddressNumber  = 132;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PanelAskedToResumeUnsolicited:  // 247
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Panel Asked to Resume Unsolicited Messages";
                    st.giAddressNumber  = 132;
                    on                  = false;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.MainCPUWatchdogOperated:  // 296
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Main CPU Watchdog Operated";
                    st.giAddressNumber  = 133;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.CPUEPROMChecksumError:  // 297
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "CPU EPROM Checksum Error";
                    st.giAddressNumber  = 134;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.CPUE2PROMMemoryWriteError:  // 298
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "CPU E2PROM Memory Write Error";
                    st.giAddressNumber  = 135;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.CPUFlashMemoryChecksumError:  // 299
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "CPU FLASH Memory Checksum Error";
                    st.giAddressNumber  = 136;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PrinterFault:  // 300
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Printer Fault";
                    st.giAddressNumber  = 137;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.CPUFlashMemoryWriteError:  // 301
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "CPU FLASH Memory Write Error";
                    st.giAddressNumber  = 138;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.CPUDisplayHardwareFault:  // 303
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "CPU/Display Hardware Fault";
                    st.giAddressNumber  = 139;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.BaseboardExpansionHardwareFault:  // 304
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Baseboard/Expansion Hardware Fault";
                    st.giAddressNumber  = 140;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.CPUWatchdogTimerFault:  // 305
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "CPU Watchdog Timer Fault";
                    st.giAddressNumber  = 141;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.CPUWatchdogNotEnabled:  // 320
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "CPU Watchdog Not Enabled";
                    st.giAddressNumber  = 142;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ClockSetToAfterAD2099:  // 321
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Clock Set to After AD2099";
                    st.giAddressNumber  = 143;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.CPUClockMonitorFailure:  // 322
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "CPU Clock Monitor Failure";
                    st.giAddressNumber  = 144;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.CPUIllegalInstruction:  // 323
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "CPU Illegal Instruction";
                    st.giAddressNumber  = 145;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PSUFaultCrowbarActive:  // 326
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "PSU Fault: Crowbar Active";
                    st.giAddressNumber  = 146;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ConfigurationNeedsExpansionCard:  // 327
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Configuration Needs Expansion Card";
                    st.giAddressNumber  = 147;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ConfigurationNeedsRS485Card:  // 328
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Configuration Needs RS485 Card";
                    st.giAddressNumber  = 148;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ConfigurationNeedsRS232Card:  // 329
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Configuration Needs RS232 Card";
                    st.giAddressNumber  = 149;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.CommsCardDisplaced:  // 330
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "RS232/RS485/Printer Card Displaced";
                    st.giAddressNumber  = 150;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PoweredOffDueToLowBattery:  // 331
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Powered Off Due to Low Battery";
                    st.giAddressNumber  = 151;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ExternalPSUDualTXPathFault:  // 333
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "External PSU Dual TX Path Fault";
                    st.giAddressNumber  = 152;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ExternalPSULowSystemVoltage:  // 334
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "External PSU Low System Voltage";
                    st.giAddressNumber  = 153;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ELIBCardMissingOrFault:  // 335
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "ELIB Card Missing or Fault, Loop " + st.loop;
                    st.giAddressNumber  = 154;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ELIBFlashMemoryWriteFail:  // 339
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "ELIB FLASH Memory Write Fail, Loop " + st.loop;
                    st.giAddressNumber  = 155;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ELIBDataDownloadFailed:  // 343
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "ELIB Data Download Failed, Loop " + st.loop;
                    st.giAddressNumber  = 156;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.MainCPUWatchdogOperatedCOP:  // 349
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Main CPU Watchdog Operated (COP)";
                    st.giAddressNumber  = 157;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.IncompatibleLIBCardInstalled:  // 351
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Incompatible LIB Card Installed, Loop " + st.loop;
                    st.giAddressNumber  = 158;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.IncompatibleLoopDeviceAndLIB:  // 366
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Incompatible Loop Device and LIB, Loop " + st.loop;
                    st.giAddressNumber  = 159;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PowerSupplySecondaryBackupFault:  // 369
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Power-Supply Secondary Backup Fault";
                    st.giAddressNumber  = 160;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.IncompatiblePanelNetworkZones:  // 370
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Incompatible Panel/Network Zones Combination";
                    st.giAddressNumber  = 161;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ID2netPrimaryCPUFault:  // 371
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "ID2net Primary CPU Fault";
                    st.giAddressNumber  = 162;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ID2netSecondaryCPUFault:  // 372
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "ID2net Secondary CPU Fault";
                    st.giAddressNumber  = 163;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ID2netPartialOpenShortCircuitFault:  // 373
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "ID2net Partial Open/Short Circuit Fault";
                    st.giAddressNumber  = 164;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ID2netPhaseReversalFault:  // 374
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "ID2net Phase Reversal Fault";
                    st.giAddressNumber  = 165;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ID2netChannelInversionFault:  // 375
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "ID2net Channel Inversion Fault";
                    st.giAddressNumber  = 166;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.IncompatibleBaudRateCombination:  // 376
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Incompatible Baud Rate Combination";
                    st.giAddressNumber  = 167;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SounderCircuitsCPUFault:  // 377
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Sounder Circuits CPU Fault";
                    st.giAddressNumber  = 168;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ExtinguishingSystemExternalFault:  // 378
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Extinguishing System External Fault";
                    st.giAddressNumber  = 169;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ExtinguishingSystemTXPathFault:  // 379
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Extinguishing System TX Path Fault";
                    st.giAddressNumber  = 170;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.TooManyCLIPAddresses:  // 380
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Too Many CLIP Addresses";
                    st.giAddressNumber  = 171;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.OPALAutoConfigIncomplete:  // 381
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "OPAL Auto-config Incomplete";
                    st.giAddressNumber  = 172;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SensorAtAddressOutOfRange:  // 382
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Sensor at Address Out-of-Range, Loop " + st.loop;
                    st.giAddressNumber  = 173;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ModuleAtAddressOutOfRange:  // 386
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Module at Address Out-of-Range, Loop " + st.loop;
                    st.giAddressNumber  = 174;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ID2netMessageDeliverFailure:  // 400
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "ID2net Message Delivery Failure";
                    st.giAddressNumber  = 175;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.WalkTestSoundersStart:  // 401
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Walk Test Sounders Start";
                    st.giAddressNumber  = 176;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.WalkTestNoSoundersStart:  // 402
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Walk Test No Sounders Start";
                    st.giAddressNumber  = 176;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.WalkTestAutoReset:  // 403
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Walk Test Auto Reset";
                    st.giAddressNumber  = 176;
                    on                  = false;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.WalkTestEnd:  // 404
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Walk Test End";
                    st.giAddressNumber  = 176;
                    on                  = false;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.MainsBrownOut:  // 405
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Mains Brown-out";
                    st.giAddressNumber  = 177;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PSUModuleFault:  // 406
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "PSU Module Fault";
                    st.giAddressNumber  = 178;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.AuxSupplyFault:  // 407
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Auxiliary Supply Fault";
                    st.giAddressNumber  = 179;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.MemoryLockOpen:  // 408
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Memory Lock Open";
                    st.giAddressNumber  = 180;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.DisableLocalInputs:  // 409
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Disable Local Inputs";
                    st.giAddressNumber  = 181;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.EnableLocalInputs:  // 410
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Enable Local Inputs";
                    st.giAddressNumber  = 181;
                    on                  = false;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PCEvent1:  // 411
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "PC Event 1";
                    st.giAddressNumber  = 182;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PCEvent2:  // 412
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "PC Event 2";
                    st.giAddressNumber  = 183;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PCEvent3:  // 413
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "PC Event 3";
                    st.giAddressNumber  = 184;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PCEvent4:  // 414
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "PC Event 4";
                    st.giAddressNumber  = 185;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PCEvent5:  // 415
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "PC Event 5";
                    st.giAddressNumber  = 186;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PCEvent6:  // 416
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "PC Event 6";
                    st.giAddressNumber  = 187;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PCEvent7:  // 417
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "PC Event 7";
                    st.giAddressNumber  = 188;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PCEvent8:  // 418
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "PC Event 8";
                    st.giAddressNumber  = 189;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.BatteryWiringFault:  // 419
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Battery Wiring Fault";
                    st.giAddressNumber  = 190;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PerformanceCardReadFault:  // 420
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Performance Card Read Fault";
                    st.giAddressNumber  = 191;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.AuxillarySupplyDisconnected:  // 421
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Auxiliary Supply Disconnected";
                    st.giAddressNumber  = 192;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.LEDCardMissingFFault:  // 422
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "LED Card Missing Fault";
                    st.giAddressNumber  = 193;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.NetworkPanelMissing:  // 423
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Network Panel Missing";
                    st.giAddressNumber  = 194;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.MaximumLoopsExceeded:  // 424
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Maximum Loops Exceeded";
                    st.giAddressNumber  = 195;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.LEDCardAddedFault:  // 425
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "LED Card Added Fault";
                    st.giAddressNumber  = 196;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.RemoteLinkNoReply:  // 426
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Remote Link No Reply";
                    st.giAddressNumber  = 197;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PanelStatusMismatch:  // 427
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Panel Status Mismatch";
                    st.giAddressNumber  = 198;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ConfigStatusMismatch:  // 428
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "Config Status Mismatch";
                    st.giAddressNumber  = 199;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.GeneralNonDeviceFaultatRemotePanel:  // 429
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "General Fault at Remote Panel";
                    st.giAddressNumber  = 200;
                    st.loop             = 0;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.NewAuxillarySUpply:  // 430
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "New Auxiliary Supply";
                    st.giAddressNumber  = 192;
                    on                  = false;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.AllClearMessage:  // 431
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "All Clear Message";
                    st.giAddressNumber  = 201;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.StartSystemTest:  // 432
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Start System Test";
                    st.giAddressNumber  = 202;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.StopSystemTest:  // 433
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Stop System Test";
                    st.giAddressNumber  = 202;
                    on                  = false;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SystemIOCardMissingFault:  // 434
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "System IO Card Missing Fault";
                    st.giAddressNumber  = 203;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SystemIOCardAddedFault:  // 435
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "System IO Card Added Fault";
                    st.giAddressNumber  = 204;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SystemIOCardVersionMismatch:  // 436
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "System IO Card Version Mismatch";
                    st.giAddressNumber  = 205;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SystemIOCardRestart:  // 437
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "System IO Card Restart";
                    st.giAddressNumber  = 206;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SystemIOCardCommsFaul:  // 438
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "System IO Card Comms Fault";
                    st.giAddressNumber  = 207;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.FATFBFCommsFailureFault:  // 439
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "FAT/FBF Comms Failure Fault";
                    st.giAddressNumber  = 208;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.FATFBFMissingFault:  // 440
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "FAT/FBF Missing Fault";
                    st.giAddressNumber  = 209;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.RemoteFireOutputTestEnd:  // 441
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Remote Fire Output Test End";
                    st.giAddressNumber  = 67;
                    on                  = false;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SSTDeviceTestStart:  // 442
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "SST Device Test Start";
                    st.giAddressNumber  = 210;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SSTDeviceTestEnd:  // 443
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "SST Device Test End";
                    st.giAddressNumber  = 210;
                    on                  = false;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ActivateRemoteFireOutput:  // 444
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Activate Remote Fire Outputs";
                    st.giAddressNumber  = 211;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ActivateSSTDevices:  // 445
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Activate SST Devices";
                    st.giAddressNumber  = 212;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SuppressInternalBuzzer:  // 446
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Suppress Internal Buzzer";
                    st.giAddressNumber  = 213;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.EnableInternalBuzzer:  // 447
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.gsTextField      = "Enable Internal Buzzer";
                    st.giAddressNumber  = 213;
                    on                  = false;
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.LossOfLoop:            // 148
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 20 + st.loop;
                    st.gsTextField      = "Loop Loss";
                    st.getDeviceText    = false;
                    return true;

                case enmNotEventType.LossPartLoop:          // 149
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 20 + st.loop;
                    st.gsTextField      = "Loop Part Loss";
                    st.getDeviceText    = false;
                    return true;

                case enmNotEventType.EndBFaultLoop:         // 150
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 20 + st.loop;
                    st.gsTextField      = "End B Fault Loop";
                    st.getDeviceText    = false;
                    return true;

                case enmNotEventType.NetworkDisabled:       // 156
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 72;
                    st.gsTextField      = "Network Disabled";
                    st.getDeviceText    = false;
                    return true;

                case enmNotEventType.ReSoundSounder:        // 157
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 29;
                    st.gsTextField      = "Re-Sound Sounders";
                    st.getDeviceText    = false;
                    st.bOneShotReset    = true;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.OverRideSounder:       // 163
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 65;
                    st.gsTextField      = "Over-Ride Sounder/Investigation delay";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.InvestigateDelayExtended:  // 167
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 66;
                    st.gsTextField      = "Investigation delay extended";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SounderImmediateMode:  // 170
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 70;
                    st.gsTextField      = "Sounder Immediate Mode";
                    st.getDeviceText    = false;
                    return true;

                case enmNotEventType.SounderDelayMode:      // 171
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 5;
                    st.gsTextField      = "Sounder Delay Mode";
                    st.getDeviceText    = false;
                    return true;

                case enmNotEventType.RemoteFireOutPutTest:  // 176
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 67;
                    st.gsTextField      = "Remote Fire Output Test";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SounderRelayCircuitDisabled:   // 181
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 73;
                    st.gsTextField      = "Sounder or Relay Circuit Disabled";
                    st.getDeviceText    = false;
                    return true;

                case enmNotEventType.FireRelayDisabled:     // 182
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 74;
                    st.gsTextField      = "Fire Relay Disabled";
                    st.getDeviceText    = false;
                    return true;

                case enmNotEventType.FaultRelayDisabled:    // 183
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 75;
                    st.gsTextField      = "Fault Relay Disabled";
                    st.getDeviceText    = false;
                    return true;

                case enmNotEventType.SounderRelayCircuitEnabled:    // 184
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 73;
                    st.gsTextField      = "Sounder or Relay Circuit Enabled";
                    st.getDeviceText    = false;
                    return true;

                case enmNotEventType.FireRelayEnabled:      // 185
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 74;
                    st.gsTextField      = "Fire Relay Enabled";
                    st.getDeviceText    = false;
                    return true;

                case enmNotEventType.FaultRelayEnabled:     // 186
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 75;
                    st.gsTextField      = "Fault Relay Enabled";
                    st.getDeviceText    = false;
                    return true;

                case enmNotEventType.NetworkZoneInEnabled:  // 192
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.loop             = 15;
                    st.giAddressNumber  = st.zone;
                    st.gsTextField      = "Network In Zone " + st.zone + " Enabled";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.NetworkZoneInDisabled: // 193
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.loop             = 15;
                    st.giAddressNumber  = st.zone;
                    st.gsTextField      = "Network In Zone " + st.zone + " Disabled";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.NetworkEnableZone:     // 195
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.loop             = 15;
                    st.giAddressNumber  = st.zone;
                    st.gsTextField      = "Network Zone " + st.zone + " Enabled";
                    st.getDeviceText    = false;
                    return true;

                case enmNotEventType.NetworkDisableZone:    // 196
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.loop             = 15;
                    st.giAddressNumber  = st.zone;
                    st.gsTextField      = "Network Zone " + st.zone + " Disabled";
                    st.getDeviceText    = false;
                    return true;

                case enmNotEventType.NetworkSoundersEnabled:    // 197
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 92;
                    st.gsTextField      = "Network Sounder Zone " + st.zone + " Enabled";
                    st.getDeviceText    = false;
                    return true;

                case enmNotEventType.NetworkSoundersDisabled:   // 198
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 92;
                    st.gsTextField      = "Network Sounder Zone " + st.zone + " Disabled";
                    st.getDeviceText    = false;
                    return true;

                case enmNotEventType.ZoneInFault:            // 200
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 3;
                    st.gsTextField      = "Zone In Fault";
                    st.getDeviceText    = false;
                    return true;

                case enmNotEventType.ID2NetworkDupNode:      // 203
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 33;
                    st.gsTextField      = "ID2 Net Duplicate Node";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PowerFaultID2Booster:   // 204
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 35;
                    st.gsTextField      = "Power Fault ID2 Booster";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.AccessLevel1:           // 205
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 76;
                    st.gsTextField      = "Access Level 1";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.AccessLevel2:           // 206
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 77;
                    st.gsTextField      = "Access Level 2";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.AccessLevel3:           // 207
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 78;
                    st.gsTextField      = "Access Level 3";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.AccessLevel4:           // 208
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 79;
                    st.gsTextField      = "Access Level 4";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ControlOutputsEnabled1:    // 209
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 101;
                    st.gsTextField      = "Control Outputs Enabled";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ControlOutputsDisabled1:   // 210
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 101;
                    st.gsTextField      = "Control Outputs Disabled";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.EntireZoneEnable:       // 228
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 97;
                    st.gsTextField      = "Entire Zone Enable";
                    st.getDeviceText    = false;
                    on = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.EntireZoneDisable:      // 229
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 97;
                    st.gsTextField      = "Entire Zone Disable";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.NetworkEntireZoneEnable:   // 230
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 93;
                    st.gsTextField      = "Network Entire Zone Enable";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.NetworkEntireZoneDisable:  // 231
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 93;
                    st.gsTextField      = "Network Entire Zone Disable";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.LIBCardLoopCPUFault:       // 257
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 41;
                    st.gsTextField      = "LIB Card Loop CPU Fault";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.LIBCardLoopCPUPwrRestart:  // 261
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 42;
                    st.gsTextField      = "LIB Card Loop CPU Power Restart";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.LIBCardLoopShortCircuit:   // 265
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 34;
                    st.gsTextField      = "LIB Card Loop Short Circuit";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.LIBCardDeviceZeroPresent:  // 269
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 44;
                    st.gsTextField      = "LIB Card Device Zero Present";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.LIBCardMissing:            // 273
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 45;
                    st.gsTextField      = "LIB Card Missing";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.LIBCardLoopEndDriverFault: // 277
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 46;
                    st.gsTextField      = "LIB Card Loop End Driver Fault";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.LIBCardLoopSignalDegraded: // 281
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 47;
                    st.gsTextField      = "LIB Card Loop Signal Degraded";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.LIBCardROMChkSumErr:       // 285
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 48;
                    st.gsTextField      = "LIB Card ROM Checksum Error";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.RS232LinkFault:             // 288
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 49;
                    st.gsTextField      = "RS232 Link Fault";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.MainsPSUFault:              // 289
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.giAddressNumber  = 11;
                    st.gsTextField      = "Mains PSU Fault";
                    st.getDeviceText    = false;
                    return true;

                case enmNotEventType.PSUChargerFault:            // 290
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 50;
                    st.gsTextField      = "PSU Charger Fault";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.BatteryLowVoltage:          // 291
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 51;
                    st.gsTextField      = "Battery Low Voltage";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.BatteryFailure:             // 292
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 52;
                    st.gsTextField      = "Battery Failure";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SoftwareFailure:            // 302
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 36;
                    st.gsTextField      = "Software Failure";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SounderCircuit1ShortFault:  // 306
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 13;
                    st.gsTextField      = "Sounder Circuit 1 Short";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SounderCircuit2ShortFault:  // 307
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 14;
                    st.gsTextField      = "Sounder Circuit 2 Short";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SounderCircuit1OpenFault:   // 308
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 82;
                    st.gsTextField      = "Sounder Circuit 1 Open";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SounderCircuit2OpenFault:   // 309
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 83;
                    st.gsTextField      = "Sounder Circuit 2 Open";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.EarthFault:                 // 312
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.giAddressNumber  = 17;
                    st.gsTextField      = "Earth Fault";
                    st.getDeviceText    = false;
                    return true;

                case enmNotEventType.SounderCircuit3ShortFault:  // 313
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 86;
                    st.gsTextField      = "Sounder Circuit 3 Short";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SounderCircuit4ShortFault:  // 314
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 87;
                    st.gsTextField      = "Sounder Circuit 4 Short";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SounderCircuit3OpenFault:   // 315
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 88;
                    st.gsTextField      = "Sounder Circuit 3 Open";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SounderCircuit4OpenFault:   // 316
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 89;
                    st.gsTextField      = "Sounder Circuit 4 Open";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.PanelKeyStuck:              // 319
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 40;
                    st.gsTextField      = "Panel Key Stuck";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.AuxOutput1Fault:            // 324
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 37;
                    st.gsTextField      = "Aux Output 1 Fault";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.AuxOutput2Fault:            // 325
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 38;
                    st.gsTextField      = "Aux Output 2 Fault";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ExternalPSUFault:           // 332
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 102;
                    st.gsTextField      = "External PSU Fault";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.NetworkZoneAssignIncorrect: // 347
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 53;
                    st.gsTextField      = "Network Zone Assign Incorrect";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.NetworkRefAssingIncorrect:  // 348
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 54;
                    st.gsTextField      = "Network Ref Assign Incorrect";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ID2NetworkZoneDup:          // 350
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 55;
                    st.gsTextField      = "ID2 Net Zone Duplication";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ID2NetworkStartUpFaultNetCardMissing:  // 355
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 56;
                    st.gsTextField      = "ID2 Net Startup Fault NetCard Missing";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ID2NetworkStartUpFaultNoACK:           // 356
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 57;
                    st.gsTextField      = "ID2 Net Startup Fault No Ack";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ID2NetworkStartUpFaultNoReply:         // 358
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 58;
                    st.gsTextField      = "ID2 Net Startup Fault No Reply";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ID2NetworkStartUpFaultJOINFail:        // 360
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 59;
                    st.gsTextField      = "ID2 Net Startup Fault Join Fail";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ID2NetworkRunTimeFault:                // 361
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 60;
                    st.gsTextField      = "ID2 Net Run Time Fault";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ID2ChannelLink1Fault:                  // 362
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 61;
                    st.gsTextField      = "ID2 Net Channel 1 Link Fault";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ID2ChannelLink2Fault:                  // 363
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 62;
                    st.gsTextField      = "ID2 Net Channel 2 Link Fault";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ID2FlashChecksumErr:                   // 364
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 63;
                    st.gsTextField      = "ID2 Net Flash checksum Error";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.ID2NetworkOverLoadTimeOut:             // 365
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 64;
                    st.gsTextField      = "ID2 Net OverLoad Timeout";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SignalledFaultatPanelInput1:           // 395
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    st.giAddressNumber  = 99;
                    st.gsTextField      = "Signalled Fault at Panel Input 1";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                case enmNotEventType.SignalledFaultatPanelInput2:           // 396
                    st.gAlarmType       = enmNotAlarmType.NOTStatusEvent.ToString();
                    // AMX status-event slot for Panel Input 2. The value 100
                    // coincides with kModuleAddressMin but is a different concept.
                    st.giAddressNumber  = 100;
                    st.gsTextField      = "Signalled Fault at Panel Input 2";
                    st.getDeviceText    = false;
                    Console.WriteLine(DateTime.Now + ": " + st.gsTextField);
                    return true;

                default:
                    return false;   // not handled — caller will try subclass hook
            }
        }

        // ----------------------------------------------------------------
        // Subclass hook — panel-specific events
        // Return true if handled, false to fall through to "Unknown Event"
        // ----------------------------------------------------------------
        protected virtual bool HandlePanelSpecificEvent(int eventcode, Id3kParseState st, ref bool on)
            => false;

        // ----------------------------------------------------------------
        // Post-switch dispatch hook — subclasses add panel-specific logic
        // (e.g. Notifier's _disabledZones tracking). Called only when
        // bDontSendToAMX is false, before send_response_amx_and_serial.
        // ----------------------------------------------------------------
        protected virtual void HandlePostSwitchDispatch(
            int evnum, Id3kParseState st, int p1, bool on,
            decimal zone, string zonetext, int eventcode,
            int p2, int p3, int p4)
        {
            if (p1 == (int)enmPRLAlarmType.Isolate)
            {
                send_response_amx_disable(evnum, st.gsTextField, gsDeviceText, zonetext, on);
            }
        }

        // ----------------------------------------------------------------
        // Shared send infrastructure
        // ----------------------------------------------------------------
        private void send_response_amx_and_serial(int evnum, string message1, string message2, string message3 = "")
        {
            string friendlymessage = message2 + (message3.Length > 0 ? " " + message3 : "");
            this.NotifyClient(friendlymessage, false);

            CSAMXSingleton.CS.SendAlarmToAMX(evnum, message1, message2, message3);
            CSAMXSingleton.CS.FlushMessages();

            string stracknowledge = ">IACK\r";
            foreach (char ch in stracknowledge) SendChar(ch);
            Console.WriteLine(DateTime.Now + ": >IACK Sent to Panel");
        }

        // ----------------------------------------------------------------
        // Device-type text lookup — identical across all ID3K panels.
        // Pearl adds case 26 (UnmonitoredRelayOutput) — PanelPearl overrides.
        // ----------------------------------------------------------------
        public virtual void GetDeviceTypeText(int piDeviceType)
        {
            gsDeviceText = "";
            try
            {
                switch (piDeviceType)
                {
                    case 0:  gDeviceType = EnmDeviceType.DeviceNotDefined;              gsDeviceText = "Device Not Defined"; break;
                    case 1:  gDeviceType = EnmDeviceType.HeatThermal;                  gsDeviceText = "Heat Thermal"; break;
                    case 2:  gDeviceType = EnmDeviceType.Ionisation;                   gsDeviceText = "Ionisation"; break;
                    case 3:  gDeviceType = EnmDeviceType.Optical;                      gsDeviceText = "Optical"; break;
                    case 4:  gDeviceType = EnmDeviceType.Reserved1;                    gsDeviceText = "4 Reserved"; break;
                    case 5:  gDeviceType = EnmDeviceType.CallPointManual;              gsDeviceText = "Call Point Manual"; break;
                    case 6:  gDeviceType = EnmDeviceType.GeneralControlOutput;         gsDeviceText = "General Control Output"; break;
                    case 7:  gDeviceType = EnmDeviceType.GeneralMonitoredInput;        gsDeviceText = "General Monitored Input"; break;
                    case 8:  gDeviceType = EnmDeviceType.SprinklerSystemMonitor;       gsDeviceText = "Sprinkler System Monitor"; break;
                    case 9:  gDeviceType = EnmDeviceType.VIEWSensor;                  gsDeviceText = "View Sensor"; break;
                    case 10: gDeviceType = EnmDeviceType.ConventionalZoneMonitorCDI;   gsDeviceText = "Conventional Zone Monitor CDI"; break;
                    case 11: gDeviceType = EnmDeviceType.SounderOutput;               gsDeviceText = "Sounder Output"; break;
                    case 12: gDeviceType = EnmDeviceType.AUXILIARYModule;             gsDeviceText = "Auxiliary Module"; break;
                    case 13: gDeviceType = EnmDeviceType.ConventionalZoneMonitorZMXM512; gsDeviceText = "Conventional Zone Monitor ZMX/M512"; break;
                    case 14: gDeviceType = EnmDeviceType.AdvancedMULTISensor;         gsDeviceText = "Advanced Multi Sensor"; break;
                    case 15: gDeviceType = EnmDeviceType.Reserved2;                   gsDeviceText = "15 Reserved"; break;
                    case 16: gDeviceType = EnmDeviceType.Reserved3;                   gsDeviceText = "16 Reserved"; break;
                    case 17: gDeviceType = EnmDeviceType.GASSensorInterface;          gsDeviceText = "Gas Sensor Interface"; break;
                    case 18: gDeviceType = EnmDeviceType.LoopBoosterModule;           gsDeviceText = "Loop Booster Module"; break;
                    case 19: gDeviceType = EnmDeviceType.SMART3Sensor;               gsDeviceText = "SMART 3 Sensor"; break;
                    case 20: gDeviceType = EnmDeviceType.SMART4Sensor;               gsDeviceText = "SMART 4 Sensor"; break;
                    default: gDeviceType = EnmDeviceType.Unknown;                     gsDeviceText = ""; break;
                }
            }
            catch (Exception) { }
        }

        // ----------------------------------------------------------------
        // Sectoring gate — identical on both panels
        // ----------------------------------------------------------------
        protected bool CheckForSectoring(int psEventCode, int psLoopNo)
        {
            if (psLoopNo == 0) return false;
            switch (psEventCode)
            {
                case 129: case 130: case 131: case 132: case 138:
                case 157: case 163: case 167: case 170:
                case 171: case 172: case 173: case 176:
                    return true;
                default:
                    return false;
            }
        }

        // ----------------------------------------------------------------
        // Checksum — used by inbound validation and by Notifier's outbound
        // ----------------------------------------------------------------
        public string CreateNOTChecksum(string myString)
        {
            int checksum = 0;
            for (int n = 0; n < myString.Length; n++)
            {
                int i = (int)myString[n];
                i = i ^ (checksum / 256);
                int j = i / 16;
                i = i ^ j;
                j = (i * 16) % 65536;
                j = j ^ checksum;
                int k = ((i / 8) ^ (j & 255)) % 256;
                j = i * 32;
                i = (i ^ j) % 256;
                checksum = (i & 255) + ((k * 256) % 65536);
            }
            return checksum.ToString("X").PadLeft(4, '0');
        }

        protected bool CheckSumValidation(string psCheckSumValues, byte[] paryMessage)
        {
            try
            {
                // VB NOTNetManager.bas CheckSumValidation: loop starts at element 2
                // of a 1-based array, i.e. the second character (index 1 in 0-based).
                // The frame is >IE..., so element 1 = '>' (skipped), element 2 = 'I'
                // (included in the checksum). C# was incorrectly starting at index 2,
                // skipping both '>' and 'I'.
                int i = gbHalfDuplex ? 2 : 1;
                if (gbHalfDuplex)
                {
                    string sMessage = "";
                    while (i < paryMessage.Length - 4)
                        sMessage += Encoding.ASCII.GetString(new byte[] { paryMessage[i++] });
                    return psCheckSumValues == CreateNOTChecksum(sMessage);
                }
                else
                {
                    int iMsgCheckSum = 0;
                    while (i < paryMessage.Length - 2)
                        iMsgCheckSum += paryMessage[i++];
                    iMsgCheckSum %= 256;
                    return iMsgCheckSum == Convert.ToInt32(psCheckSumValues, 16);
                }
            }
            catch { return false; }
        }

        // ----------------------------------------------------------------
        // Module address inbound offset — both panels add 100 on receive
        // ----------------------------------------------------------------
        protected virtual int GetModuleAddressOffset() => 100;

        // ----------------------------------------------------------------
        // Heartbeat — identical on both panels (>IQS)
        // ----------------------------------------------------------------
        protected override void heartbeat_timer_callback(object sender)
        {
            base.heartbeat_timer_callback(sender);
            string strheartbeat = ">IQS\r";
            foreach (char ch in strheartbeat) SendChar(ch);
        }

        // ----------------------------------------------------------------
        // StartUp — identical serial-port configuration for both panels
        // ----------------------------------------------------------------
        public override void StartUp(int fakemode)
        {
            int setttingbaudrate  = base.GetSetting<int>(ksettingsetupsection, "BaudRate");
            string settingparity  = base.GetSetting<string>(ksettingsetupsection, "Parity");
            int settingdatabits   = base.GetSetting<int>(ksettingsetupsection, "DataBits");
            int settingstopbits   = base.GetSetting<int>(ksettingsetupsection, "StopBits");

            if (fakemode > 0) return;

            serialport = new SerialPort(this.Identifier);
            serialport.BaudRate  = setttingbaudrate;

            Parity parity = Parity.None;
            string friendlyparity = settingparity.Substring(0, 1).ToUpper();
            if (friendlyparity == "E") parity = Parity.Even;
            if (friendlyparity == "O") parity = Parity.Odd;

            serialport.Parity     = parity;
            serialport.DataBits   = settingdatabits;
            serialport.StopBits   = (StopBits)settingstopbits;
            serialport.Handshake  = Handshake.None;
            serialport.RtsEnable  = false;
            serialport.DataReceived += SerialPort_Datareceived;

            if (serialport.IsOpen) serialport.Close();

            base.NotifyClient("Attempting Open " + serialport.PortName, false);
            serialport.Encoding               = Encoding.ASCII;
            serialport.DtrEnable              = true;
            serialport.ReadBufferSize         = 8000;
            serialport.WriteBufferSize        = 200;
            serialport.ReadTimeout            = 500;
            serialport.ParityReplace          = (byte)0;
            serialport.ReceivedBytesThreshold = 8;

            try { serialport.Open(); }
            catch { base.NotifyClient("Failed To Open " + serialport.PortName, false); }

            if (serialport.IsOpen)
            {
                serialport.DiscardInBuffer();
                serialport.DiscardOutBuffer();
            }
        }

        // ----------------------------------------------------------------
        // Action stubs — all route through the subclass send_message
        // ----------------------------------------------------------------
        public override void Evacuate(string passedvalues)         => send_message(ActionType.kEVACTUATE,        passedvalues);
        public override void Alert(string passedvalues)            => send_message(ActionType.kALERT,            passedvalues);
        public override void EvacuateNetwork(string passedvalues)  => send_message(ActionType.kEVACTUATENETWORK, passedvalues);
        public override void Silence(string passedvalues)          => send_message(ActionType.kSILENCE,          passedvalues);
        public override void MuteBuzzers(string passedvalues)      => send_message(ActionType.kMUTEBUZZERS,      passedvalues);
        public override void Reset(string passedvalues)            => send_message(ActionType.kRESET,            passedvalues);
        public override void DisableDevice(string passedvalues)    => send_message(ActionType.kDISABLEDEVICE,    passedvalues);
        public override void EnableDevice(string passedvalues)     => send_message(ActionType.kENABLEDEVICE,     passedvalues);
        public override void DisableZone(string passedvalues)      => send_message(ActionType.kDISABLEZONE,      passedvalues);
        public override void EnableZone(string passedvalues)       => send_message(ActionType.kENABLEZONE,       passedvalues);
        // Friendly name for the analogue store's Panel column — Identifier can
        // be a COM port (e.g. "COM3"), matching the Syncro/Taktis literals.
        protected virtual string AnaloguePanelName => "Notifier";

        // Analogue readback (099-048 section 3.3.4): subclasses implement
        // Analogue() because the request's checksum tail differs by wire format;
        // the response lands here from Parse. Readings store as one row per
        // value: the device's primary value (CLIP PW1 / S200 sub-address 0 /
        // gas) under the plain address, extras as "addr/PWn" or "addr.n".
        protected void HandleExtendedDeviceStatus(Id3kExtendedDeviceStatus status)
        {
            NotifyClient($"Extended status: panel {status.Panel} loop {status.Loop} "
                + $"{(status.IsSensor ? "sensor" : "module")} {status.Address} "
                + $"type {status.DeviceTypeCode} status {status.StatusWord} \"{status.Text}\"", false);

            if (status.GasAnalogueValue.HasValue)
            {
                addtoanalogue(AnaloguePanelName, status.Panel, status.Loop, status.Address.ToString(), status.GasAnalogueValue.Value);
            }
            else if (status.Analogue != null)
            {
                foreach (Id3kAnalogueReading r in status.Analogue.Readings)
                {
                    string addr = status.Analogue.Protocol == Id3kDeviceProtocol.Clip
                        ? (r.Index == 1 ? status.Address.ToString() : $"{status.Address}/PW{r.Index}")
                        : (r.Index == 0 ? status.Address.ToString() : $"{status.Address}.{r.Index}");
                    addtoanalogue(AnaloguePanelName, status.Panel, status.Loop, addr, r.Value);
                }
            }
        }

        // Subclasses provide the panel-specific wire-format send
        public abstract void send_message(ActionType action, string passedvalues);
    }
}
