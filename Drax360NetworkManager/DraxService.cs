using Drax360Service.Panels;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Drax360Service
{
    #region enums
    public enum ActionType
    {
        kEVACTUATE,
        kEVACTUATENETWORK,
        kALERT,
        kRESET,
        kRESETNETWORK,
        kSILENCE,
        kMUTEBUZZERS,
        kDISABLEDEVICE,
        kENABLEDEVICE,
        kDISABLEMODULE,
        kENABLEMODULE,
        kDISABLEZONE,
        kENABLEZONE,
        KANALOGUEDATA,
        KHandShake
    }
    public enum NwmData
    {
        Blank = 0,
        AlarmToAmx = 1,
        IsolationToAmx = 2,
        IsolationToNwm = 3,
        OutputControlToNwm = 4,
        SounderIsolationToNwm = 5,
        ControlIsolationToNwm = 6,
        SounderControlToNwm = 7,
        ControlControlToNwm = 8,
        MessageForSystemHistoryToAmx = 9,
        NWMErrorToAmx = 10,
        GeneralControlToNwm = 11,
        EvacuateToNwm = 12,
        AlertToNwm = 13,
        SilenceToNwm = 14,
        ResetToNwm = 15,
        BuzzerMuteToNwm = 16,
        ForceEVMAttrToAmx = 17,
        EventOutputToNwm = 18,
        StartTestToNwm = 19,
        endTestToNwm = 20
    }
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
    public enum enmPRLEventType
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
    public enum enmNotPRLAlarmType
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
    public enum enmPRLAlarmType
    {
        Fire = 0,
        NonFireAlarm = 1,
        PreAlarm = 2,
        Isolate = 4,
        TestModeFire = 6,
        Fault = 8,
        OutputActivate = 9,
        DeviceTestMode = 10,
        DisableZone = 11,
        StatusEvent = 15,
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
        UnmonitoredRelayOutput,
        ViewReferenceSensor,
        Unknown
    }
    public enum EnmMorleyEventNature
    {
        Pre_Alarm_Signal = 4,
        Fire_Alarm_Signal = 5,
        Panel_Reset = 8,
        Evacuation_Alarm = 9,
        Detector_Contaminated = 13,
        No_Reply_From_Detector = 15,
        External_Link_Master_Failed = 18,
        Detector_Data_Corrupted = 19,
        Problem_With_Loop_Wiring = 21,
        Problem_With_Sounder_Circuit = 22,
        Problem_With_PSU = 23,
        Earth_fault = 25,
        Zone_Totally_Disabled = 27,
        No_Reply_From_Slave = 28,
        Bad_Reply_From_Slave = 29,
        Walk_Test = 33,
        Zone_Partially_Disabled = 34,
        Detector_Disabled = 35,
        Relay_Outputs_Disabled = 36,
        Sounder_Outputs_Disabled = 37
    }

    #endregion

    public partial class DraxService : ServiceBase
    {
        #region constants
        const string kpipenamesend = "Drax360PipeSend";
        const string kpipenamereturn = "Drax360PipeReturn";
        const char kpipedelim = '|';
        const string kappname = "Drax360 Service";
        const int kfaketimertickseconds = 60;
        const int kfakefireinitialwakeseconds = 0;
        const string klogfilefolder = "System";
        // settings sections
        const string ksettingsetupsection = "SETUP";
        const string ksettingpanelsection = "PANEL";
        const string ksettingmainsection = "MAIN";

        // RSM Constants Start
        const int krsmport = 1471;

        protected SerialPort serialport { get; set; }

        #endregion

        #region private variables

        // RSM Start
        private TcpListener rsmtcpListener;
        private CancellationTokenSource rsmcancellationTokenSource;
        private bool rsmisListening = false;
        // RSM End


        private int _port = 3090;
        private string _address = "localhost";
        private TcpClient _tcpClient;
        private bool _connected = true;

        private NetworkStream _stream;
        private StreamWriter _writer;
        private System.Timers.Timer _heartbeatTimer;
        private NamedPipeServerStream pipeserversend = null;
       
        private string panel = "";
        private string configurationbasefolder="";

        private List<AbstractPanel> abstractpanels = new List<AbstractPanel>();

        private List<System.Threading.Timer> faketimers = new List<System.Threading.Timer>();

        private int indent = 0;
        private string[] args = null;
        private int fakemode = 0;
        private Mutex filelockmutex = new Mutex();

        public bool DebugLog { get; set; }

        #endregion

        #region private methods
        private string friendlytimestamp()
        {
            if (indent > 0) return "";

            var ukTimeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
            var ukTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ukTimeZone);

            string ret = ukTime.ToString("dd-MM-yyyy HH:mm:ss.fff", CultureInfo.InvariantCulture);

            return ret + " ";
        }

        private void ln(string message, EventLogEntryType eventtype = EventLogEntryType.Information)
        {
            Console.WriteLine("".PadLeft(indent, '\t') + message);
            Console.ResetColor();
            log(message);
        }

        private void kvp(string key, object value)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write("".PadLeft(indent, '\t') + key.Trim());

            Console.ResetColor();
            Console.WriteLine(" = " + value);

            Console.ResetColor();
            log(key + " = " + value);
        }

        private void dumpavailableserialports()
        {
            string[] ports = SerialPort.GetPortNames();
            if (ports.Length == 0)
            {
                warning("No Available Serial Ports");
                return;
            }
            title("Available Serial Ports");
            indent++;
            foreach (string port in ports)
            {
                ln(port);
            }
            indent--;
        }

        private void log(string message, EventLogEntryType eventtype = EventLogEntryType.Information)
        {
            if (this.DebugLog == true)
            {
                filelockmutex.WaitOne();

                // changed to new log file path
                string logdir = Path.Combine(configurationbasefolder, klogfilefolder);
                if (!Directory.Exists(logdir))
                {
                    Directory.CreateDirectory(logdir);
                }

                string workinglogfile = Path.Combine(logdir, DateTime.Now.ToString("yyyy-MM-dd-") + getpanel().GetFileName + ".log");
                File.AppendAllText(workinglogfile, friendlytimestamp() + " " + message + "\r\n");

                filelockmutex.ReleaseMutex();
            }
        }

        private void pad()
        {
            Console.WriteLine();
        }
        private void title(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            ln(msg);
        }

        private void warning(string warningmessage)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            ln("Warning " + warningmessage, EventLogEntryType.FailureAudit);
        }

        private void init_service()
        {
            // now go grab com ports
            abstractpanels.Clear();
            //sps.Clear();
            faketimers.Clear();

            // used to just load our settings from the ini file
            AbstractPanel apbase = getpanel();
            if (panel=="EMAIL")
            {
                string identifier = "EMAIL";
                AbstractPanel ap = getpanel(identifier);
                ap.StartUp(fakemode);
                ap.OutsideEvents += Sp_Fire;
                abstractpanels.Add(ap);
                StartDeviceWatcher();
                return;

            }


            if (panel == "RSM")
            {

                // we will read this from config later
                string identifier = "192.168.3.199";
                AbstractPanel ap = getpanel(identifier);
                ap.StartUp(fakemode);
                ap.OutsideEvents += Sp_Fire;
                abstractpanels.Add(ap);

                identifier = "192.168.3.1";
                ap = getpanel(identifier);
                ap.StartUp(fakemode);
                ap.OutsideEvents += Sp_Fire;
                abstractpanels.Add(ap);


                try
                {
                    rsmtcpListener = new TcpListener(IPAddress.Any, krsmport);
                    rsmtcpListener.Start();

                    rsmcancellationTokenSource = new CancellationTokenSource();
                    rsmisListening = true;


                    Task.Run(() => rsmListenForConnections(rsmcancellationTokenSource.Token));
                }
                catch (Exception ex)
                {
                    //MessageBox.Show($"Error starting listener: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    rsmStopListener();
                }




                StartDeviceWatcher();
                return;

            }


            for (int i = 1; i < 7; i++)
            {
                string panel = ksettingpanelsection + i;

                // now work out the settings for this panel                               
                int port = 0;

                if (apbase.Extension.ToUpper() == "MORLEY" || (apbase.Extension.ToUpper() == "MAX" & i==1))
                {
                    port = apbase.GetSetting<int>("SETUP", "CommPort");
                }
                else
                {
                    port = apbase.GetSetting<int>(panel, "CommPort");
                }

                if (port <= 0) continue;

                string identifier = "COM" + port;
                AbstractPanel ap = getpanel(identifier);

                ap.StartUp(fakemode);
                ap.OutsideEvents += Sp_Fire;

                // we are in fake mode
                if (this.fakemode > 0)
                {
                    ln("Opened Fake " + identifier + " Mode " + fakemode);
                    
                    faketimers.Add(new System.Threading.Timer(fake_timer, identifier, kfakefireinitialwakeseconds * 1000, kfaketimertickseconds * 1000));
                }
                else
                { }

                abstractpanels.Add(ap);
                break;
            }
            StartDeviceWatcher();
            
        }

        private void Sp_Fire(object sender, EventArgs e)
        {
            CustomEventArgs ex = e as CustomEventArgs;
            string msg = ex.Message.ToString();
            bool notifyui = ex.NotifyUI;
            ln(msg);
            if (notifyui)
            { 
                sendreturncmd(msg);
            }
        }

        private void Sp_Log(object sender, EventArgs e)
        {
            CustomEventArgs ex = e as CustomEventArgs;
            string msg = ex.Message.ToString();
            ln(msg);

        }

        private void fake_timer(object sender)
        {
            string identifier = sender.ToString();
            ln("Fake Timer Tick " + identifier);

            AbstractPanel ourabstractpanel = null;
            foreach (AbstractPanel ap in abstractpanels)
            {
                if (ap.Identifier == identifier)
                {
                    ourabstractpanel = ap;
                    break;
                }
            }
            if (ourabstractpanel == null) return;

            string read = ourabstractpanel.FakeString;

            byte[] bytes = Encoding.ASCII.GetBytes(read);

           
            ourabstractpanel.Parse(bytes);
        }
        private AbstractPanel getpanel(string identifier = "")
        {
            AbstractPanel ret = null;
            switch (panel)
            {

                case "ADVANCED":
                    ret = new PanelAdvanced(this.configurationbasefolder, identifier);
                    break;

                case "EMAIL":
                    ret = new PanelEmail(this.configurationbasefolder, identifier);
                    break;

                case "GENT":
                    ret = new PanelGent(this.configurationbasefolder,identifier);
                    break;

                case "MORLEYMAX":
                    ret = new PanelMorleyMax(this.configurationbasefolder, identifier);
                    break;

                case "MORLEYZX":
                    ret = new PanelMorleyZX(this.configurationbasefolder, identifier);
                    break;

                case "NOTIFIER":
                    ret = new PanelNotifier(this.configurationbasefolder, identifier);
                    break;

                case "PEARL":
                    ret = new PanelPearl(this.configurationbasefolder, identifier);
                    break;

                case "RSM":
                    ret = new PanelRSM(this.configurationbasefolder, identifier);
                    break;

                case "TAKTIS":
                    ret = new PanelTaktis(this.configurationbasefolder, identifier);
                    break;

                case "SYNCRO":
                    ret = new PanelSyncro(this.configurationbasefolder, identifier);
                    break;

                default:
                    throw new Exception("Panel Undefined " + panel);
            }
            return ret;
        }

        private void startpipeserver()
        {
            try
            {
                pipeserversend = new NamedPipeServerStream(kpipenamesend, PipeDirection.InOut, 254, PipeTransmissionMode.Message);
                ln("Pipe Server Send is Started (" + kpipenamesend + ")");
            }
            catch (Exception ex)
            {
                string err = "Error starting Pipe Server.  Check it is not running elsewhere as " + kpipenamesend+" "+ex.Message;
                // ln(err, EventLogEntryType.Error);
                throw new Exception(err);
            }
        }
        private async void startpipesend()
        {
            while (pipeserversend != null)
            {
                await pipeserversend.WaitForConnectionAsync();

                //receive message from client
                var messagebytes = readpipemessage(pipeserversend);
                string strresponse = Encoding.UTF8.GetString(messagebytes);
                ln("Message received from client: " + strresponse);
                string strret = handlepiperesponse(strresponse);
                //prepare some response
                byte[] response = null;

                try
                {
                    response = Encoding.UTF8.GetBytes(strret);
                }
                catch
                { }

                //send response to a client
                try
                {
                    pipeserversend?.Write(response ?? Array.Empty<byte>(), 0, response?.Length ?? 0);

                    pipeserversend.Disconnect();
                }
                catch (Exception ex)
                {
                    ln("Error sending response: " + ex.Message, EventLogEntryType.Error);
                }
            }
        }

        private async Task<string> sendreturnserver(string message)
        {
            using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", kpipenamereturn, PipeDirection.InOut))
            {
                pipe.Connect(5000);
                pipe.ReadMode = PipeTransmissionMode.Message;

                byte[] ba = Encoding.Default.GetBytes(message);
                pipe.Write(ba, 0, ba.Length);

                var result = await Task.Run(() =>
                {
                    return readmessagereturn(pipe);
                });

                string strresponse = Encoding.Default.GetString(result);

                Console.WriteLine("Response received from Return server: " + strresponse);

                return strresponse;
            }
        }
        private static byte[] readmessagereturn(PipeStream pipe)
        {
            if (!pipe.IsConnected) return new byte[0];

            byte[] buffer = new byte[1024];
            using (var ms = new MemoryStream())
            {
                do
                {
                    var readBytes = pipe.Read(buffer, 0, buffer.Length);
                    ms.Write(buffer, 0, readBytes);
                }
                while (!pipe.IsMessageComplete);

                return ms.ToArray();
            }
        }
        private string handlepiperesponse(string strresponse)
        {
            string passedvalues = "";
            string[] partssplit = null;
            string[] parts = strresponse.Split(kpipedelim);
            if (parts.Length > 1)
            {
                partssplit = parts[1].Split(',');
                string[] values = ExtractTextBoxValues(parts[1]);
                passedvalues = string.Join(",", values); // "1,2,3,4"
            }
            string cmd = parts[0].Trim().ToUpper();
            string ret = "OK";

            if (String.IsNullOrEmpty(cmd)) return ret;
            switch (cmd)
            {
                case "SILENCE":

                    // for now alert all connected panels to silence
                    foreach (var panel in abstractpanels)
                    {
                        panel.Silence(passedvalues);
                    }

                    break;

                case "MUTEBUZZERS":

                    // for now alert all connected panels to mute buzzers
                    foreach (var panel in abstractpanels)
                    {
                        panel.MuteBuzzers(passedvalues);
                    }

                    break;

                case "RESET":

                    // for now alert all connected panels to reset
                    foreach (var panel in abstractpanels)
                    {
                        panel.Reset(passedvalues);
                    }

                    break;

                case "EVACUATE":

                    // for now alert all connected panels to evacuate
                    foreach (var panel in abstractpanels)
                    {
                        panel.Evacuate(passedvalues);
                    }

                    break;

                case "ALERT":

                    // for now alert all connected panels to evacuate
                    foreach (var panel in abstractpanels)
                    {
                        panel.Alert(passedvalues);
                    }

                    break;

                case "EVACUATENETWORK":

                    // for now alert all connected panels to evacuate
                    foreach (var panel in abstractpanels)
                    {
                        panel.EvacuateNetwork(passedvalues);
                    }

                    break;

                case "DISABLEDEVICE":

                    if (passedvalues.Length > 0)
                    {
                        foreach (var panel in abstractpanels)
                        {
                            panel.DisableDevice(passedvalues);
                        }
                    }
                    break;

                case "ENABLEDEVICE":

                    if (passedvalues.Length > 0)
                    {
                        foreach (var panel in abstractpanels)
                        {
                            panel.EnableDevice(passedvalues);
                        }
                    }

                    break;

                case "DISABLEZONE":

                    if (passedvalues.Length > 0)
                    {
                        foreach (var panel in abstractpanels)
                        {
                            panel.DisableZone(passedvalues);
                        }
                    }
                    break;

                case "ENABLEZONE":

                    if (passedvalues.Length > 0)
                    {
                        foreach (var panel in abstractpanels)
                        {
                            panel.EnableZone(passedvalues);
                        }
                    }

                    break;

                case "ANALOGUE":

                    if (passedvalues.Length > 0)
                    {
                        foreach (var panel in abstractpanels)
                        {
                            panel.Analogue(passedvalues);
                        }
                    }

                    break;

                case "GETPANELTYPE":
                    ret = panel;
                    break;

                case "GETCOMMPORTSTATUS":
                    if (partssplit.Length != 1) break;
                    string identifier = partssplit[0];
                    DateTime lastSeen = DateTime.MinValue;
                    AbstractPanel ourabstractpanel = null;
                    foreach (AbstractPanel ap in abstractpanels)
                    {
                        if (ap.Identifier == identifier)
                        {
                            ourabstractpanel = ap;
                            lastSeen = ourabstractpanel.lastDataReceived;
                         
                            break;
                        }
                    }
                    if (ourabstractpanel == null) return "ERROR";
                    ret = ourabstractpanel.SerialPortIsOpen() ? "CONNECTED" : "DISCONNECTED";
                    if (ret == "CONNECTED")
                    {
                        if (lastSeen > DateTime.MinValue)
                        {
                            ret = "Data Last Received: " + lastSeen.ToString();
                        }
                    }
                    break;

                case "TEST BOX":

                    if (partssplit.Length >= 4)
                    {
                        int p1 = int.Parse(partssplit[0]); int p2 = int.Parse(partssplit[1]);
                        int p3 = int.Parse(partssplit[2]); int p4 = int.Parse(partssplit[3]);

                        int evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1, true);
                        CSAMXSingleton.CS.SendAlarmToAMX(evnum, "##TEST", "", "");
                        CSAMXSingleton.CS.FlushMessages();
                    }
                    break;

                case "TEST BOX RESET":

                    if (partssplit.Length >= 4)
                    {
                        int p1 = int.Parse(partssplit[0]); int p2 = int.Parse(partssplit[1]);
                        int p3 = int.Parse(partssplit[2]); int p4 = int.Parse(partssplit[3]);

                        int evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1, false);
                        CSAMXSingleton.CS.SendResetToAMX(evnum, "##TEST", "", "");
                        CSAMXSingleton.CS.FlushMessages();
                    }
                    break;

                case "SETTINGSGET":
                    if (partssplit.Length != 2) break;
                    {
                        string section = partssplit[0];
                        string key = partssplit[1];

                        ret = SettingsSingleton.Instance(panel).GetSetting<string>(section, key);
                    }
                    break;

                case "SETTINGSGETKEYSINSECTION":
                    if (partssplit.Length != 1) break;
                    {
                        string section = partssplit[0];
                        ret = SettingsSingleton.Instance(panel).GetSettingsKeysInSection(section);
                    }
                    break;

                case "SETTINGSGETSECTIONS":
                    ret = SettingsSingleton.Instance(panel).GetSettingSections();
                    break;

                case "SETTINGSSET":
                    if (partssplit.Length != 3) break;
                    {
                        string section = partssplit[0];
                        string key = partssplit[1];
                        string value = partssplit[2];
                        SettingsSingleton.Instance(panel).SetSetting(section, key, value);
                    }
                    break;

                case "SETTINGSSAVE":
                    SettingsSingleton.Instance(panel).SaveSettings();
                    break;

                case "SERVICERESTART":
                    init_service();
                    break;

                case "SETTINGSRELOAD":
                    SettingsSingleton.Instance(panel).ReLoadSettings();
                    break;

                default:

                    ln("Pipe Message Not Handled " + cmd);
                    throw new Exception("Pipe Message Not Handled " + cmd);
            }

            return ret;
        }
        private string[] ExtractTextBoxValues(string input)
        {
            // Match "Text: X" and extract X
            var matches = Regex.Matches(input, @"Text:\s*(\d+)");
            return matches.Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
        }
        private static byte[] readpipemessage(PipeStream pipe)
        {
            if (!pipe.IsConnected) return new byte[0];
            byte[] buffer = new byte[2048];
            using (var ms = new MemoryStream())
            {
                do
                {
                    var readBytes = pipe.Read(buffer, 0, buffer.Length);
                    ms.Write(buffer, 0, readBytes);
                }
                while (!pipe.IsMessageComplete);

                return ms.ToArray();
            }
        }

        private void stoppipeserver()
        {
            if (pipeserversend != null)
            {
                ln("Pipe Server Send is stopping...");
                pipeserversend.Close();
                pipeserversend.Dispose();
                pipeserversend = null;
            }
        }
        #endregion

        #region constructors
        public DraxService()
        {
            InitializeComponent();
        }
        #endregion

        #region public methods
        public void Run(string[] args)
        {
            this.DebugLog = true;
            this.args = args;

            // singular for now
            panel = ConfigurationManager.AppSettings["Panels"].Trim().ToUpper();

            // New log file path
            configurationbasefolder = ConfigurationManager.AppSettings["Configuration"].Trim();

            if (!Directory.Exists(configurationbasefolder))
            {
                Directory.CreateDirectory(configurationbasefolder);
            }
            if (!firstruncheck()) return;



            // determine if we are in a fake mode
            fakemode = Convert.ToInt32(ConfigurationManager.AppSettings["FakeMode"].Trim());



            string longbar = "".PadRight(48, '-');

            string msg = " " + kappname + " Started  ";
            string shortbar = "".PadRight((longbar.Length - msg.Length) / 2, '-');
            title(longbar);

            title(shortbar + msg + shortbar);
            title(longbar);




            if (args.Length > 0)
            {
                try
                {


                }
                catch
                { }
            }
            else
            {
                EventLogger.WriteToEventLog("No Command Line Args", EventLogEntryType.Warning);
            }

            kvp("Version", Assembly.GetEntryAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>().Version);
            kvp("Panel", panel);
            kvp("Configuration", this.configurationbasefolder);
            if (!Elements.isService)
            {
                title("Interactive Session");
                Console.Title = kappname;
            }
            if (fakemode > 0)
            {
                title("Fake Mode");
            }

            pad();

            AbstractPanel apbase = getpanel();
            switch (panel)

            {
                case "ADVANCED":
                    this.DebugLog = apbase.GetSetting<bool>(ksettingmainsection, "DesignTime");
                    break;
                case "EMAIL":
                    this.DebugLog = apbase.GetSetting<bool>(ksettingmainsection, "Debug");
                    break;
                case "GENT":
                    this.DebugLog = Convert.ToBoolean(apbase.GetSetting<int>(ksettingsetupsection, "DataLogging"));
                    break;
                case "MORLEYMAX":
                    this.DebugLog = Convert.ToBoolean(apbase.GetSetting<int>(ksettingsetupsection, "DataLoggingSet"));
                    break;
                case "MORLEYZX":
                    this.DebugLog = Convert.ToBoolean(apbase.GetSetting<int>(ksettingsetupsection, "DebugLog"));
                    break;
                case "NOTIFIER":
                    this.DebugLog = Convert.ToBoolean(apbase.GetSetting<int>(ksettingsetupsection, "DesignTime"));
                    break;
                case "PEARL":
                    this.DebugLog = Convert.ToBoolean(apbase.GetSetting<int>(ksettingsetupsection, "DesignTime"));
                    break;
                case "RSM":
                    this.DebugLog = Convert.ToBoolean(apbase.GetSetting<int>(ksettingsetupsection, "Debug"));
                    break;
                case "TAKTIS":
                    this.DebugLog = Convert.ToBoolean(apbase.GetSetting<int>(ksettingsetupsection, "DataLogging"));
                    break;
                case "SYNCRO":
                    this.DebugLog = Convert.ToBoolean(apbase.GetSetting<int>(ksettingsetupsection, "CreateLog"));
                    break;
                default:
                    this.DebugLog = true;
                    break;
            }
            AMXTransfer amxtransfer = new AMXTransfer();
            amxtransfer.OutsideEvents += Sp_Log;
            AMXTransfer.Instance.Run(args);

            startpipeserver();

            if (panel != "TAKTIS" && panel != "EMAIL" && panel != "RSM")
            {
                pad();
                dumpavailableserialports();
                pad();
            }

            startpipesend();
            CSAMXSingleton.CS.Startup(configurationbasefolder, apbase.Extension);
            CSAMXSingleton.CS.OutsideEvents += Sp_Log;

            init_service();    // start the service
        }
       
        public string sendreturncmd(string cmd, string parameters = "")
        {
            string strcmd = cmd;
            if (!string.IsNullOrEmpty(parameters))
            {
                strcmd += kpipedelim + parameters;
            }

            string result = "";
           
            try
            {
                result = Task.Run(() => sendreturnserver(strcmd)).Result;
            }
            catch (Exception ex)
            {
                result = "Error: " + ex;
            }

            return result;
        }

        public void Stopit()
        {
            indent = 0;
            ln("Stopping Service");
            if (panel == "RSM")
            {
                rsmStopListener();
            }
            stoppipeserver();
            // close fake timers
            if (this.fakemode > 0)
            {
                foreach (System.Threading.Timer timer in this.faketimers)
                {

                    timer.Dispose();

                }
                faketimers.Clear();
            }

            // close serial ports
            foreach (AbstractPanel ap in abstractpanels)
            {
                ap.Shutdown();
            }
            abstractpanels.Clear();
           
            try
            {
                _tcpClient?.Close();
                _tcpClient?.Dispose();
                _tcpClient = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error closing TCP connection: " + ex.Message);
            }
            ln("Stopped Service");
        }

        private void StartDeviceWatcher()
        {
            var watcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent"));
            watcher.EventArrived += (s, e) =>
            {
                Console.WriteLine("Device change detected. Rescanning ports...");
                RescanPorts();
            };
            watcher.Start();
        }

        private bool firstruncheck()
        {
            const string kinifolder = "";
            string inifolder = Path.Combine(this.configurationbasefolder, kinifolder);
            if (!Directory.Exists(inifolder))
            {
                Directory.CreateDirectory(inifolder);
            }
            var dirInfo = new DirectoryInfo(inifolder);
            var allFiles = dirInfo.GetFiles("*." + "ini", SearchOption.TopDirectoryOnly);
            if (allFiles.Length == 0) {
                ln("Error No Ini Files Copied into " + inifolder,EventLogEntryType.Error);
                return false; }

            return true;
        }

        private void RescanPorts()
        {
            var availablePorts = SerialPort.GetPortNames();

            foreach (AbstractPanel panel in abstractpanels)
            {
                if (!panel.SerialPortIsOpen())
                {
                    panel.TryReconnect();
                }
            }
        }


        private async void rsmListenForConnections(CancellationToken token)
        {
            while (rsmisListening && !token.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await rsmtcpListener.AcceptTcpClientAsync();
                    _ = Task.Run(() => rsmHandleClient(client, token), token);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (rsmisListening)
                    {
                        //AppendData($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Error accepting connection: {ex.Message}\n");
                    }
                }
            }
        }

        private async void rsmHandleClient(TcpClient client, CancellationToken token)
        {
            string clientIP = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
            bool found = false;
            PanelRSM rsmPanel = null;
            foreach (AbstractPanel ap in abstractpanels)
            {
                if (ap.Identifier == clientIP)
                {
                    found = true;
                    rsmPanel = ap as PanelRSM;
                    break;
                }
            }
            if (!found)
            {
                client.Close();
                return;
            }

            try
            {
                using (NetworkStream stream = client.GetStream())
                {
                    byte[] buffer = new byte[4096];
                    int bytesRead;

                    while (!token.IsCancellationRequested && (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                    {
                      
                        
                        // Create a copy of the received data
                        byte[] receivedData = new byte[bytesRead];
                        Array.Copy(buffer, receivedData, bytesRead);
                        byte[] ack = rsmPanel.Parse(receivedData);
                        if (ack != null && ack.Length > 0)
                        {
                            await stream.WriteAsync(ack, 0, ack.Length, token);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Only log errors that aren't the expected "connection closed by remote host"
                if (!token.IsCancellationRequested && !ex.Message.Contains("forcibly closed"))
                {
                    //AppendData($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Error handling client: {ex.Message}\n");
                }
            }
            finally
            {
                client.Close();
                //AppendData($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Client disconnected from {clientIP}\n");
            }
        }

        private void rsmStopListener()
        {
            rsmisListening = false;
            rsmcancellationTokenSource?.Cancel();
            rsmtcpListener?.Stop();
        }

        #endregion

        #region protected methods
        protected override void OnStart(string[] args)
        {
            EventLogger.WriteToEventLog("Service is starting...", EventLogEntryType.Information);
            try
            {
                Run(args);
            }
            catch(Exception e)
            {
                EventLogger.WriteToEventLog(e.Message, EventLogEntryType.Error);
                Console.Error.Write(e.Message);
            }
        }

        protected override void OnStop()
        {
            EventLogger.WriteToEventLog("Service is Stopping...", EventLogEntryType.Information);
            try
            {
                Stopit();
            }
            catch (Exception e)
            {
                EventLogger.WriteToEventLog(e.Message, EventLogEntryType.Error);
                Console.Error.Write(e.Message);
            }
        }
        #endregion
    }
}