using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Drax360Service.Panels
{
    public enum enmEventType
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
    public enum enmAlarmType
    {
        MAXFire = 0,
        MAXNonFireAlarm = 1,
        MAXPreAlarm = 2,
        MAXIsolate = 4,
        MAXTestModeFire = 6,
        MAXFault = 8,
        MAXOutputActivate = 9,
        MAXDeviceTestMode = 10,
        MAXDisableZone = 11,
        MAXStatusEvent = 15,
        MAXUnknown = 15
    }

    internal class PanelMorelyMax : AbstractPanel
    {

        public override string GetFileName { get => "MaxMan"; }

        public override string FakeString
        {
            get =>

                /* MorleyMax
            >IS0001C000000000000BE7\r
            >IE0220611450330000000BDD\r
            >IE0220611450330000000BDD\r
            >IE0220611450330000000BDD\r
            >IE0220611450330000000BDD\r
            >IE0102411527000100001S01030000000000"OFFICE P1?DEV ROOM ZONE 1"1A7\r*/

                ">IE0220611450330000000BDD\r";

        }

        public PanelMorelyMax(string identifier) : base(identifier)
        {
            if (!String.IsNullOrEmpty(identifier))
            {
                heartbeat_timer = new Timer(heartbeat_timer_callback, this.Identifier, 1000, kheartbeatdelayseconds * 1000);
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

            };
            if (foundat <= 0) return;
            this.buffer.Clear();
            string strmsg = Encoding.UTF8.GetString(ourmessage, 0, foundat);
            if (!strmsg.StartsWith(">")) return;
            string cmd = strmsg.Substring(1, 2);

            Console.WriteLine(strmsg.Replace("\r", "") + " Received from Panel");

            //
            if (cmd == "IS")
            {
            }

            if (cmd == "IE")
            {
                string gsTextField = "";
                string gAlarmType = "";
                decimal giAddressNumber = 0;
                string panel = Encoding.UTF8.GetString(ourmessage, 4 - 1, 2);
                decimal eventcode = Convert.ToDecimal(Encoding.UTF8.GetString(ourmessage, 6 - 1, 3));
                decimal dayofweek = Convert.ToDecimal(Encoding.UTF8.GetString(ourmessage, 9 - 1, 1));
                decimal hours = Convert.ToDecimal(Encoding.UTF8.GetString(ourmessage, 10 - 1, 2));
                decimal minutes = Convert.ToDecimal(Encoding.UTF8.GetString(ourmessage, 12 - 1, 2));
                decimal seconds = Convert.ToDecimal(Encoding.UTF8.GetString(ourmessage, 14 - 1, 2));
                decimal loop = Convert.ToDecimal(Encoding.UTF8.GetString(ourmessage, 16 - 1, 2));
                decimal zone = Convert.ToDecimal(Encoding.UTF8.GetString(ourmessage, 18 - 1, 5));
                string sensor = Encoding.UTF8.GetString(ourmessage, 23 - 1, 1);

                string straddress = Encoding.UTF8.GetString(ourmessage, 24 - 1, 2);

                int address = Convert.ToInt32(straddress, 16);
                Console.WriteLine("Event " + eventcode + " Zone " + zone + " Address " + address);
                if ((enmEventType)eventcode == enmEventType.Fire)
                {
                    gsTextField = "Fire";
                    Console.WriteLine(gsTextField);
                    gAlarmType = enmAlarmType.MAXFire.ToString();
                }
                else if ((enmEventType)eventcode == enmEventType.TestFire)
                {
                    gsTextField = "Test Fire";
                    Console.WriteLine(gsTextField);
                    gAlarmType = enmAlarmType.MAXFire.ToString();
                }
                else if ((enmEventType)eventcode == enmEventType.FireDisabled)
                {
                    gsTextField = "Fire Disabled";
                    Console.WriteLine(gsTextField);
                    gAlarmType = enmAlarmType.MAXTestModeFire.ToString();
                }
                else if ((enmEventType)eventcode == enmEventType.NoReplyMissing)
                {
                    gAlarmType = enmAlarmType.MAXFault.ToString();
                    gsTextField = "Device Missing";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.TypeMisMatch)
                {
                    gAlarmType = enmAlarmType.MAXFault.ToString();
                    gsTextField = "Type Mismatch";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.PreAlarm)
                {
                    gAlarmType = enmAlarmType.MAXPreAlarm.ToString();
                    gsTextField = "Pre Alarm";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.FireCleared)
                {
                    gAlarmType = enmAlarmType.MAXFire.ToString();
                    gsTextField = "Fire Cleared";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.FaultCleared)
                {
                    gAlarmType = enmAlarmType.MAXFault.ToString();
                    gsTextField = "Fault Cleared";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.MissingCleared)
                {
                    gAlarmType = enmAlarmType.MAXFault.ToString();
                    gsTextField = "Missing Cleared";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.SensorModuleFault)
                {
                    gAlarmType = enmAlarmType.MAXFault.ToString();
                    gsTextField = "Sensor Fault";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.Deviceenabled)
                {
                    Console.WriteLine("Device " + address + " Enabled");
                }
                else if ((enmEventType)eventcode == enmEventType.Devicedisabled)
                {
                    Console.WriteLine("Device " + address + " Disabled");
                }
                else if ((enmEventType)eventcode == enmEventType.SystemReset)
                {
                    gsTextField = "Panel Reset";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.ModuleLoadShortCircuit)
                {
                    gAlarmType = enmAlarmType.MAXFault.ToString();
                    gsTextField = "Module Load Short Circuit";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.OutputModuleTestDeActivation)
                {
                    gAlarmType = enmAlarmType.MAXOutputActivate.ToString();
                    gsTextField = "Module Activation";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.OutputModuleTestActivation)
                {
                    gAlarmType = enmAlarmType.MAXOutputActivate.ToString();
                    gsTextField = "Module DeActivation";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.DuplicateAddress)
                {
                    gAlarmType = enmAlarmType.MAXFault.ToString();
                    gsTextField = "Duplicate Address";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.AUXSet)
                {
                    gAlarmType = enmAlarmType.MAXNonFireAlarm.ToString();
                    gsTextField = "AUX Set";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.AuxCleared)
                {
                    gAlarmType = enmAlarmType.MAXNonFireAlarm.ToString();
                    gsTextField = "Aux Cleared";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.TechnicalAlarm)
                {
                    gAlarmType = enmAlarmType.MAXNonFireAlarm.ToString();
                    gsTextField = "Technical Alarm";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.PowerSupplyFault)
                {
                    gAlarmType = enmAlarmType.MAXFault.ToString();
                    gsTextField = "Power Supply Fault";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.ThermalAlarm)
                {
                    gAlarmType = enmAlarmType.MAXNonFireAlarm.ToString();
                    gsTextField = "Thermal Alarm";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.PowerSupplyFault)
                {
                    gAlarmType = enmAlarmType.MAXNonFireAlarm.ToString();
                    gsTextField = "Power Supply Fault";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.EnableZone)
                {
                    gAlarmType = enmAlarmType.MAXDisableZone.ToString();
                    Console.WriteLine("Zone " + zone + " Enabled");
                }
                else if ((enmEventType)eventcode == enmEventType.DisableZone)
                {
                    gAlarmType = enmAlarmType.MAXDisableZone.ToString();
                    Console.WriteLine("Zone " + zone + " Disabled");
                }
                else if ((enmEventType)eventcode == enmEventType.LIBCardLoopCPUFault)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 41;
                    gsTextField = "LIB Card Loop CPU Fault";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.LIBCardLoopCPUPwrRestart)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 42;
                    gsTextField = "LIB Card Loop CPU Power Restart";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.LIBCardLoopShortCircuit)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 34;
                    gsTextField = "LIB Card Loop Short Circuit";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.LIBCardDeviceZeroPresent)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 44;
                    gsTextField = "LIB Card Device Zero Present";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.LIBCardMissing)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 45;
                    gsTextField = "LIB Card Missing";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.LIBCardLoopEndDriverFault)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 46;
                    gsTextField = "LIB Card Loop End Driver Fault";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.LIBCardLoopSignalDegraded)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 47;
                    gsTextField = "LIB Card Loop Signal Degraded";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.LIBCardROMChkSumErr)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 48;
                    gsTextField = "LIB Card ROM Checksum Error";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.RS232LinkFault)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 49;
                    gsTextField = "RS232 Link Fault";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.PSUChargerFault)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 50;
                    gsTextField = "PSU Charger Fault";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.BatteryLowVoltage)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 51;
                    gsTextField = "Battery Low Voltage";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.BatteryFailure)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 52;
                    gsTextField = "Battery Failure";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.SoftwareFailure)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 36;
                    gsTextField = "Software Failure";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.PanelKeyStuck)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 40;
                    gsTextField = "Panel Key Stuck";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.AuxOutput1Fault)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 37;
                    gsTextField = "Aux Output 1 Fault";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.AuxOutput2Fault)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 38;
                    gsTextField = "Aux Output 2 Fault";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.NetworkZoneAssignIncorrect)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 53;
                    gsTextField = "Network Zone Assign Incorrect";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.NetworkRefAssingIncorrect)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 54;
                    gsTextField = "Network Ref Assign Incorrect";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.ID2NetworkZoneDup)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 55;
                    gsTextField = "ID2 Net Zone Duplication";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.ID2NetworkStartUpFaultNetCardMissing)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 56;
                    gsTextField = "ID2 Net Startup Fault NetCard Missing";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.ID2NetworkStartUpFaultNoACK)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 57;
                    gsTextField = "ID2 Net Startup Fault No Ack";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.ID2NetworkStartUpFaultNoReply)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 58;
                    gsTextField = "ID2 Net Startup Fault No Reply";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.ID2NetworkStartUpFaultJOINFail)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 59;
                    gsTextField = "ID2 Net Startup Fault Join Fail";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.ID2NetworkRunTimeFault)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();    
                    giAddressNumber = 60;
                    gsTextField = "ID2 Net Run Time Fault";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.ID2ChannelLink1Fault)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 61;
                    gsTextField = "ID2 Net Channel 1 Link Fault";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.ID2ChannelLink2Fault)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 62;
                    gsTextField = "ID2 Net Channel 2 Link Fault";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.ID2FlashChecksumErr)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 63;
                    gsTextField = "ID2 Net Flash checksum Error";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.ID2NetworkOverLoadTimeOut)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 64;
                    gsTextField = "ID2 Net OverLoad Timeout";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.OverRideSounder)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 65;
                    gsTextField = "Over-Ride Sounder/Investigation delay";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.InvestigateDelayExtended)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 66;
                    gsTextField = "Investigation delay extended";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.RemoteFireOutPutTest)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 67;
                    gsTextField = "Remote Fire Output Test";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.SignalledFaultatPanelInput1)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 99;
                    gsTextField = "Signalled Fault at Panel Input 1";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.SignalledFaultatPanelInput2)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 100;
                    gsTextField = "Signalled Fault at Panel Input 2";
                    Console.WriteLine(gsTextField);
                }
                else if ((enmEventType)eventcode == enmEventType.ExternalPSUFault)
                {
                    gAlarmType = enmAlarmType.MAXStatusEvent.ToString();
                    giAddressNumber = 102;
                    gsTextField = "External PSU Fault";
                    Console.WriteLine(gsTextField);
                }
            }

            string stracknowledge = ">IACK\r";
            //             FireFire("FIRE FIRE");

            sendserial(stracknowledge);
            Console.WriteLine(stracknowledge.Replace("\r", "") + " Sent to Panel");
        }

        protected override void heartbeat_timer_callback(object sender)
        {
          
            base.heartbeat_timer_callback (sender);

            sendserial(">IQS\r");
        }

        public override void OnStartUp()
        {
        }

        public override void Evacuate(string passedvalues)
        {
            Console.WriteLine("GOT EVACUATE PLEASE SEND TO SERIAL PORT TO RAISE ALARM");
        }
        public override void Alert(string passedvalues)
        {
            Console.WriteLine("GOT ALERT PLEASE SEND TO SERIAL PORT TO RAISE ALARM");
        }
        public override void EvacuateNetwork(string passedvalues)
        {
            Console.WriteLine("GOT EVACUATE NETWORK PLEASE SEND TO SERIAL PORT TO RAISE ALARM");
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

            if (action == ActionType.kRESET)
            {
                message = "IE" +
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
                message = "IE00129" +
                 iDayOfWeek +
                 sHour.ToString("D2") +
                 sMinute.ToString("D2") +
                 sSecond.ToString("D2") +
                 loop.ToString("D2") +
                 zone.ToString("D5");
            }
            if (action == ActionType.kDISABLEDEVICE)
            {
                message = "IE" +
                 node.ToString("D2") +
                 "024" +
                 iDayOfWeek +
                 sHour.ToString("D2") +
                 sMinute.ToString("D2") +
                 sSecond.ToString("D2") +
                 loop.ToString("D2") +
                 zone.ToString("D5") +
                 "S" +
                 device.ToString("D2")+
                 "00";
            }

            if (action == ActionType.kENABLEDEVICE)
            {
                message = "IE" +
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
                message = "IE" +
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
                message = "IE" +
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
                message = "IE00" +
                 "137" +
                 iDayOfWeek +
                 sHour.ToString("D2") +
                 sMinute.ToString("D2") +
                 sSecond.ToString("D2") +
                 "00"+
                 zone.ToString("D5");
            }

            if (action == ActionType.kENABLEZONE)
            {
                message = "IE00" +
                 "136" +
                 iDayOfWeek +
                 sHour.ToString("D2") +
                 sMinute.ToString("D2") +
                 sSecond.ToString("D2") +
                 "00" +
                 zone.ToString("D5");
            }

            string sChecksum = CreateMAXChecksum(message);

            sendserial(">" + message + sChecksum + "\r");

            Console.WriteLine(">" + message + sChecksum + " Sent to panel");
        }

        public string CreateMAXChecksum(string myString)
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
    }
}