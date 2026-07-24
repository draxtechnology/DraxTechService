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

                case enmNotEventType.GeneralNonDeviceFaultatRemotePanel:  // 429
                    // 099-048 3.5.2: category F, "placeholder for otherwise
                    // unspecified fault". Loop/zone fields not applicable —
                    // the panel fills the loop byte anyway, so zero it.
                    st.gAlarmType       = enmNotAlarmType.NOTFault.ToString();
                    st.gsTextField      = "General Fault at Remote Panel";
                    st.loop             = 0;
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
