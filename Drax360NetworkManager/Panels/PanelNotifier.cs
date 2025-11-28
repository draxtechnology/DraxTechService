using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Runtime.Remoting.Contexts;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Schema;

namespace Drax360Service.Panels
{
    public enum enmNotEventType
    {
        Fire = 1,
        FireDisabled = 2,
        TestFire = 3,
        NoReplyMissing = 5,
        TypeMisMatch = 6,
        SensorModuleFault = 8,
        PreAlarm = 10,
        RemovedDisabled = 13,
        FireCleared = 19,
        FaultCleared = 20,
        MissingCleared = 21,
        Deviceenabled = 23,
        Devicedisabled = 24,
        ModuleLoadShortCircuit = 33,
        OutputModuleTestActivation = 34,
        OutputModuleTestDeActivation = 35,
        DuplicateAddress = 36,
        AUXSet = 37,
        AuxCleared = 38,
        TechnicalAlarm = 44,
        PowerSupplyFault = 45,
        LoopBoosterFault = 48,
        ThermalAlarm = 50,
        SystemReset = 129,
        TerminateTest = 130,
        SilenceSounder = 131,
        MuteBuzzer = 132,
        StartZoneTest = 135,
        EnableZone = 136,
        DisableZone = 137,
        Evacuate = 138,
        SysClockAdjust = 139,
        EditChangesConfirmed = 140,
        SuspectedLoopBreak = 143,
        PowerRestart = 146,
        CommsFail = 147,
        LossOfLoop = 148,
        LossPartLoop = 149,
        EndBFaultLoop = 150,
        NetworkGenerlaReset = 153,
        NetworkSilenceSounders = 154,
        NetworkGeneralMuteSounder = 155,
        NetworkDisabled = 156,
        ReSoundSounder = 157,
        SounderDisabled = 159,
        RemoteFireOutputDisabled = 160,
        MuteInternalBuzzer = 161,
        ControlOutputsDisabled = 162,
        OverRideSounder = 163,
        RemoteFireOutputActivated = 165,
        ControlOutputsEnabled = 166,
        InvestigateDelayExtended = 167,
        RemoteFireOutputEnabled = 168,
        SounderEnabled = 169,
        SounderImmediateMode = 170,
        SounderDelayMode = 171,
        SystemDayMode = 172,
        SystemNightMode = 173,
        RemoteFireOutPutTest = 176,
        SounderRelayCircuitDisabled = 181,
        FireRelayDisabled = 182,
        FaultRelayDisabled = 183,
        SounderRelayCircuitEnabled = 184,
        FireRelayEnabled = 185,
        FaultRelyEnabled = 186,
        NetworkZoneInTestMode = 191,
        NetworkZoneInEnabled = 192,
        NetworkZoneInDisabled = 193,
        NetworkZoneInTest = 194,
        NetworkEnableZone = 195,
        NetworkDisableZone = 196,
        NetworkSoundersEnabled = 197,
        NetworkSoundersDisabled = 198,
        NetworkZoneInFault = 199,
        ZoneInFault = 200,
        ID2NetworkDupNode = 203,
        PowerFaultID2Booster = 204,
        AccessLevel1 = 205,
        AccessLevel2 = 206,
        AccessLevel3 = 207,
        AccessLevel4 = 208,
        ControlOutputsEnabled1 = 209,
        ControlOutputsDisabled1 = 210,
        EntireZoneEnable = 228,
        EnitreZoneDisable = 229,
        NetworkEntireZoneEnable = 230,
        NetworkEntireZoneDisable = 231,
        LIBCardLoopCPUFault = 257,
        LIBCardLoopCPUPwrRestart = 261,
        LIBCardLoopShortCircuit = 265,
        LIBCardDeviceZeroPresent = 269,
        LIBCardMissing = 273,
        LIBCardLoopEndDriverFault = 277,
        LIBCardLoopSignalDegraded = 281,
        LIBCardROMChkSumErr = 285,
        RS232LinkFault = 288,
        MainsPSUFault = 289,
        PSUChargerFault = 290,
        BatteryLowVoltage = 291,
        BatteryFailure = 292,
        SoftwareFailure = 302,
        SounderCircuit1ShortFault = 306,
        SounderCircuit2ShortFault = 307,
        SounderCircuit1OpenFault = 308,
        SounderCircuit2OpenFault = 309,
        SounderCircuit1RelayFault = 310,
        SounderCircuit2RelayFault = 311,
        EarthFault = 312,
        SounderCircuit3ShortFault = 313,
        SounderCircuit4ShortFault = 314,
        SounderCircuit3OpenFault = 315,
        SounderCircuit4OpenFault = 316,
        SounderCircuit3RelayFault = 317,
        SounderCircuit4RelayFault = 318,
        PanelKeyStuck = 319,
        AuxOutput1Fault = 324,
        AuxOutput2Fault = 325,
        NetworkZoneAssignIncorrect = 347,
        NetworkRefAssingIncorrect = 348,
        ID2NetworkZoneDup = 350,
        ID2NetworkStartUpFaultNetCardMissing = 355,
        ID2NetworkStartUpFaultNoACK = 356,
        ID2NetworkStartUpFaultNoReply = 358,
        ID2NetworkStartUpFaultJOINFail = 360,
        ID2NetworkRunTimeFault = 361,
        ID2ChannelLink1Fault = 362,
        ID2ChannelLink2Fault = 363,
        ID2FlashChecksumErr = 364,
        ID2NetworkOverLoadTimeOut = 365,
        SignalledFaultatPanelInput1 = 395,
        SignalledFaultatPanelInput2 = 396,
        ExternalPSUFault = 332
    }
    public enum enmNotAlarmType
    {
        NOTFire = 0,
        NOTNonFireAlarm = 1,
        NOTPreAlarm = 2,
        NOTIsolate = 4,
        NOTTestModeFire = 6,
        NOTFault = 8,
        NOTOutputActivate = 9,
        NOTDeviceTestMode = 10,
        NOTDisableZone = 11,
        NOTStatusEvent = 15,
    }
    public enum EnmDeviceType
    {
        DeviceNotDefined,
        HeatThermal,
        Ionisation,
        Optical,
        Reserved1,
        CallPointManual,
        GeneralControlOutput,
        GeneralMonitoredInput,
        SprinklerSystemMonitor,
        VIEWSensor,
        ConventionalZoneMonitorCDI,
        SounderOutput,
        AUXILIARYModule,
        ConventionalZoneMonitorZMXM512,
        AdvancedMULTISensor,
        Reserved2,
        Reserved3,
        GASSensorInterface,
        LoopBoosterModule,
        SMART3Sensor,
        SMART4Sensor,
        Unknown
    }

    internal class PanelNotifier : AbstractPanel
    {
        public string gsDeviceText = "";
        public EnmDeviceType gDeviceType;
        public bool gbHalfDuplex = false;
        public bool gbSectoring = false;
        public int gsSectorNo;
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

        public PanelNotifier(string baselogfolder, string identifier) : base(baselogfolder, identifier, "NOTMan", "NOT")
        {
            if (!String.IsNullOrEmpty(identifier))
            {
                heartbeat_timer = new Timer(heartbeat_timer_callback, this.Identifier, 1000, kHeartbeatDelaySeconds * 1000);
            }
        }

        public override void Parse(byte[] buffer)
        {
            //Thread.Sleep(1000); // Allow time for full message to arrive
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
            this.buffer.Clear();
            string strmsg = Encoding.UTF8.GetString(ourmessage, 0, foundat);
            if (!strmsg.StartsWith(">")) return;
            string cmd = strmsg.Substring(1, 2);

            Console.WriteLine(strmsg.Replace("\r", "") + " Received from Panel");

            //
            if (cmd == "IS")
            {
                string stracknowledge = ">IACK\r";

                foreach (char ch in stracknowledge)
                {
                    SendChar(ch);
                }

                //serialstringsend(stracknowledge);
                Console.WriteLine(stracknowledge.Replace("\r", "") + " Sent to Panel");
            }

            if (cmd == "IE")
            {
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
                try
                {
                    zone = Convert.ToDecimal(Encoding.UTF8.GetString(ourmessage, 18 - 1, 5));
                }
                catch { }
                string sensor = Encoding.UTF8.GetString(ourmessage, 23 - 1, 1);
                int address = 0;
                try
                {
                    string straddress = Encoding.UTF8.GetString(ourmessage, 24 - 1, 2);

                    address = Convert.ToInt32(straddress, 16);
                }
                catch { }
                giAddressNumber = address;
                bool on = true;

                string sDevicetype = "";
                try
                {
                    sDevicetype = Encoding.UTF8.GetString(ourmessage, 26 - 1, 2);
                }
                catch { };

                Console.WriteLine("Event " + eventcode + " Zone " + zone + " Address " + address);

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
                    GetDeviceTypeText(sDevicetype);
                }

                string sChecksum = "";
                bool bValidChecksum = false;
                if (gbHalfDuplex)
                {
                    sChecksum = Encoding.UTF8.GetString(new byte[] { ourmessage[ourmessage.Length - 4] });
                    sChecksum += Encoding.UTF8.GetString(new byte[] { ourmessage[ourmessage.Length - 3] });
                    sChecksum += Encoding.UTF8.GetString(new byte[] { ourmessage[ourmessage.Length - 2] });
                    sChecksum += Encoding.UTF8.GetString(new byte[] { ourmessage[ourmessage.Length - 1] });
                }
                else
                {
                    sChecksum = Encoding.UTF8.GetString(new byte[] { ourmessage[ourmessage.Length - 2] });
                    sChecksum += Encoding.UTF8.GetString(new byte[] { ourmessage[ourmessage.Length - 1] });
                }
                bValidChecksum = CheckSumValidation(sChecksum, ourmessage);

                switch ((enmNotEventType)eventcode)
                {
                    case enmNotEventType.Fire:
                        gsTextField = "Fire";
                        Console.WriteLine(gsTextField);
                        gAlarmType = enmNotAlarmType.NOTFire.ToString();
                        break;

                    case enmNotEventType.TestFire:
                        gsTextField = "Test Fire";
                        Console.WriteLine(gsTextField);
                        gAlarmType = enmNotAlarmType.NOTFire.ToString();
                        break;

                    case enmNotEventType.FireDisabled:
                        gsTextField = "Fire Disabled";
                        Console.WriteLine(gsTextField);
                        gAlarmType = enmNotAlarmType.NOTTestModeFire.ToString();
                        break;

                    case enmNotEventType.NoReplyMissing:
                        gAlarmType = enmNotAlarmType.NOTFault.ToString();
                        gsTextField = "Device Missing";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.TypeMisMatch:
                        gAlarmType = enmNotAlarmType.NOTFault.ToString();
                        gsTextField = "Type Mismatch";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.PreAlarm:
                        gAlarmType = enmNotAlarmType.NOTPreAlarm.ToString();
                        gsTextField = "Pre Alarm";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.RemovedDisabled:
                        gAlarmType = enmNotAlarmType.NOTFault.ToString();
                        gsTextField = "Removed Under Disablement";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.FireCleared:
                        gAlarmType = enmNotAlarmType.NOTFire.ToString();
                        gsTextField = "Fire Cleared";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.FaultCleared:
                        gAlarmType = enmNotAlarmType.NOTFault.ToString();
                        gsTextField = "Fault Cleared";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.MissingCleared:
                        gAlarmType = enmNotAlarmType.NOTFault.ToString();
                        gsTextField = "Missing Cleared";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.SensorModuleFault:
                        gAlarmType = enmNotAlarmType.NOTFault.ToString();
                        gsTextField = "Sensor Fault";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.Deviceenabled:
                        Console.WriteLine("Device " + address + " Enabled");
                        gAlarmType = enmNotAlarmType.NOTIsolate.ToString();
                        gsTextField = "Device " + address + " Enabled";
                        on = false;
                        break;

                    case enmNotEventType.Devicedisabled:
                        gAlarmType = enmNotAlarmType.NOTIsolate.ToString();
                        gsTextField = "Device " + address + " Disabled";
                        Console.WriteLine("Device " + address + " Disabled");
                        break;

                    case enmNotEventType.SystemReset:
                        gsTextField = "Panel Reset";
                        giAddressNumber = 9;
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.ModuleLoadShortCircuit:
                        gAlarmType = enmNotAlarmType.NOTFault.ToString();
                        gsTextField = "Module Load Short Circuit";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.OutputModuleTestDeActivation:
                        gAlarmType = enmNotAlarmType.NOTOutputActivate.ToString();
                        gsTextField = "Module Activation";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.OutputModuleTestActivation:
                        gAlarmType = enmNotAlarmType.NOTOutputActivate.ToString();
                        gsTextField = "Module DeActivation";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.DuplicateAddress:
                        gAlarmType = enmNotAlarmType.NOTFault.ToString();
                        gsTextField = "Duplicate Address";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.AUXSet:
                        gAlarmType = enmNotAlarmType.NOTNonFireAlarm.ToString();
                        gsTextField = "AUX Set";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.AuxCleared:
                        gAlarmType = enmNotAlarmType.NOTNonFireAlarm.ToString();
                        gsTextField = "Aux Cleared";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.TechnicalAlarm:
                        gAlarmType = enmNotAlarmType.NOTNonFireAlarm.ToString();
                        gsTextField = "Technical Alarm";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.PowerSupplyFault:
                        gAlarmType = enmNotAlarmType.NOTFault.ToString();
                        gsTextField = "Power Supply Fault";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.LoopBoosterFault:
                        gAlarmType = enmNotAlarmType.NOTFault.ToString();
                        gsTextField = "Loop Booster Fault";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.ThermalAlarm:
                        gAlarmType = enmNotAlarmType.NOTNonFireAlarm.ToString();
                        giAddressNumber = 0;
                        gsTextField = "Thermal Alarm";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.EnableZone:
                        gAlarmType = enmNotAlarmType.NOTDisableZone.ToString();
                        gsTextField = "Zone " + zone + " Enabled";
                        on = false;
                        Console.WriteLine("Zone " + zone + " Enabled");
                        break;

                    case enmNotEventType.DisableZone:
                        gAlarmType = enmNotAlarmType.NOTDisableZone.ToString();
                        gsTextField = "Zone " + zone + " Disabled";
                        Console.WriteLine("Zone " + zone + " Disabled");
                        break;

                    case enmNotEventType.LIBCardLoopCPUFault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 41;
                        gsTextField = "LIB Card Loop CPU Fault";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.LIBCardLoopCPUPwrRestart:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 42;
                        gsTextField = "LIB Card Loop CPU Power Restart";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.LIBCardLoopShortCircuit:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 34;
                        gsTextField = "LIB Card Loop Short Circuit";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.LIBCardDeviceZeroPresent:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 44;
                        gsTextField = "LIB Card Device Zero Present";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.LIBCardMissing:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 45;
                        gsTextField = "LIB Card Missing";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.LIBCardLoopEndDriverFault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 46;
                        gsTextField = "LIB Card Loop End Driver Fault";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.LIBCardLoopSignalDegraded:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 47;
                        gsTextField = "LIB Card Loop Signal Degraded";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.LIBCardROMChkSumErr:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 48;
                        gsTextField = "LIB Card ROM Checksum Error";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.RS232LinkFault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 49;
                        gsTextField = "RS232 Link Fault";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.PSUChargerFault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 50;
                        gsTextField = "PSU Charger Fault";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.BatteryLowVoltage:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 51;
                        gsTextField = "Battery Low Voltage";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.BatteryFailure:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 52;
                        gsTextField = "Battery Failure";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.SoftwareFailure:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 36;
                        gsTextField = "Software Failure";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.PanelKeyStuck:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 40;
                        gsTextField = "Panel Key Stuck";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.AuxOutput1Fault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 37;
                        gsTextField = "Aux Output 1 Fault";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.AuxOutput2Fault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 38;
                        gsTextField = "Aux Output 2 Fault";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.NetworkZoneAssignIncorrect:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 53;
                        gsTextField = "Network Zone Assign Incorrect";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.NetworkRefAssingIncorrect:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 54;
                        gsTextField = "Network Ref Assign Incorrect";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.ID2NetworkZoneDup:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 55;
                        gsTextField = "ID2 Net Zone Duplication";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.ID2NetworkStartUpFaultNetCardMissing:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 56;
                        gsTextField = "ID2 Net Startup Fault NetCard Missing";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.ID2NetworkStartUpFaultNoACK:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 57;
                        gsTextField = "ID2 Net Startup Fault No Ack";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.ID2NetworkStartUpFaultNoReply:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 58;
                        gsTextField = "ID2 Net Startup Fault No Reply";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.ID2NetworkStartUpFaultJOINFail:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 59;
                        gsTextField = "ID2 Net Startup Fault Join Fail";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.ID2NetworkRunTimeFault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 60;
                        gsTextField = "ID2 Net Run Time Fault";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.ID2ChannelLink1Fault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 61;
                        gsTextField = "ID2 Net Channel 1 Link Fault";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.ID2ChannelLink2Fault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 62;
                        gsTextField = "ID2 Net Channel 2 Link Fault";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.ID2FlashChecksumErr:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 63;
                        gsTextField = "ID2 Net Flash checksum Error";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.ID2NetworkOverLoadTimeOut:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 64;
                        gsTextField = "ID2 Net OverLoad Timeout";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.OverRideSounder:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 65;
                        gsTextField = "Over-Ride Sounder/Investigation delay";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.InvestigateDelayExtended:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 66;
                        gsTextField = "Investigation delay extended";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.RemoteFireOutPutTest:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 67;
                        gsTextField = "Remote Fire Output Test";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.SignalledFaultatPanelInput1:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 99;
                        gsTextField = "Signalled Fault at Panel Input 1";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.SignalledFaultatPanelInput2:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 100;
                        gsTextField = "Signalled Fault at Panel Input 2";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.ExternalPSUFault:
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 102;
                        gsTextField = "External PSU Fault";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.TerminateTest:  // 130
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 96;
                        gsTextField = "End Zone " + zone + " Test";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.SilenceSounder:  // 131
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 10;
                        gsTextField = "Alarms Silenced";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.MuteBuzzer:  // 132
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 18;
                        gsTextField = "Internal Buzzer Muted";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.StartZoneTest:  // 135
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 96;
                        gsTextField = "Start Zone " + zone + " Test";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.Evacuate:  // 138
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 1;
                        gsTextField = "Evacuate";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.SysClockAdjust:  // 139
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 30;
                        gsTextField = "System Clock Adjust";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.EditChangesConfirmed:  // 140
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 31;
                        gsTextField = "Edited Changes Confirmed";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.CommsFail:  // 147
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 32;
                        gsTextField = "Comms Fail";
                        Console.WriteLine(gsTextField);
                        break;


                    case enmNotEventType.ID2NetworkDupNode:  // 203
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 33;
                        gsTextField = "ID2 Net Duplicate Node";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.PowerFaultID2Booster:  // 204
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 35;
                        gsTextField = "Power Fault ID2 Booster";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.AccessLevel1:  // 205
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 76;
                        gsTextField = "Access Level 1";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.AccessLevel2:  // 206
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 77;
                        gsTextField = "Access Level 2";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.AccessLevel3:  // 207
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 78;
                        gsTextField = "Access Level 3";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.AccessLevel4:  // 208
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 79;
                        gsTextField = "Access Level 4";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.ControlOutputsEnabled1:  // 209
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 101;
                        gsTextField = "Control Outputs Enabled";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.ControlOutputsDisabled1:  // 210
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 101;
                        gsTextField = "Control Outputs Disabled";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.EntireZoneEnable:  // 228
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 97;
                        gsTextField = "Entire Zone Enable";
                        on = false;
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.EnitreZoneDisable:  // 229
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 97;
                        gsTextField = "Entire Zone Disable";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.NetworkEntireZoneEnable:  // 230
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 93;
                        gsTextField = "Network Entire Zone Enable";
                        Console.WriteLine(gsTextField);
                        break;

                    case enmNotEventType.NetworkEntireZoneDisable:  // 231
                        gAlarmType = enmNotAlarmType.NOTStatusEvent.ToString();
                        giAddressNumber = 93;
                        gsTextField = "Network Entire Zone Enable";
                        Console.WriteLine(gsTextField);
                        break;
                }

                if (gbSectoring == false)
                {
                    GetDeviceTypeText(sDevicetype);
                }
                int p1 = 0;
                int p2 = 0;
                int p3 = 0;
                int p4 = 0;
                int evnum = 0;

                try
                {
                    enmNotAlarmType enumValue = (enmNotAlarmType)Enum.Parse(typeof(enmNotAlarmType), gAlarmType);
                    p1 = (int)(enumValue);
                }
                catch (Exception ex)
                {
                    this.NotifyClient("gAlarmType " + gAlarmType + " " + ex.Message, false);
                    Console.WriteLine($"Unexpected error: {ex.Message}");
                }

                if (sensor.ToLower() == "m")   // Module at 100 to address
                {
                    giAddressNumber = giAddressNumber + 100;
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
                send_response_amx_and_serial(evnum, gsTextField, gsDeviceText, zonetext);
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

            Console.WriteLine(stracknowledge.Replace("\r", "") + " Sent to Panel");

        }

        public void GetDeviceTypeText(string psDeviceType)
        {
            // Author J.M Macpherson
            // Date 03/01/2006
            // Gets the Device type text

            gsDeviceText = "";

            try
            {
                switch (psDeviceType)
                {
                    case "00":
                        gDeviceType = EnmDeviceType.DeviceNotDefined;
                        gsDeviceText = "Device Not Defined";
                        break;
                    case "01":
                        gDeviceType = EnmDeviceType.HeatThermal;
                        gsDeviceText = "Heat Thermal";
                        break;
                    case "02":
                        gDeviceType = EnmDeviceType.Ionisation;
                        gsDeviceText = "Ionisation";
                        break;
                    case "03":
                        gDeviceType = EnmDeviceType.Optical;
                        gsDeviceText = "Optical";
                        break;
                    case "04":
                        gDeviceType = EnmDeviceType.Reserved1;
                        gsDeviceText = "4 Reserved";
                        break;
                    case "05":
                        gDeviceType = EnmDeviceType.CallPointManual;
                        gsDeviceText = "Call Point Manual";
                        break;
                    case "06":
                        gDeviceType = EnmDeviceType.GeneralControlOutput;
                        gsDeviceText = "General Control Output";
                        break;
                    case "07":
                        gDeviceType = EnmDeviceType.GeneralMonitoredInput;
                        gsDeviceText = "General Monitored Input";
                        break;
                    case "08":
                        gDeviceType = EnmDeviceType.SprinklerSystemMonitor;
                        gsDeviceText = "Sprinkler System Monitor";
                        break;
                    case "09":
                        gDeviceType = EnmDeviceType.VIEWSensor;
                        gsDeviceText = "View Sensor";
                        break;
                    case "10":
                        gDeviceType = EnmDeviceType.ConventionalZoneMonitorCDI;
                        gsDeviceText = "Conventional Zone Monitor CDI";
                        break;
                    case "11":
                        gDeviceType = EnmDeviceType.SounderOutput;
                        gsDeviceText = "Sounder Output";
                        break;
                    case "12":
                        gDeviceType = EnmDeviceType.AUXILIARYModule;
                        gsDeviceText = "Auxiliary Module";
                        break;
                    case "13":
                        gDeviceType = EnmDeviceType.ConventionalZoneMonitorZMXM512;
                        gsDeviceText = "Conventional Zone Monitor ZMX/M512";
                        break;
                    case "14":
                        gDeviceType = EnmDeviceType.AdvancedMULTISensor;
                        gsDeviceText = "Advanced Multi Sensor";
                        break;
                    case "15":
                        gDeviceType = EnmDeviceType.Reserved2;
                        gsDeviceText = "15 Reserved";
                        break;
                    case "16":
                        gDeviceType = EnmDeviceType.Reserved3;
                        gsDeviceText = "16 Reserved";
                        break;
                    case "17":
                        gDeviceType = EnmDeviceType.GASSensorInterface;
                        gsDeviceText = "Gas Sensor Interface";
                        break;
                    case "18":
                        gDeviceType = EnmDeviceType.LoopBoosterModule;
                        gsDeviceText = "Loop Booster Module";
                        break;
                    case "19":
                        gDeviceType = EnmDeviceType.SMART3Sensor;
                        gsDeviceText = "SMART 3 Sensor";
                        break;
                    case "20":
                        gDeviceType = EnmDeviceType.SMART4Sensor;
                        gsDeviceText = "SMART 4 Sensor";
                        break;
                    case "":
                    case null:
                        gDeviceType = EnmDeviceType.DeviceNotDefined;
                        gsDeviceText = "";
                        break;
                    default:
                        gDeviceType = EnmDeviceType.Unknown;
                        gsDeviceText = "";
                        break;
                }
            }
            catch (Exception ex)
            {

            }
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

        public virtual void send_message(ActionType action, string passedvalues)
        {
            string[] parts = passedvalues.Split(',');

            int node = 1, loop = 0, zone = 0, device = 0;

            if (parts.Length > 0) int.TryParse(parts[0], out node);
            if (parts.Length > 1) int.TryParse(parts[1], out loop);
            if (parts.Length > 2) int.TryParse(parts[2], out zone);
            if (parts.Length > 3) int.TryParse(parts[3], out device);

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
                 device.ToString("D2") +
                 "00";
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
                 device.ToString("D2") +
                 "00";
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
                 device.ToString("D2") +
                 "00";
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
                 device.ToString("D2") +
                 "00";
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

            string sChecksum = CreateNOTChecksum(message);
            message = message + "\r";

            foreach (char ch in message)
            {
                SendChar(ch);
            }

            //serialstringsend(">" + message + "\r");

            Console.WriteLine(">" + message + " Sent to panel");
        }

        public string CreateNOTChecksum(string myString)
        {
            int checksum = 0;

            for (int n = 0; n < myString.Length; n++)
            {
                int i = (int)myString[n]; // Equivalent to Asc(Mid$(MyString, n, 1)) in VB6
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
                i = 2;

                if (gbHalfDuplex == true)
                {
                    while (i < paryMessage.Length - 4)
                    {
                        sMessage += Encoding.ASCII.GetString(new byte[] { paryMessage[i] });
                        i++;
                    }

                    sChecksum = CreateNOTChecksum(sMessage);

                    return psCheckSumValues == sChecksum;
                }
                else
                {
                    while (i < paryMessage.Length - 2)
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