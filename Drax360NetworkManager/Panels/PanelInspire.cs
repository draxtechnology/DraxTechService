using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;

namespace DraxTechnology.Panels
{
    internal class PanelInspire : AbstractPanel
    {
public string gsDeviceText = "";
        public EnmDeviceType gDeviceType;
        public bool gbHalfDuplex = false;
        public bool gbSectoring = false;
        public int gsSectorNo;
        private readonly List<(int zone, int p2, int p3, int p4, int p1)> _disabledZones = new();
        private bool bOneShotReset;
        public override string FakeString
        {
            get =>

                /* Notifier
            >IS0001C000000000000BE7\r
            >IE0220611450330000000BDD\r
            >IE0220611450330000000BDD\r
            >IE0220611450330000000BDD\r
            >IE0220611450330000000BDD\r
            >IE0102411527000100001S01030000000000"OFFICE P1?DEV ROOM ZONE 1"1A7\r*/

                ">IE0220611450330000000BDD\r";
        }
        public override string PanelVersion => "1.0.0.0";
        public PanelInspire(string baselogfolder, string identifier) : base(baselogfolder, identifier, "INSMan", "INS")
        {
            if (!String.IsNullOrEmpty(identifier))
            {
                heartbeat_timer = new Timer(heartbeat_timer_callback, this.Identifier, 1000, kHeartbeatDelaySeconds * 1000);
            }
        }
        public override void Parse(byte[] buffer)
        {
            base.Parse(buffer);
            int foundat = -1;
            int bufferlength = this.buffer.Count;

            byte[] ourmessage = this.buffer.ToArray();
            for (int i = 0; i < ourmessage.Length; i++)
            {
                if (ourmessage[i] == '\r')
                {
                    foundat = i;
                    break;
                }
            }
            ;
            if (foundat <= 0) return;
            // Remove only the first complete message; leave any trailing bytes for
            // the next Parse() call so back-to-back panel notifications aren't dropped.
            this.buffer.RemoveRange(0, foundat + 1);
            ourmessage = ourmessage[..(foundat + 1)];
            string strmsg = Encoding.UTF8.GetString(ourmessage, 0, foundat);
            if (!strmsg.StartsWith(">")) return;
            string cmd = strmsg.Substring(1, 2);

            Console.WriteLine(DateTime.Now + ": " + strmsg.Replace("\r", "") + " Received from Panel");

            //
            if (cmd == "IS")
            {
                string stracknowledge = ">IACK\r";

                foreach (char ch in stracknowledge)
                {
                    SendChar(ch);
                }
                Console.WriteLine(DateTime.Now + ": " + stracknowledge.Replace("\r", "") + " Sent to Panel");
            }

            if (cmd == "IE")
            {
                bOneShotReset = false;
                string gsTextField = "";
                string gAlarmType = "";
                decimal giAddressNumber = 0;
                int panel = Convert.ToInt32(Encoding.UTF8.GetString(ourmessage, 4 - 1, 2));
                int eventcode = Convert.ToInt32(Encoding.UTF8.GetString(ourmessage, 6 - 1, 3));
                decimal dayofweek = Convert.ToDecimal(Encoding.UTF8.GetString(ourmessage, 9 - 1, 1));
                decimal hours = Convert.ToDecimal(Encoding.UTF8.GetString(ourmessage, 10 - 1, 2));
                decimal minutes = Convert.ToDecimal(Encoding.UTF8.GetString(ourmessage, 12 - 1, 2));
                decimal seconds = Convert.ToDecimal(Encoding.UTF8.GetString(ourmessage, 14 - 1, 2));
                int loop = Convert.ToInt32(Encoding.UTF8.GetString(ourmessage, 16 - 1, 2));
                decimal zone = 0;
                decimal.TryParse(Encoding.UTF8.GetString(ourmessage, 18 - 1, 5), out zone);

                string sensor = Encoding.UTF8.GetString(ourmessage, 23 - 1, 1);
                int address = 0;
                int.TryParse(
                    Encoding.UTF8.GetString(ourmessage, 24 - 1, 2),
                    out address);
                giAddressNumber = address;

                string sTextField = "";
                if (ourmessage != null && ourmessage.Length > 38)
                {
                    int start = (ourmessage[36] == 254) ? 39 : 38;

                    // VB6 GetTextData always terminates at the closing '"' — 0xFE within
                    // the text is a device/zone separator, not the end marker. Scan forward
                    // to find the closing '"' and stop there; don't read into the checksum.
                    int closeQuote = -1;
                    for (int j = start; j < ourmessage.Length; j++)
                    {
                        if (ourmessage[j] == (byte)'"') { closeQuote = j; break; }
                        if (ourmessage[j] == '\r') break;
                    }

                    if (closeQuote >= 0)
                    {
                        // 0xFE splits device text (before) from zone text (after).
                        // VB6 GetTextData returns the part before 0xFE (or full text if none).
                        int separator = Array.IndexOf(ourmessage, (byte)254, start);
                        int textEnd = (separator >= 0 && separator < closeQuote) ? separator : closeQuote;
                        sTextField = Encoding.UTF8.GetString(ourmessage, start, textEnd - start).Trim();
                    }
                    else
                    {
                        this.NotifyClient("WARN: no closing quote in text field. Raw[" + (start - 2) + "..]: " +
                            BitConverter.ToString(ourmessage, Math.Max(0, start - 2),
                                Math.Min(40, ourmessage.Length - Math.Max(0, start - 2))), false);
                    }
                }

                bool on = true;

                gsTextField = sTextField;

                // Device type sits at positions 25-26 only in full notifications (length >= 30).
                // A 28-byte message is an RS-485 echo of our own send_message command; positions
                // 25-26 there are the checksum, not a device type — parsing them gives 0
                // ("Device Not Defined") which is wrong.
                bool hasDeviceTypeField = ourmessage.Length >= 30;
                int iDevicetype = 0;
                if (hasDeviceTypeField)
                {
                    int.TryParse(Encoding.UTF8.GetString(ourmessage, 26 - 1, 2), out iDevicetype);
                }
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

                // Do not decode device type if sectoring as device not in status event
                gsDeviceText = "";
                if (!gbSectoring)
                {
                    GetDeviceTypeText(iDevicetype);
                }

                string sChecksum = "";
                bool bValidChecksum = false;
                if (gbHalfDuplex)
                {
                    sChecksum = Encoding.UTF8.GetString(new byte[] { ourmessage[ourmessage.Length - 5] });
                    sChecksum += Encoding.UTF8.GetString(new byte[] { ourmessage[ourmessage.Length - 4] });
                    sChecksum += Encoding.UTF8.GetString(new byte[] { ourmessage[ourmessage.Length - 3] });
                    sChecksum += Encoding.UTF8.GetString(new byte[] { ourmessage[ourmessage.Length - 2] });
                }
                else
                {
                    sChecksum = Encoding.UTF8.GetString(new byte[] { ourmessage[ourmessage.Length - 3] });
                    sChecksum += Encoding.UTF8.GetString(new byte[] { ourmessage[ourmessage.Length - 2] });
                }
                bValidChecksum = CheckSumValidation(sChecksum, ourmessage);
                if (!bValidChecksum)
                {
                    NotifyClient("Failed Checksum NOTNACK");
                    foreach (char ch in ">IN\r")
                        SendChar(ch);
                    return;
                }

                bool getDeviceText = true;
                bool bDontSendToAMX = false;
                switch ((enmNotEventType)eventcode)
                {
                    case enmNotEventType.Fire:
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        gAlarmType = enmNotAlarmType.NOTFire.ToString();
                        break;

                    case enmNotEventType.TestFire:
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        gAlarmType = enmNotAlarmType.NOTFire.ToString();
                        break;

                    case enmNotEventType.FireDisabled:
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        gAlarmType = enmNotAlarmType.NOTTestModeFire.ToString();
                        bDontSendToAMX = true;
                        break;

                    case enmNotEventType.NoReplyMissing:
                        gAlarmType = enmNotAlarmType.NOTFault.ToString();
                        gsTextField = "Device Missing";
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        getDeviceText = false;
                        break;

                    case enmNotEventType.TypeMisMatch:
                        gAlarmType = enmNotAlarmType.NOTFault.ToString();
                        gsTextField = "Type Mismatch";
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        getDeviceText = false;
                        break;

                    case enmNotEventType.PreAlarm:
                        gAlarmType = enmNotAlarmType.NOTPreAlarm.ToString();
                        gsTextField = "Pre Alarm";
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        getDeviceText = false;
                        break;

                    case enmNotEventType.RemovedDisabled:
                        gAlarmType = enmNotAlarmType.NOTFault.ToString();
                        gsTextField = "Removed Under Disablement";
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        getDeviceText = false;
                        break;

                    case enmNotEventType.FireCleared:
                        gAlarmType = enmNotAlarmType.NOTFire.ToString();
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        getDeviceText = false;
                        bDontSendToAMX = true;
                        break;

                    case enmNotEventType.FaultCleared:
                        gAlarmType = enmNotAlarmType.NOTFault.ToString();
                        gsTextField = "Fault Cleared";
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        getDeviceText = false;
                        break;

                    case enmNotEventType.MissingCleared:
                        gAlarmType = enmNotAlarmType.NOTFault.ToString();
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        getDeviceText = false;
                        bDontSendToAMX = true;
                        break;

                    case enmNotEventType.SensorModuleFault:
                        gAlarmType = enmNotAlarmType.NOTFault.ToString();
                        gsTextField = "Sensor Fault";
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        getDeviceText = false;
                        break;

                    case enmNotEventType.Deviceenabled:
                        Console.WriteLine(DateTime.Now + ": " + "Device " + address + " Enabled");
                        gAlarmType = enmNotAlarmType.NOTIsolate.ToString();
                        gsTextField = sTextField.Length > 0 ? sTextField : "Device " + address + " Enabled";
                        on = false;
                        break;

                    case enmNotEventType.Devicedisabled:
                        gAlarmType = enmNotAlarmType.NOTIsolate.ToString();
                        gsTextField = sTextField.Length > 0 ? sTextField : "Device " + address + " Disabled";
                        Console.WriteLine(DateTime.Now + ": " + "Device " + address + " Disabled");
                        break;

                    case enmNotEventType.SystemReset:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        gsTextField = "Panel Reset";
                        giAddressNumber = 9;
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.ModuleLoadShortCircuit:
                        gAlarmType = enmNotAlarmType.NOTFault.ToString();
                        gsTextField = "Module Load Short Circuit";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.OutputModuleTestDeActivation:
                        gAlarmType = enmNotAlarmType.NOTOutputActivate.ToString();
                        gsTextField = "Module Activation";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.OutputModuleTestActivation:
                        gAlarmType = enmNotAlarmType.NOTOutputActivate.ToString();
                        gsTextField = "Module DeActivation";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.DuplicateAddress:
                        gAlarmType = enmNotAlarmType.NOTFault.ToString();
                        gsTextField = "Duplicate Address";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.AUXSet:
                        gAlarmType = enmNotAlarmType.NOTNonFireAlarm.ToString();
                        gsTextField = "AUX Set";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.AuxCleared:
                        gAlarmType = enmNotAlarmType.NOTNonFireAlarm.ToString();
                        gsTextField = "Aux Cleared";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.TechnicalAlarm:
                        gAlarmType = enmNotAlarmType.NOTNonFireAlarm.ToString();
                        gsTextField = "Technical Alarm";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.NetworkGeneralReset:
                    case enmNotEventType.NetworkSilenceSounders:
                    case enmNotEventType.NetworkGeneralMuteSounder:
                    case enmNotEventType.NetworkZoneInTestMode:
                    case enmNotEventType.NetworkZoneInTest:
                    case enmNotEventType.NetworkZoneInFault:
                        bDontSendToAMX = true;
                        break;

                    case enmNotEventType.PowerSupplyFault:
                        gAlarmType = enmNotAlarmType.NOTFault.ToString();
                        gsTextField = "Power Supply Fault";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;


                    case enmNotEventType.PowerRestart:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        gsTextField = "Power Restart";
                        getDeviceText = false;
                        giAddressNumber = 98;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);

                        break;
                    case enmNotEventType.LoopBoosterFault:
                        gAlarmType = enmNotAlarmType.NOTFault.ToString();
                        gsTextField = "Loop Booster Fault";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.ThermalAlarm:
                        gAlarmType = enmNotAlarmType.NOTNonFireAlarm.ToString();
                        giAddressNumber = 0;
                        gsTextField = "Thermal Alarm";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.SounderCircuit1ShortFault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 13;
                        gsTextField = "Sounder Circuit 1 Short";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.SounderCircuit2ShortFault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 14;
                        gsTextField = "Sounder Circuit 2 Short";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.SounderCircuit1OpenFault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 82;
                        gsTextField = "Sounder Circuit 1 Open";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.SounderCircuit2OpenFault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 83;
                        gsTextField = "Sounder Circuit 2 Open";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.SounderCircuit1RelayFault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 84;
                        gsTextField = "Sounder Circuit 1 Relay";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.SounderCircuit2RelayFault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 85;
                        gsTextField = "Sounder Circuit 2 Relay";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.SounderCircuit3ShortFault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 86;
                        gsTextField = "Sounder Circuit 3 Short";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.SounderCircuit4ShortFault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 87;
                        gsTextField = "Sounder Circuit 4 Short";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.SounderCircuit3OpenFault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 88;
                        gsTextField = "Sounder Circuit 3 Open";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.SounderCircuit4OpenFault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 89;
                        gsTextField = "Sounder Circuit 4 Open";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.EnableZone:   // 136
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        gsTextField = "Entire Zone " + zone + " Enabled";
                        getDeviceText = false;
                        loop = 15;
                        on = false;
                        giAddressNumber = zone;
                        Console.WriteLine(DateTime.Now + ": " + "Entire Zone " + zone + " Enabled");
                        break;

                    case enmNotEventType.DisableZone:   // 137
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        gsTextField = "Entire Zone " + zone + " Disabled";
                        getDeviceText = false;
                        loop = 15;
                        giAddressNumber = zone;
                        Console.WriteLine(DateTime.Now + ": " + "Entire Zone " + zone + " Disabled");
                        break;

                    case enmNotEventType.LIBCardLoopCPUFault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 41;
                        gsTextField = "LIB Card Loop CPU Fault";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.LIBCardLoopCPUPwrRestart:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 42;
                        gsTextField = "LIB Card Loop CPU Power Restart";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.LIBCardLoopShortCircuit:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 34;
                        gsTextField = "LIB Card Loop Short Circuit";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.LIBCardDeviceZeroPresent:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 44;
                        gsTextField = "LIB Card Device Zero Present";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.LIBCardMissing:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 45;
                        gsTextField = "LIB Card Missing";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.LIBCardLoopEndDriverFault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 46;
                        gsTextField = "LIB Card Loop End Driver Fault";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.LIBCardLoopSignalDegraded:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 47;
                        gsTextField = "LIB Card Loop Signal Degraded";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.LIBCardROMChkSumErr:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 48;
                        gsTextField = "LIB Card ROM Checksum Error";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.RS232LinkFault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 49;
                        gsTextField = "RS232 Link Fault";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.PSUChargerFault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 50;
                        gsTextField = "PSU Charger Fault";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.BatteryLowVoltage:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 51;
                        gsTextField = "Battery Low Voltage";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.BatteryFailure:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 52;
                        gsTextField = "Battery Failure";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.SoftwareFailure:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 36;
                        gsTextField = "Software Failure";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.PanelKeyStuck:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 40;
                        gsTextField = "Panel Key Stuck";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.AuxOutput1Fault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 37;
                        gsTextField = "Aux Output 1 Fault";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.AuxOutput2Fault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 38;
                        gsTextField = "Aux Output 2 Fault";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.NetworkZoneAssignIncorrect:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 53;
                        gsTextField = "Network Zone Assign Incorrect";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.NetworkRefAssingIncorrect:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 54;
                        gsTextField = "Network Ref Assign Incorrect";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.ID2NetworkZoneDup:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 55;
                        gsTextField = "ID2 Net Zone Duplication";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.ID2NetworkStartUpFaultNetCardMissing:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 56;
                        gsTextField = "ID2 Net Startup Fault NetCard Missing";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.ID2NetworkStartUpFaultNoACK:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 57;
                        gsTextField = "ID2 Net Startup Fault No Ack";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.ID2NetworkStartUpFaultNoReply:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 58;
                        gsTextField = "ID2 Net Startup Fault No Reply";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.ID2NetworkStartUpFaultJOINFail:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 59;
                        gsTextField = "ID2 Net Startup Fault Join Fail";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.ID2NetworkRunTimeFault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 60;
                        gsTextField = "ID2 Net Run Time Fault";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.ID2ChannelLink1Fault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 61;
                        gsTextField = "ID2 Net Channel 1 Link Fault";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.ID2ChannelLink2Fault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 62;
                        gsTextField = "ID2 Net Channel 2 Link Fault";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.ID2FlashChecksumErr:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 63;
                        gsTextField = "ID2 Net Flash checksum Error";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.ID2NetworkOverLoadTimeOut:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 64;
                        gsTextField = "ID2 Net OverLoad Timeout";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.OverRideSounder:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 65;
                        gsTextField = "Over-Ride Sounder/Investigation delay";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.SounderDisabled:  // 159
                        gAlarmType = enmNotAlarmType.NOTIsolate.ToString();
                        gsTextField = "Sounder Disabled";
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.SounderEnabled:  // 169
                        gAlarmType = enmNotAlarmType.NOTIsolate.ToString();
                        gsTextField = "Sounder Enabled";
                        on = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.InvestigateDelayExtended:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 66;
                        gsTextField = "Investigation delay extended";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.RemoteFireOutPutTest:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 67;
                        gsTextField = "Remote Fire Output Test";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.SignalledFaultatPanelInput1:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 99;
                        gsTextField = "Signalled Fault at Panel Input 1";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.SignalledFaultatPanelInput2:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 100;
                        gsTextField = "Signalled Fault at Panel Input 2";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.ExternalPSUFault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 102;
                        gsTextField = "External PSU Fault";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.TerminateTest:  // 130
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 96;
                        gsTextField = "End Zone " + zone + " Test";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.SilenceSounder:  // 131
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 10;
                        gsTextField = "Alarms Silenced";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.MuteBuzzer:  // 132
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 18;
                        gsTextField = "Internal Buzzer Muted";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.StartZoneTest:  // 135
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 96;
                        gsTextField = "Start Zone " + zone + " Test";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.Evacuate:  // 138
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 1;
                        gsTextField = "Evacuate";
                        getDeviceText = false;
                        bOneShotReset = true;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.SysClockAdjust:  // 139
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 30;
                        gsTextField = "System Clock Adjust";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.EditChangesConfirmed:  // 140
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 31;
                        gsTextField = "Edited Changes Confirmed";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.CommsFail:  // 147
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 32;
                        gsTextField = "Comms Fail";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.ReSoundSounder:  // 157
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 29;
                        gsTextField = "Re-Sound Sounders";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.ID2NetworkDupNode:  // 203
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 33;
                        gsTextField = "ID2 Net Duplicate Node";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.PowerFaultID2Booster:  // 204
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 35;
                        gsTextField = "Power Fault ID2 Booster";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.AccessLevel1:  // 205
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 76;
                        gsTextField = "Access Level 1";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.AccessLevel2:  // 206
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 77;
                        gsTextField = "Access Level 2";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.AccessLevel3:  // 207
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 78;
                        gsTextField = "Access Level 3";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.AccessLevel4:  // 208
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 79;
                        gsTextField = "Access Level 4";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.ControlOutputsEnabled1:  // 209
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 101;
                        gsTextField = "Control Outputs Enabled";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.ControlOutputsDisabled1:  // 210
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 101;
                        gsTextField = "Control Outputs Disabled";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.EntireZoneEnable:  // 228
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 97;
                        gsTextField = "Entire Zone Enable";
                        getDeviceText = false;
                        on = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;
                    case enmNotEventType.SystemDayMode:  // 172
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 7;
                        gsTextField = "System Day Mode";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.SystemNightMode:  // 173
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 71;
                        gsTextField = "System Night Mode";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.NetworkZoneInEnabled:  // 192
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 71;
                        gsTextField = "Network In Zone " + zone + " Enabled";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.NetworkZoneInDisabled:  // 193
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 71;
                        gsTextField = "Network In Zone " + zone + " Disabled";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.EntireZoneDisable:  // 229
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 97;
                        gsTextField = "Entire Zone Disable";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.NetworkEntireZoneEnable:  // 230
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 93;
                        gsTextField = "Network Entire Zone Enable";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.NetworkEntireZoneDisable:  // 231
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 93;
                        gsTextField = "Network Entire Zone Disable";
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    case enmNotEventType.SuspectedLoopBreak:  // 143
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        gsTextField = "Suspected Loop Break " + loop;
                        giAddressNumber = 20 + loop;
                        loop = 0;
                        getDeviceText = false;
                        Console.WriteLine(DateTime.Now + ": " + gsTextField);
                        break;

                    default:
                        base.NotifyClient("Unknown Event " + ((enmNotEventType)eventcode));
                        break;
                }

                Console.WriteLine(DateTime.Now + ": " + "Event " + eventcode + " Zone " + zone + " Address " + giAddressNumber);


                gsDeviceText = "";
                if (getDeviceText && !gbSectoring && hasDeviceTypeField)
                {
                    GetDeviceTypeText(iDevicetype);
                }
                int p1 = 0;
                int p2 = 0;
                int p3 = 0;
                int p4 = 0;
                int evnum = 0;

                // Unknown / unhandled events leave gAlarmType empty; default to
                // NOTStatusEvent so the AMX side gets a benign status code rather
                // than a parse exception. Enum.TryParse avoids the throw entirely.
                if (string.IsNullOrEmpty(gAlarmType))
                {
                    gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                }
                if (Enum.TryParse(gAlarmType, out enmNotAlarmType enumValue))
                {
                    p1 = (int)enumValue;
                }
                else
                {
                    this.NotifyClient("gAlarmType " + gAlarmType + " not a valid enmNotAlarmType", false);
                    p1 = (int)enmNotAlarmType.NOTStatusEvent;
                }

                if (sensor.ToLower() == "m")
                {
                    loop += 10;
                }

                p2 = panel;
                p2 = p2 + this.Offset;

                p3 = loop;
                p4 = Convert.ToInt32(giAddressNumber);

                string zonetext = "";
                if (zone > 0)
                {
                    zonetext = "Zone " + zone;
                }
                evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1, on);
                if (p1 == (int)enmPRLAlarmType.Isolate)  // If Disable Device neeed to also send another event to AMX to increase the Isolation count
                {
                    if (!bDontSendToAMX)
                    {
                        send_response_amx_disable(evnum, gsTextField, zonetext, gsDeviceText, on);
                    }
                }

                if ((enmNotEventType)eventcode == enmNotEventType.DisableZone)
                    _disabledZones.Add(((int)zone, p2, p3, p4, p1));

                if ((enmNotEventType)eventcode == enmNotEventType.EnableZone)
                {
                    foreach (var entry in _disabledZones.Where(e => e.zone == (int)zone).ToList())
                    {
                        if (!bDontSendToAMX)
                        {
                            int offEvnum = CSAMXSingleton.CS.MakeInputNumber(entry.p2, entry.p3, entry.p4, entry.p1, false);
                            CSAMXSingleton.CS.SendResetToAMX(offEvnum, gsTextField, "", "");
                        }
                    }
                    _disabledZones.RemoveAll(e => e.zone == (int)zone);
                }

                if (!bDontSendToAMX)
                {
                    this.NotifyClient("Sending gsTextField: " + gsTextField + " gsDeviceText: " + gsDeviceText + " zonetext: " + zonetext, false);
                    send_response_amx_and_serial(evnum, gsTextField, gsDeviceText, zonetext);
                }

                if (bOneShotReset && evnum != 0)
                {
                    if (!bDontSendToAMX)
                    {
                        base.NotifyClient("OneShot - Force EVM Attribute 13");
                        CSAMXSingleton.CS.ForceEvmAttribute(evnum, 13, 1);
                        CSAMXSingleton.CS.FlushMessages();
                    }
                }
                // Removed Thread.Sleep(1000) here (2026-05-23): the AMX writer
                // queue + MAK ack already paces outbound traffic, so blocking
                // the receive thread for a full second after a dispatch was
                // just delaying the next inbound parse cycle for no benefit.
            }
        }
        private void send_response_amx_and_serial(int evnum, string message1, string message2, string message3 = "")
        {
            string friendlymessage = message2 + (message3.Length > 0 ? (" " + message3) : "");

            // Signal the event back to the main service, so that it can be logged
            this.NotifyClient(friendlymessage, false);

            CSAMXSingleton.CS.SendAlarmToAMX(evnum, message1, message2, message3);
            CSAMXSingleton.CS.FlushMessages();

            string stracknowledge = ">IACK\r";

            foreach (char ch in stracknowledge)
            {
                SendChar(ch);
            }

            Console.WriteLine(DateTime.Now + ": " + stracknowledge.Replace("\r", "") + " Sent to Panel");

        }
        public void GetDeviceTypeText(int piDeviceType)
        {
            // Author J.M Macpherson
            // Date 03/01/2006
            // Gets the Device type text

            gsDeviceText = "";

            try
            {
                switch (piDeviceType)
                {
                    case 0:
                        gDeviceType = EnmDeviceType.DeviceNotDefined;
                        gsDeviceText = "Device Not Defined";
                        break;
                    case 1:
                        gDeviceType = EnmDeviceType.HeatThermal;
                        gsDeviceText = "Heat Thermal";
                        break;
                    case 2:
                        gDeviceType = EnmDeviceType.Ionisation;
                        gsDeviceText = "Ionisation";
                        break;
                    case 3:
                        gDeviceType = EnmDeviceType.Optical;
                        gsDeviceText = "Optical";
                        break;
                    case 4:
                        gDeviceType = EnmDeviceType.Reserved1;
                        gsDeviceText = "4 Reserved";
                        break;
                    case 5:
                        gDeviceType = EnmDeviceType.CallPointManual;
                        gsDeviceText = "Call Point Manual";
                        break;
                    case 6:
                        gDeviceType = EnmDeviceType.GeneralControlOutput;
                        gsDeviceText = "General Control Output";
                        break;
                    case 7:
                        gDeviceType = EnmDeviceType.GeneralMonitoredInput;
                        gsDeviceText = "General Monitored Input";
                        break;
                    case 8:
                        gDeviceType = EnmDeviceType.SprinklerSystemMonitor;
                        gsDeviceText = "Sprinkler System Monitor";
                        break;
                    case 9:
                        gDeviceType = EnmDeviceType.VIEWSensor;
                        gsDeviceText = "View Sensor";
                        break;
                    case 10:
                        gDeviceType = EnmDeviceType.ConventionalZoneMonitorCDI;
                        gsDeviceText = "Conventional Zone Monitor CDI";
                        break;
                    case 11:
                        gDeviceType = EnmDeviceType.SounderOutput;
                        gsDeviceText = "Sounder Output";
                        break;
                    case 12:
                        gDeviceType = EnmDeviceType.AUXILIARYModule;
                        gsDeviceText = "Auxiliary Module";
                        break;
                    case 13:
                        gDeviceType = EnmDeviceType.ConventionalZoneMonitorZMXM512;
                        gsDeviceText = "Conventional Zone Monitor ZMX/M512";
                        break;
                    case 14:
                        gDeviceType = EnmDeviceType.AdvancedMULTISensor;
                        gsDeviceText = "Advanced Multi Sensor";
                        break;
                    case 15:
                        gDeviceType = EnmDeviceType.Reserved2;
                        gsDeviceText = "15 Reserved";
                        break;
                    case 16:
                        gDeviceType = EnmDeviceType.Reserved3;
                        gsDeviceText = "16 Reserved";
                        break;
                    case 17:
                        gDeviceType = EnmDeviceType.GASSensorInterface;
                        gsDeviceText = "Gas Sensor Interface";
                        break;
                    case 18:
                        gDeviceType = EnmDeviceType.LoopBoosterModule;
                        gsDeviceText = "Loop Booster Module";
                        break;
                    case 19:
                        gDeviceType = EnmDeviceType.SMART3Sensor;
                        gsDeviceText = "SMART 3 Sensor";
                        break;
                    case 20:
                        gDeviceType = EnmDeviceType.SMART4Sensor;
                        gsDeviceText = "SMART 4 Sensor";
                        break;
                    case 26:
                        gDeviceType = EnmDeviceType.UnmonitoredRelayOutput;
                        gsDeviceText = "Unmonitored Relay Output";
                        break;
                    case 50:
                    case 51:
                    case 52:
                    case 53:
                    case 54:
                    case 55:
                    case 56:
                    case 57:
                    case 58:
                    case 59:
                    case 60:
                    case 61:
                    case 62:
                    case 63:
                    case 64:
                    case 65:
                    case 66:
                    case 67:
                    case 68:
                    case 69:
                    case 70:
                    case 71:
                    case 72:
                    case 73:
                    case 74:
                    case 75:
                    case 76:
                    case 77:
                    case 78:
                    case 79:
                    case 80:
                    case 81:
                        break;
                    default:
                        gDeviceType = EnmDeviceType.Unknown;
                        gsDeviceText = "";
                        break;
                }
            }
            catch (Exception ex)
            { }
        }

        private bool CheckForSectoring(int psEventCode, int psLoopNo)
        {
            if (psEventCode == 129 || psEventCode == 131 || psEventCode == 132 || psEventCode == 138 ||
                psEventCode == 157 || psEventCode == 163 || psEventCode == 167 || psEventCode == 170 ||
                psEventCode == 171 || psEventCode == 172 || psEventCode == 173 || psEventCode == 176 ||
                psEventCode == 130)
            {
                if (psLoopNo != 0)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            return false;
        }

        protected override void heartbeat_timer_callback(object sender)
        {
            base.heartbeat_timer_callback(sender);

            string strheartbeat = ">IQS\r";

            foreach (char ch in strheartbeat)
            {
                SendChar(ch);
            }
        }

        public override void StartUp(int fakemode)
        {
            int setttingbaudrate = base.GetSetting<int>(ksettingsetupsection, "BaudRate");
            string settingparity = base.GetSetting<string>(ksettingsetupsection, "Parity");
            int settingdatabits = base.GetSetting<int>(ksettingsetupsection, "DataBits");
            int settingstopbits = base.GetSetting<int>(ksettingsetupsection, "StopBits");

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
            serialport.RtsEnable = false;
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
                base.NotifyClient("Failed To Open " + serialport.PortName, false);


            }

            if (serialport.IsOpen)
            {
                serialport.DiscardInBuffer();
                serialport.DiscardOutBuffer();
            }
        }

        public override void Evacuate(string passedvalues)
        {
            send_message(ActionType.kEVACTUATE, passedvalues);
        }
        public override void Alert(string passedvalues)
        {
            send_message(ActionType.kALERT, passedvalues);
        }
        public override void EvacuateNetwork(string passedvalues)
        {
            send_message(ActionType.kEVACTUATENETWORK, passedvalues);
        }
        public override void Silence(string passedvalues)
        {
            send_message(ActionType.kSILENCE, passedvalues);
        }
        public override void MuteBuzzers(string passedvalues)
        {
            send_message(ActionType.kMUTEBUZZERS, passedvalues);
        }
        public override void Reset(string passedvalues)
        {
            send_message(ActionType.kRESET, passedvalues);
        }
        public override void DisableDevice(string passedvalues)
        {
            send_message(ActionType.kDISABLEDEVICE, passedvalues);
        }
        public override void EnableDevice(string passedvalues)
        {
            send_message(ActionType.kENABLEDEVICE, passedvalues);
        }
        public override void DisableZone(string passedvalues)
        {
            send_message(ActionType.kDISABLEZONE, passedvalues);
        }
        public override void EnableZone(string passedvalues)
        {
            send_message(ActionType.kENABLEZONE, passedvalues);
        }
        public override void Analogue(string passedvalues)
        {
            throw new NotImplementedException();
        }
        public virtual void send_message(ActionType action, string passedvalues)
        {
            ParsePassedValues(passedvalues, out int node, out int loop, out int zone, out int device);

            // VB6 Pearl: loop >= 11 means module (panel loop + 10 was added on inbound).
            // Subtract 10 to recover the wire loop; address is sent as-is.
            if (loop >= 11)
            {
                action = action switch
                {
                    ActionType.kDISABLEDEVICE => ActionType.kDISABLEMODULE,
                    ActionType.kENABLEDEVICE => ActionType.kENABLEMODULE,
                    _ => action,
                };
                loop -= 10;
            }

            string loopStr = loop.ToString("D2"); // Pads with leading zeros to 2 digits
            string zoneStr = zone.ToString("D5"); // Pads with leading zeros to 5 digits

            DateTime now = DateTime.Now;

            int iDayOfWeek = (int)now.DayOfWeek; // Sunday = 0, Monday = 1, etc.
            iDayOfWeek++;

            int sHour = now.Hour;
            int sMinute = now.Minute;
            int sSecond = now.Second;

            int sYear = int.Parse(now.ToString("yy"));   // Two-digit year
            int sMonth = int.Parse(now.ToString("MM"));  // Two-digit month
            int sDate = int.Parse(now.ToString("dd"));   // Two-digit day

            string message = "";

            if (action == ActionType.kEVACTUATE)
            {
                message = ">IE00" +
                 "138" +
                 iDayOfWeek +
                 sHour.ToString("D2") +
                 sMinute.ToString("D2") +
                 sSecond.ToString("D2") +
                 loop.ToString("D2") +
                 zone.ToString("D5");
            }
            if (action == ActionType.kRESET)
            {
                message = ">IE" +
                 node.ToString("D2") +
                 "129" +
                 iDayOfWeek +
                 sHour.ToString("D2") +
                 sMinute.ToString("D2") +
                 sSecond.ToString("D2") +
                 loop.ToString("D2") +
                 zone.ToString("D5");
            }

            if (action == ActionType.kRESETNETWORK)
            {
                message = ">IE00129" +
                 iDayOfWeek +
                 sHour.ToString("D2") +
                 sMinute.ToString("D2") +
                 sSecond.ToString("D2") +
                 loop.ToString("D2") +
                 zone.ToString("D5");
            }

            if (action == ActionType.kSILENCE)
            {
                message = ">IE" +
                 node.ToString("D2") +
                 "131" +
                 iDayOfWeek +
                 sHour.ToString("D2") +
                 sMinute.ToString("D2") +
                 sSecond.ToString("D2") +
                 loop.ToString("D2") +
                 zone.ToString("D5");
            }

            if (action == ActionType.kDISABLEDEVICE)
            {
                message = ">IE" +
                 node.ToString("D2") +
                 "024" +
                 iDayOfWeek +
                 sHour.ToString("D2") +
                 sMinute.ToString("D2") +
                 sSecond.ToString("D2") +
                 loop.ToString("D2") +
                 zone.ToString("D5") +
                 "S" +
                 device.ToString("D2");
            }

            if (action == ActionType.kENABLEDEVICE)
            {
                message = ">IE" +
                 node.ToString("D2") +
                 "023" +
                 iDayOfWeek +
                 sHour.ToString("D2") +
                 sMinute.ToString("D2") +
                 sSecond.ToString("D2") +
                 loop.ToString("D2") +
                 zone.ToString("D5") +
                 "S" +
                 device.ToString("D2");
            }

            if (action == ActionType.kDISABLEMODULE)
            {
                message = ">IE" +
                 node.ToString("D2") +
                 "024" +
                 iDayOfWeek +
                 sHour.ToString("D2") +
                 sMinute.ToString("D2") +
                 sSecond.ToString("D2") +
                 loop.ToString("D2") +
                 zone.ToString("D5") +
                 "M" +
                 device.ToString("D2");
            }

            if (action == ActionType.kENABLEMODULE)
            {
                message = ">IE" +
                 node.ToString("D2") +
                 "023" +
                 iDayOfWeek +
                 sHour.ToString("D2") +
                 sMinute.ToString("D2") +
                 sSecond.ToString("D2") +
                 loop.ToString("D2") +
                 zone.ToString("D5") +
                 "M" +
                 device.ToString("D2");
            }

            if (action == ActionType.kDISABLEZONE)
            {
                message = ">IE00" +
                 "137" +
                 iDayOfWeek +
                 sHour.ToString("D2") +
                 sMinute.ToString("D2") +
                 sSecond.ToString("D2") +
                 "00" +
                 zone.ToString("D5");
            }

            if (action == ActionType.kENABLEZONE)
            {
                message = ">IE00" +
                 "136" +
                 iDayOfWeek +
                 sHour.ToString("D2") +
                 sMinute.ToString("D2") +
                 sSecond.ToString("D2") +
                 "00" +
                 zone.ToString("D5");
            }

            string sChecksum = gbHalfDuplex
                ? CreateNOTChecksum(message.Substring(1))
                : CreateSimpleChecksum(message.Substring(1));
            message = message + sChecksum + "\r";

            serialsend(message);

            Console.WriteLine(DateTime.Now + ": " + message.Replace("\r", "") + " Sent to panel");
        }

        private string CreateSimpleChecksum(string body)
        {
            int sum = 0;
            foreach (char c in body)
                sum += (byte)c;
            return (sum % 256).ToString("X2");
        }

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

            // Convert to hex and pad with leading zeros to 4 characters
            string hexChecksum = checksum.ToString("X");
            return hexChecksum.PadLeft(4, '0');
        }

        private bool CheckSumValidation(string psCheckSumValues, byte[] paryMessage)
        {
            // Checks if checksum is valid
            // Return: True or False
            int i;
            int iDecCheckSum;
            string iHexCheckSum;
            int iMsgCheckSum = 0;
            string sMessage = "";
            string sChecksum;

            try
            {
                i = 1;

                if (gbHalfDuplex == true)
                {
                    while (i < paryMessage.Length - 5)
                    {
                        sMessage += Encoding.ASCII.GetString(new byte[] { paryMessage[i] });
                        i++;
                    }

                    sChecksum = CreateNOTChecksum(sMessage);

                    return psCheckSumValues == sChecksum;
                }
                else
                {
                    while (i < paryMessage.Length - 3)
                    {
                        // Add byte value directly
                        iMsgCheckSum = paryMessage[i] + iMsgCheckSum;
                        i++;
                    }

                    iMsgCheckSum = iMsgCheckSum % 256;
                    iHexCheckSum = psCheckSumValues;
                    iDecCheckSum = Convert.ToInt32(iHexCheckSum, 16);

                    return iMsgCheckSum == iDecCheckSum;
                }
            }
            catch (Exception ex)
            {
                return false;
            }
        }
    }
}
