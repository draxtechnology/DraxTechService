using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Instrumentation;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Remoting.Contexts;
using System.Runtime.Remoting.Messaging;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using static Drax360Service.Panels.PanelTaktis;
using System.Globalization;

namespace Drax360Service.Panels
{

    public enum TransmissionType
    {
        RX,
        TX
    }

    public enum enmTAKMessageType
    {
        TAKMsgNAK = 0,
        TAKMsgACK = 1,
        TAKMsgEventID = 2,
        TAKMsgNodeName = 7,
        TAKMsgQueryXFERProtVer = 46,
        TAKMsgPanelInFire = 60,
        TAKMsgAnalogueReading = 62,
        TAKMsgQueryAnalogDetail = 68,
        TAKMsgEventAck = 86,
        TAKMsgRequestXMLConfig = 163,
        TAKMsgSendXMLConfig = 168,
        TAKMsgRequestTime = 184,
        TAKMsgGetPanelID = 202,
        TAKMsgQueryLEDState = 247
    }

    public enum enmTAKEventType
    {
        TAKEventFire = 0,
        TAKEventEvacuate = 1,
        TAKEventAlert = 2,
        TAKEventAlarmPreAlarm = 3,
        TAKEventSecurity = 4,
        TAKEventFault = 5,
        TAKEventDisablement = 6,
        TAKEventTechAlarm = 7,
        TAKEventAlarmTest = 8,
        TAKEventStatus = 9,
        TAKEventCeaction = 10,
        TAKEventNone = 11,
        TAKEventMax = 12,
        TAKEventOther = 13,
        TAKEventAll = 14,
        TAKEventUnknown = 15
    }

    public enum enmTAKEventCode
    {
        TAKNone = 0,
        TAKPsFault = 1,
        TAKCalibrationFault = 2,
        TAKOutput1OpenFault = 3,
        TAKOutput1ShortFault = 4,
        TAKOutput2OpenFault = 5,
        TAKOutput2ShortFault = 6,
        TAKInputOpenFault = 7,
        TAKInputShortFault = 8,
        TAKInternalFault = 9,
        TAKMaintenanceFault = 10,
        TAKDetectorFault = 11,
        TAKSlaveOpenFault = 12,
        TAKSlaveShortFault = 13,
        TAKSlave1ShortFault = 14,
        TAKSlave2ShortFault = 15,
        TAKDisconnectedFault = 16,
        TAKDoubleAddressFault = 17,
        TAKMonitoredOutputFault = 18,
        TAKUnknownDeviceFault = 19,
        TAKUnexpectedDeviceFault = 20,
        TAKWrongDeviceFault = 21,
        TAKInitialisingDevice = 22,
        TAKStart = 23,
        TAKAutolearn = 24,
        TAKPcConfig = 25,
        TAKEarthFault = 26,
        TAKLoopWiringFault = 27,
        TAKLoopShortCctFault = 28,
        TAKLoopOpenCctFault = 29,
        TAKMainsFailedFault = 30,
        TAKLowBatteryFault = 31,
        TAKBatteryDisconnectedFault = 32,
        TAKBatteryOverchargeFault = 33,
        TAKAux24vFuseFault = 34,
        TAKChargerFault = 35,
        TAKRomFault = 36,
        TAKRamFault = 37,
        TAKWatchDogOperated = 38,
        TAKBadDataFault = 39,
        TAKUnknownEventFault = 40,
        TAKModemActive = 41,
        TAKPrinterFault = 42,
        TAKEn54VersionFault = 43,
        TAKEventPreAlarm = 44,
        TAKCalibrationFailedFault = 45,
        TAKModemFault = 46,
        TAKInitDevice = 47,
        TAKInputActivated = 48,
        TAKOpticalElementFault = 49,
        TAKHeatElementFault = 50,
        TAKBothElementFault = 51,
        TAKSelfTestFailedFault = 52,
        TAKCeActive = 53,
        TAKLoopProtocolFault = 54,
        TAKLoopMissing = 55,
        TAKLoopUnexpected = 56,
        TAKSubAddressLimit = 57,
        TAKIoModMissing = 58,
        TAKIoModUnexpected = 59,
        TAKSerialInput = 60,
        TAKNetUnexpectedNode = 61,
        TAKNetUnknownType = 62,
        TAKNetMissingNode = 63,
        TAKNetUnexpectedCard = 64,
        TAKNetMissingCard = 65,
        TAKNetWrongAddress = 66,
        TAKNetBroken = 67,
        TAKNetCommsFault = 68,
        TAKNetCommsTimeout = 69,
        TAKNetInvalidAddress = 70,
        TAKSounderBoardUnexpected = 71,
        TAKRelayBoardUnexpected = 72,
        TAKSounderBoardMissing = 73,
        TAKRelayBoardMissing = 74,
        TAKZoneIoUnexpected = 75,
        TAKZoneIoMissing = 76,
        TAKSystemFault = 77,
        TAKDisableDevice = 78,
        TAKDisableZone = 79,
        TAKDisableLoop = 80,
        TAKDisableSounders = 81,
        TAKDisablePanelInput = 82,
        TAKDisablePanelOutput = 83,
        TAKDisableCe = 84,
        TAKDisableBuzzer = 85,
        TAKDisablePrinter = 86,
        TAKDisableEarthFault = 87,
        TAKDayNightDisable = 88,
        TAKGeneralDisablement = 89,
        TAKOemDevice = 90,
        TAKEventTest = 91,
        TAKZoneIoUnexpectedUsa = 92,
        TAKZoneIoMissingUsa = 93,
        TAKDisableImmediateOutput = 94,
        TAKMemoryWriteEnableOn = 95,
        TAKAnnunMissing = 96,
        TAKAnnunUnexpected = 97,
        TAKLcdPowerFault = 98,
        TAKModulePowerSupplyFault = 99,
        TAKOutputShortFault = 100,
        TAKOutputOpenFault = 101,
        TAKAddressing = 102,
        TAKAutoAddressingFailure = 103,
        TAKDevBatteryLow = 104,
        TAKDevTamperFault = 105,
        TAKDevExtInterference = 106,
        TAKDevFataFault = 107,
        TAKIsolatorOpen = 108,
        TAKMicroProcessorFault = 109,
        TAKPrismReflectorTrgetting = 110,
        TAKAlignmentMode = 111,
        TAKHighSpeedFault = 112,
        TAKContaminationReached = 113,
        TAKAudioFault = 114,
        TAKHeadMissingFault = 115,
        TAKTamperFault = 116,
        TAKSignalStrengthFault = 117,
        TAKRadBatteryFault = 118,
        TAKSounderMissingFault = 119,
        TAKDevBackBatteryLow = 120,
        TAKSlaveExpLoss = 121,
        TAKEightZoneMimicMissing = 122,
        TAKEightZoneMimicUnexpected = 123,
        TAKSixteenZoneMimicMissing = 124,
        TAKSixteenZoneMimicUnexpected = 125,
        TAKBattImpFailed = 126,
        TAKAerialTamperFault = 127,
        TAKBackGroundOutOfRange = 128,
        TAKHeadFault = 129,
        TAKHeadDirtyCompensation = 130,
        TAKTamperInputFault = 131,
        TAKReceiverFault = 132,
        TAKBatteryFault = 133,
        TAKFuseTrip = 134,
        TAKCurrentLimitFault = 135,
        TAKVoltageLimitFault = 136,
        TAKWeakOpenCircuit = 137,
        TAKWeakShortCircuit = 138,
        TAKOpenCircuitFault = 139,
        TAKShortCircuitFault = 140,
        TAKBoardAMissing = 141,
        TAKBoardBMissing = 142,
        TAKLoopCommsTimeout = 143,
        TAKAllOutputDisabled = 144,
        TAKAllSoundersDisabled = 145,
        TAKAllZoneDisabled = 146,
        TAKLoopPrimUnderVoltage = 147,
        TAKLoopSecUnderVoltage = 148,
        TAKLoopBoardMissing = 149,
        TAKLoopBoardUnexpected = 150,
        TAKPSUEarthFault = 151,
        TAKExtinguishantActivated = 152,
        TAKPSUFault = 153,
        TAKUserLoggedIn = 154,
        TAKAutolearnDevice = 155,
        TAKClassWiringFault = 156,
        TAKIfamMissing = 157,
        TAKCommunicatorMissing = 158,
        TAKCommsFailure = 159,
        TAKCommsRestored = 160,
        TAKVnetTrouble = 161,
        TAKVnetOpen = 162,
        TAKVnetShorted = 163,
        TAKVnetRestored = 164,
        TAKVnetTransFailure = 165,
        TAKVnetNodeMissing = 166,
        TAKVnetExtraNode = 167,
        TAKVnetTransRestored = 168,
        TAKLanNotConnected = 169,
        TAKLanNetNotRecognised = 170,
        TAKLanGatewayAccessFail = 171,
        TAKLanToDcCommsFail = 172,
        TAKLanToDcCommsRestored = 173,
        TAKDcCommsFailure = 174,
        TAKDcCommsRestored = 175,
        TAKPhoneLine1Trouble = 176,
        TAKPhoneLine1Restored = 177,
        TAKPhoneLine2Trouble = 178,
        TAKPhoneLine2Restored = 179,
        TAKVerification = 180,
        TAKAllPlantOutputDisabled = 181,
        TAKEventLogCleared = 182,
        TAKBootloaderUpdate = 183,
        TAKBootloaderFailed = 184,
        TAKDelayExtended = 185,
        TAKDisableModuleIoChannel = 186,
        TAKMissingIoModTaktisSounder = 187,
        TAKMissingIoModTaktisZone = 188,
        TAKMissingIoModTaktisRelay = 189,
        TAKMissingIoModTaktisMultiIo = 190,
        TAKUnexpectedIoModTaktisSounder = 191,
        TAKUnexpectedIoModTaktisZone = 192,
        TAKUnexpectedIoModTaktisRelay = 193,
        TAKUnexpectedIoModTaktisMultiIo = 194,
        TAKMgwAct1ComsTrouble = 195,
        TAKMgwAct1ConfTrouble = 196,
        TAKMgwAct2ComsTrouble = 197,
        TAKMgwAct2ConfTrouble = 198,
        TAKMgwAct3ComsTrouble = 199,
        TAKMgwAct3ConfTrouble = 200,
        TAKMgwAct4ComsTrouble = 201,
        TAKMgwAct4ConfTrouble = 202,
        TAKMgwIpnetConfTrouble = 203,
        TAKMgwIpnetComsTrouble = 204,
        TAKMgwInternalTrouble = 205,
        TAKMgwMissing = 206,
        TAKMgwDisabled = 207,
        TAKNetworkOutputPartialShortCircuitFault = 208,
        TAKNetworkOutputPartialOpenCircuitFault = 209,
        TAKNetworkOutputFullShortCircuitFault = 210,
        TAKNetworkOutputFullOpenCircuitFault = 211,
        TAKNetworkOutputConnectionFault = 212,
        TAKNetworkOutputCommunicationFault = 213,
        TAKNetworkInputPartialShortCircuitFault = 214,
        TAKNetworkInputPartialOpenCircuitFault = 215,
        TAKNetworkInputFullShortCircuitFault = 216,
        TAKNetworkInputFullOpenCircuitFault = 217,
        TAKNetworkInputConnectionFault = 218,
        TAKNetworkInputCommunicationFault = 219,
        TAKNetworkMissingNodes = 220,
        TAKNetworkConnectionFault = 221,
        TAKNetworkRepeatAddress = 222,
        TAKLedMissingBoard = 223,
        TAKMissingIoModFan = 224,
        TAKMissingIoModAncillary = 225,
        TAKMissingIoModLed = 226,
        TAKUnexpectedIoModFan = 227,
        TAKUnexpectedIoModAncillary = 228,
        TAKUnexpectedIoModLed = 229,
        TAKTestOnOutput = 230,
        TAKTestOnLed = 231,
        TAKTestOnIsolator = 232,
        TAKStorageInserted = 233,
        TAKMonitoredInputFault = 234,
        TAKImportRead = 235,
        TAKImportWrite = 236,
        TAKExportWrite = 237,
        TAKMgwUnexpected = 238,
        TAKMgwCoElementFault = 239,
        TAKCoLifeFault = 240,
        TAKEepromFault = 241,
        TAKPositiveAlarmDisabled = 242,
        TAKCeNotRunning = 243,
        TAKMgwLicenceMissing = 244,
        TAKMgwDialerDisabled = 245,
        TAKLoopPowerOff = 246,
        TAKDisableNetwork = 247
    }


    public class TimerEventArgs : EventArgs
    {
        public int Interval { get; set; }
    }
    public class TakControl
    {
        public string[] EncodedPacket { get; set; }
        public TransmissionType RXTX { get; set; }
    }

    public class TakControlRX : TakControl
    {
        public string[] EncodedPacketRX
        {
            get => EncodedPacket;
            set => EncodedPacket = value;
        }
    }
    public class SendImmediateEventArgs : EventArgs
    {
        public string[] Data { get; set; }
        public TransmissionType TransmissionType { get; set; }
    }
    internal partial class PanelTaktis : AbstractPanel
    {

        #region Enums
        public enum TakSendType
        {
            TAKsendEventStart,
            TAKSendEventCLEAR,
            TAKSendRequestActEvents,
            TAKSendRequestActEventsTX,
            TAKSendRequestEventLog,
            TAKSendRequestEventLogEx,
            TAKSendEventACKRX,
            TAKSendEventACKTX,
            TAKSendACK,
            TAKSendNACK,
            TAKSendDevicIsolateStatus,
            TAKSendAnalReading,
            TAKSendGetNodeNum,
            TAKSendGetPanelID,
            TAKSendStartConnectionMonitoringRX,
            TAKSendStartConnectionMonitoringTX,
            TAKSendStopConnectionMonitoringTX,
            TAKSendStopConnectionMonitoringRX,
            TAKSendHeartBeatTX,
            TAKSendHeartBeatRX,
            TAKSendNodeName,
            TAKSendPanelInFire,
            TAKSendQueryANALDetails,
            TAKSendControlEnableDevice,
            TAKSendControlDisableDevice,
            TAKSendControlEnableZone,
            TAKSendControlDisableZone,
            TAKSendQueryLEDState,
            TAKSendQueryPanelType,
            TAKSendControlReset,
            TAKSendControlSilence,
            TAKSendControlStartAlert,
            TAKSendControlStartEVAC,
            TAKSendControlStartPreAlarm,
            TAKSendQueryXFERProptVer,
            TAKSendRequestCurrentTime,
            TAKSendSetTestMode,
            TAKSendSetTime,
            TAKSendRequestXMLConfig,
            TAKSendSendXMLConfig,
            TAKSendControlPIOOutput,
            TAKSendControlIOChannel,
            TAKSendControlSubAddress,
            TAKSendControlSounder,
            TAKSendControlZone,
            TAKSendControlSilenceBuzzer,
            TAKSendControlResound,
            TAKSendControlStartTest
        }



        #endregion

        #region Constants
        public static class TakCommands
        {
            public const string CMD_ACK = "1";
            public const string CMD_NACK = "0";
            public const string CMD_REQUEST_ACTIVE_EVENTS = "79";
            public const string CMD_REQUEST_EVENT_LOG = "78";
            public const string CMD_EVENT_ACK = "134";
            public const string CMD_HEARTBEAT = "86";
            public const string CMD_START_MONITORING = "231";
            public const string CMD_STOP_MONITORING = "244";
            public const string CMD_RESET = "77";
            public const string CMD_SILENCE = "76";
            public const string CMD_START_ALERT = "74";
            public const string CMD_START_EVAC = "72";
            public const string CMD_SILENCE_BUZZER = "75";
            public const string CMD_RESOUND = "80";
            public const string CMD_START_TEST = "81";
            public const string CMD_SET_TIME = "88";
            public const string CMD_QUERY_ANAL_DETAILS = "68";
            public const string CMD_ENABLE_DEVICE = "71";
            public const string CMD_DISABLE_DEVICE = "70";
            public const string CMD_ENABLE_ZONE = "91";
            public const string CMD_DISABLE_ZONE = "90";
            public const string CMD_CONTROL = "197";
        }

        public static class DeviceTypes
        {
            public const string SUB_ADDRESS = "0";
            public const string ZONE = "4";
            public const string PIO_OUTPUT = "6";
            public const string IO_CHANNEL = "8";
            public const string SOUNDER = "12";
        }
        #endregion

        #region private fields   
        private System.Timers.Timer _txTimer;
        private System.Timers.Timer _rxTimer;

        private readonly Queue<TakControl> _txQueue = new Queue<TakControl>();
        private readonly Queue<TakControlRX> _rxQueue = new Queue<TakControlRX>();
        private bool _txRestart;

        private bool _messageSent = false;
        private bool _reconnect = false;
        private bool _forceReset = false;
        private string[] _timeArray;

        private int _heartBeatCount = 0;
        private long _numHeartbeats = 0;
        private bool _heartBeatSent = false;

        private bool _connectionMonitoringSent = false;
        private bool _connectionMonitoringSentTX = true;
        private bool _connectionStopMonitoringSentRX = false;
        private bool _connectionStopMonitoringSentTX = false;
        private bool _requestEventLogSent = false;
        private bool _requestEventLogEXSent = false;

        private bool _commError = false;
        public TcpClient client;
        #endregion

        #region Events
        public event EventHandler<SendImmediateEventArgs> SendImmediateRequest;
        public event EventHandler<TimerEventArgs> StartTxTimer;
        public event EventHandler<TimerEventArgs> StartRxTimer;
        #endregion

        #region public properties
        public int TxQueueCount => _txQueue.Count;
        public int RxQueueCount => _rxQueue.Count;
        public long[] glSerialNo = new long[4];
        #endregion
        public override string FakeString => throw new NotImplementedException();

        public override void Alert(string passedValues)
        {
            throw new NotImplementedException();
        }

        public override void DisableDevice(string passedValues)
        {
            throw new NotImplementedException();
        }

        public override void DisableZone(string passedValues)
        {
            throw new NotImplementedException();
        }

        public override void EnableDevice(string passedValues)
        {
            throw new NotImplementedException();
        }

        public override void EnableZone(string passedValues)
        {
            throw new NotImplementedException();
        }

        public override void Evacuate(string passedValues)
        {
            throw new NotImplementedException();
        }

        public override void EvacuateNetwork(string passedValues)
        {
            throw new NotImplementedException();
        }

        public override void MuteBuzzers(string passedValues)
        {
            throw new NotImplementedException();
        }

        public override void Reset(string passedValues)
        {
            throw new NotImplementedException();
        }

        public override void Silence(string passedValues)
        {
            throw new NotImplementedException();
        }
        protected override void heartbeat_timer_callback(object sender)
        {
            base.heartbeat_timer_callback(sender);
            sendtotaktis(TakSendType.TAKSendHeartBeatTX, clientID: 1);
        }

        private string gsIPAddress;
        private string gsIPPort;
        private TcpClientWithEvents _tcp;
        public override void StartUp(int fakemode)
        {
            gsIPAddress = base.GetSetting<string>(ksettingsetupsection, "PanelIPAddress");
            gsIPPort = base.GetSetting<string>(ksettingsetupsection, "IPPort");
            EnsureConnected();

            // RJ switched off timers
            //_messageSender.LogMessage += OnLogMessage;
            //SendImmediateRequest += OnSendImmediateRequest;
            //StartTxTimer += OnStartTxTimer;
            //StartRxTimer += OnStartRxTimer;


            sendtotaktis(TakSendType.TAKSendRequestActEvents);

            //sendtotaktis(TakSendType.TAKSendRequestActEventsTX, clientID: 1);


            // Initialize timers
            _txTimer = new System.Timers.Timer();
            _txTimer.Interval = 1000;
            _txTimer.Elapsed += TxTimer_Elapsed;
            // added a start
            _txTimer.Start();

            _rxTimer = new System.Timers.Timer();
            _rxTimer.Elapsed += RxTimer_Elapsed;
            _rxTimer.Interval = 1000;
            _rxTimer.Start();

            Thread.Sleep(10000); // wait for response needs to be long enough to decode message to get serial number

            sendtotaktis(TakSendType.TAKSendRequestEventLog, glSerialNo);

            /*
            string[] tosend = new string[12];
            for (int i = 0; i < 12 - 1; i++)
                tosend[i] = "0";
            tosend[3] = 12.ToString();
            tosend[7] = 78.ToString();
            tosend[8] = glSerialNo[0].ToString()
            tosend[9] = glSerialNo[1].ToString();
            tosend[10] = glSerialNo[2].ToString();
            tosend[11] = glSerialNo[3].ToString();

            byte[] data = convertstringarraytobytearray(tosend);

            stream.Write(data, 0, data.Length);
            stream.Flush();
            */

            //sendtotaktis(TakSendType.TAKSendRequestEventLogEx, glSerialNo);

            //sendtotaktis(TakSendType.TAKSendStartConnectionMonitoringTX, glSerialNo, clientID: 1);
            EnsureConnected();
            StartListening();


        }

        private void OnSendImmediateRequest(object sender, SendImmediateEventArgs e)
        {
            SendImmediateRequest?.Invoke(this, e);
        }

        public PanelTaktis(string baselogfolder, string identifier) : base(baselogfolder, identifier, "TAKMan", "TAK")
        {
            if (!String.IsNullOrEmpty(identifier))
            {
                // heartbeat_timer = new Timer(heartbeat_timer_callback, this.Identifier, 1000, kHeartbeatDelaySeconds * 1000);
            }
        }

        #region private methods

        private void sendtotaktis(
            TakSendType sendType,
            long[] serialNo = null,
            int node = 0,
            int zone = 0,
            int loop = 0,
            int address = 0,
            int subAddress = 0,
            int clientID = 0,
            bool onOff = false)
        {
            try
            {

                NotifyClient($"SendToTakTis - Type: {sendType}");

                bool stopSending = false;
                TransmissionType rxTx = TransmissionType.TX;
                bool immediateRxSend = false;
                bool immediateTxSend = false;

                // Adjust loop index (VB code had piLoop - 1)
                if (loop >= 0)
                {
                    loop = loop - 1;
                    NotifyClient($"Loop adjusted to: {loop}");
                }


                // Handle message sent state
                if (_messageSent)
                {
                    stopSending = true;
                    // Timer would be disabled here
                }


                // Build message based on type
                string[] dataToSend = buildmessage(
                    sendType,
                    serialNo,
                    node,
                    zone,
                    loop,
                    address,
                    subAddress,
                    clientID,
                    onOff,
                    ref rxTx,
                    ref immediateRxSend,
                    ref immediateTxSend);

                if (dataToSend == null)
                {
                    NotifyClient($"Unknown send type: {sendType}");
                    return;
                }

                // Handle immediate send
                if (immediateTxSend || immediateRxSend)
                {
                    immediateRxSend = false;
                    immediateTxSend = false;

                    NotifyClient($"Send {rxTx} Immediate");
                    /*OnSendImmediateRequest(new SendImmediateEventArgs
                    {
                        Data = dataToSend,
                        TransmissionType = rxTx
                    });
                    */


                    // RJ New
                    QueueMessage(dataToSend, rxTx, stopSending);
                    TxTimer_Elapsed(this, null);

                    return;
                }

                // Queue the message
                QueueMessage(dataToSend, rxTx, stopSending);
            }
            catch (Exception ex)
            {
                NotifyClient($"Error in SendToTakTis: {ex.Message}");
            }
        }

        private string[] buildmessage(
            TakSendType sendType,
            long[] serialNo,
            int node,
            int zone,
            int loop,
            int address,
            int subAddress,
            int clientID,
            bool onOff,
            ref TransmissionType rxTx,
            ref bool immediateRxSend,
            ref bool immediateTxSend)
        {
            string[] data = null;
            string[] serialNoStr = convertserialnumber(serialNo);
            string clientIDStr = clientID.ToString();
            string panelNo = node.ToString();
            string dataString = "";
            switch (sendType)
            {
                case TakSendType.TAKSendRequestActEvents:
                    NotifyClient("Request Active Events RX");
                    //_messageSent = true;
                    //data = CreateBasicMessage(8, TakCommands.CMD_REQUEST_ACTIVE_EVENTS);


                    data = new string[8];
                    for (int i = 0; i < 8 - 1; i++)
                        data[i] = "0";
                    data[3] = 8.ToString();
                    data[7] = 79.ToString();

                    rxTx = TransmissionType.RX;
                    immediateRxSend = true;
                    if (_reconnect)
                        immediateRxSend = true;
                    break;

                case TakSendType.TAKSendRequestActEventsTX:
                    NotifyClient("Request Active Events TX");
                    //_messageSent = true;
                    //data = CreateBasicMessage(8, TakCommands.CMD_REQUEST_ACTIVE_EVENTS);

                    data = new string[8];
                    for (int i = 0; i < 8 - 1; i++)
                        data[i] = "0";
                    data[3] = 8.ToString();
                    data[7] = 79.ToString();

                    dataString = string.Join(",", data);
                    Console.WriteLine("Request Active Events TX: " + dataString);
                    immediateTxSend = true;
                    rxTx = TransmissionType.TX;
                    if (_reconnect)
                        immediateTxSend = true;
                    break;

                case TakSendType.TAKSendRequestEventLog:
                    //_requestEventLogSent = true;
                    NotifyClient(_reconnect ? "Send Request Event Log - Immediate" : "Send Request Event Log");
                    _messageSent = true;
                    data = CreateMessageWithSerialNo(12, TakCommands.CMD_REQUEST_EVENT_LOG, serialNoStr);
                    rxTx = TransmissionType.RX;
                    HandleReconnect();
                    break;

                case TakSendType.TAKSendRequestEventLogEx:
                    NotifyClient(_reconnect ? "Send Request Event Log EX - Immediate" : "Send Request Event Log EX");
                    immediateRxSend = true;
                    //_messageSent = true;
                    _requestEventLogEXSent = true;
                    data = CreateMessageWithSerialNo(12, TakCommands.CMD_REQUEST_EVENT_LOG, serialNoStr);
                    rxTx = TransmissionType.RX;
                    HandleReconnect();
                    break;

                case TakSendType.TAKSendEventACKRX:
                    NotifyClient("Send Event ACK RX");
                    immediateRxSend = true;
                    //_messageSent = true;
                    data = CreateMessageWithSerialNo(12, TakCommands.CMD_EVENT_ACK, serialNoStr);
                    dataString = string.Join(",", data);
                    Console.WriteLine("Send Event ACK RX: " + dataString);
                    rxTx = TransmissionType.RX;
                    break;

                case TakSendType.TAKSendEventACKTX:
                    NotifyClient("Send Event ACK TX");
                    immediateTxSend = true;
                    //_messageSent = true;
                    data = CreateMessageWithSerialNo(12, TakCommands.CMD_EVENT_ACK, serialNoStr);
                    dataString = string.Join(",", data);
                    Console.WriteLine("Send Event ACK TX: " + dataString);
                    rxTx = TransmissionType.TX;
                    break;

                case TakSendType.TAKSendACK:
                    NotifyClient("Send ACK Immediate");
                    //_messageSent = true;
                    immediateRxSend = true;
                    data = CreateBasicMessage(8, TakCommands.CMD_ACK);
                    dataString = string.Join(",", data);
                    Console.WriteLine("Send ACK Immediate: " + dataString);
                    rxTx = TransmissionType.RX;
                    break;

                case TakSendType.TAKSendNACK:
                    NotifyClient("Send NACK");
                    //_messageSent = true;
                    data = CreateBasicMessage(8, TakCommands.CMD_NACK);
                    rxTx = TransmissionType.RX;
                    break;

                case TakSendType.TAKSendStartConnectionMonitoringRX:
                    _connectionMonitoringSent = true;
                    NotifyClient(_reconnect ? "Send Start Connection Immediate RX" : "Send Start Connection RX");
                    if (_reconnect)
                        immediateRxSend = true;
                    data = CreateConnectionMessage(clientIDStr, TakCommands.CMD_START_MONITORING);
                    rxTx = TransmissionType.RX;
                    break;

                case TakSendType.TAKSendStartConnectionMonitoringTX:
                    _connectionMonitoringSentTX = true;
                    NotifyClient((_txRestart ? "Send Start Connection Immediate TX: " : "Send Start Connection TX: ") +
                        _connectionMonitoringSentTX);
                    if (_txRestart)
                        immediateTxSend = true;
                    data = CreateConnectionMessage(clientIDStr, TakCommands.CMD_START_MONITORING);
                    rxTx = TransmissionType.TX;
                    break;

                case TakSendType.TAKSendStopConnectionMonitoringTX:
                    _connectionMonitoringSent = false;
                    _connectionStopMonitoringSentTX = true;
                    NotifyClient("Send Stop Connection TX");
                    data = CreateConnectionMessage(clientIDStr, TakCommands.CMD_STOP_MONITORING);
                    rxTx = TransmissionType.TX;
                    break;

                case TakSendType.TAKSendStopConnectionMonitoringRX:
                    _connectionMonitoringSent = false;
                    _connectionStopMonitoringSentRX = true;
                    NotifyClient("Send Stop Connection RX");
                    data = CreateConnectionMessage(clientIDStr, TakCommands.CMD_STOP_MONITORING);
                    rxTx = TransmissionType.RX;
                    break;

                case TakSendType.TAKSendHeartBeatTX:
                    immediateTxSend = true;
                    NotifyClient($"Send Heartbeat TX {immediateTxSend}");
                    _heartBeatCount++;
                    _numHeartbeats++;
                    _heartBeatSent = true;
                    data = CreateHeartbeatMessage(clientIDStr);
                    rxTx = TransmissionType.TX;
                    break;

                case TakSendType.TAKSendHeartBeatRX:
                    NotifyClient("Send Heartbeat RX");
                    _heartBeatCount++;
                    _numHeartbeats++;
                    _heartBeatSent = true;
                    data = CreateHeartbeatMessage(clientIDStr);
                    rxTx = TransmissionType.RX;
                    break;

                case TakSendType.TAKSendQueryANALDetails:
                    NotifyClient($"TAKSendQueryANALDetails - Node:{node} Loop:{loop} Addr:{address} SubAddr:{subAddress}");
                    _messageSent = true;
                    data = CreateDeviceQueryMessage(node, loop, address, subAddress, TakCommands.CMD_QUERY_ANAL_DETAILS);
                    break;

                case TakSendType.TAKSendControlEnableDevice:
                    NotifyClient("Enable Device");
                    _messageSent = true;
                    data = CreateDeviceQueryMessage(node, loop, address, subAddress, TakCommands.CMD_ENABLE_DEVICE);
                    break;

                case TakSendType.TAKSendControlDisableDevice:
                    NotifyClient("Disable Device");
                    _messageSent = true;
                    data = CreateDeviceQueryMessage(node, loop, address, subAddress, TakCommands.CMD_DISABLE_DEVICE);
                    break;

                case TakSendType.TAKSendControlEnableZone:
                    NotifyClient("Enable Zone");
                    _messageSent = true;
                    data = CreateZoneMessage(zone, TakCommands.CMD_ENABLE_ZONE);
                    break;

                case TakSendType.TAKSendControlDisableZone:
                    NotifyClient("Disable Zone");
                    _messageSent = true;
                    data = CreateDisableZoneMessage(zone, subAddress);
                    break;

                case TakSendType.TAKSendControlReset:
                    NotifyClient(_forceReset ? $"Reset Immediate {node}:{panelNo}" : $"Reset {node}:{panelNo}");
                    _messageSent = true;
                    if (_forceReset)
                        immediateTxSend = true;
                    data = CreatePanelControlMessage(panelNo, TakCommands.CMD_RESET);
                    break;

                case TakSendType.TAKSendControlSilence:
                    NotifyClient("Silence");
                    _messageSent = true;
                    data = CreatePanelControlMessage(node.ToString(), TakCommands.CMD_SILENCE);
                    break;

                case TakSendType.TAKSendControlStartAlert:
                    NotifyClient("Start Alert");
                    _messageSent = true;
                    data = CreatePanelControlMessage(node.ToString(), TakCommands.CMD_START_ALERT);
                    break;

                case TakSendType.TAKSendControlStartEVAC:
                    NotifyClient("Start Evac");
                    _messageSent = true;
                    data = CreatePanelControlMessage(node.ToString(), TakCommands.CMD_START_EVAC);
                    break;

                case TakSendType.TAKSendSetTime:
                    NotifyClient("Set Time");
                    _messageSent = true;
                    data = CreateSetTimeMessage();
                    break;

                case TakSendType.TAKSendControlPIOOutput:
                    NotifyClient("PIO Output");
                    _messageSent = true;
                    data = CreateExtendedControlMessage(onOff, DeviceTypes.PIO_OUTPUT, loop, address, subAddress, zone);
                    break;

                case TakSendType.TAKSendControlIOChannel:
                    NotifyClient("IO Channel");
                    _messageSent = true;
                    data = CreateExtendedControlMessage(onOff, DeviceTypes.IO_CHANNEL, loop, address, subAddress, zone);
                    break;

                case TakSendType.TAKSendControlSubAddress:
                    NotifyClient("Sub Address");
                    _messageSent = true;
                    data = CreateExtendedControlMessage(onOff, DeviceTypes.SUB_ADDRESS, loop, address, subAddress, zone);
                    break;

                case TakSendType.TAKSendControlSounder:
                    NotifyClient("Sounder");
                    _messageSent = true;
                    data = CreateExtendedControlMessage(onOff, DeviceTypes.SOUNDER, loop, address, subAddress, zone);
                    break;

                case TakSendType.TAKSendControlZone:
                    NotifyClient("Control Zone");
                    _messageSent = true;
                    data = CreateExtendedControlMessage(onOff, DeviceTypes.ZONE, loop, address, subAddress, zone);
                    break;

                case TakSendType.TAKSendControlSilenceBuzzer:
                    NotifyClient("Silence Buzzer");
                    _messageSent = true;
                    data = CreatePanelControlMessage(node.ToString(), TakCommands.CMD_SILENCE_BUZZER);
                    break;

                case TakSendType.TAKSendControlResound:
                    NotifyClient("Resound");
                    _messageSent = true;
                    data = CreatePanelControlMessage(node.ToString(), TakCommands.CMD_RESOUND);
                    break;

                case TakSendType.TAKSendControlStartTest:
                    NotifyClient("Start Test");
                    _messageSent = true;
                    data = CreatePanelControlMessage(node.ToString(), TakCommands.CMD_START_TEST);
                    break;

                default:
                    NotifyClient($"Unhandled send type: {sendType}");
                    break;
            }

            return data;
        }

        private string[] convertserialnumber(long[] serialNo)
        {
            if (serialNo == null || serialNo.Length < 4)
                return new string[4];

            return new string[]
            {
                serialNo[0].ToString(),
                serialNo[1].ToString(),
                serialNo[2].ToString(),
                serialNo[3].ToString()
            };
        }

        private string[] CreateBasicMessage(int length, string command)
        {
            var data = new string[length];
            for (int i = 0; i < length - 1; i++)
                data[i] = "0";
            data[3] = length.ToString();
            data[length - 5] = command;
            return data;
        }

        private string[] CreateBasicMessageOLD(int length, string command)
        {
            var data = new string[length];
            for (int i = 0; i < length - 1; i++)
                data[i] = "0";
            data[3] = length.ToString();
            data[length - 1] = command;
            return data;
        }

        private string[] CreateMessageWithSerialNo(int length, string command, string[] serialNo)
        {
            var data = CreateBasicMessage(length, command);
            if (serialNo != null && serialNo.Length >= 4)
            {
                data[8] = serialNo[0];
                data[9] = serialNo[1];
                data[10] = serialNo[2];
                data[11] = serialNo[3];
            }
            return data;
        }

        private string[] CreateConnectionMessage(string clientID, string command)
        {
            var data = new string[12];
            for (int i = 0; i < 11; i++)
                data[i] = "0";
            data[3] = "12";
            data[7] = command;
            data[11] = clientID;
            return data;
        }

        private string[] CreateHeartbeatMessage(string clientID)
        {
            var data = new string[12];
            for (int i = 0; i < 11; i++)
                data[i] = "0";
            data[3] = "12";
            data[7] = TakCommands.CMD_HEARTBEAT;
            data[11] = clientID;
            return data;
        }

        private string[] CreateDeviceQueryMessage(int node, int loop, int address, int subAddress, string command)
        {
            var data = new string[24];
            for (int i = 0; i < 24; i++)
                data[i] = "0";
            data[3] = "24";
            data[7] = command;
            data[11] = node.ToString();
            data[15] = loop.ToString();
            data[19] = address.ToString();
            data[23] = subAddress.ToString();
            return data;
        }

        private string[] CreateZoneMessage(int zone, string command)
        {
            var data = new string[12];
            for (int i = 0; i < 11; i++)
                data[i] = "0";
            data[3] = "12";
            data[7] = command;
            data[11] = zone.ToString();
            return data;
        }

        private string[] CreateDisableZoneMessage(int zone, int subAddress)
        {
            var data = new string[16];
            for (int i = 0; i < 15; i++)
                data[i] = "0";
            data[3] = "16";
            data[7] = TakCommands.CMD_DISABLE_ZONE;
            data[11] = zone.ToString();
            data[15] = subAddress.ToString();
            return data;
        }

        private string[] CreatePanelControlMessage(string panelNo, string command)
        {
            var data = new string[12];
            for (int i = 0; i < 11; i++)
                data[i] = "0";
            data[3] = "12";
            data[7] = command;
            data[11] = panelNo;
            return data;
        }

        private string[] CreateSetTimeMessage()
        {
            var data = new string[12];
            for (int i = 0; i < 8; i++)
                data[i] = "0";
            data[3] = "12";
            data[7] = TakCommands.CMD_SET_TIME;

            if (_timeArray != null && _timeArray.Length >= 4)
            {
                for (int i = 0; i < 4; i++)
                {
                    data[8 + i] = _timeArray[i].Length < 2 ? "0" + _timeArray[i] : _timeArray[i];
                    NotifyClient($"Set Time{i}: {data[8 + i]}");
                }
            }
            return data;
        }

        private string[] CreateExtendedControlMessage(bool onOff, string deviceType,
            int loop, int address, int subAddress, int zone)
        {
            var data = new string[32];
            for (int i = 0; i < 32; i++)
                data[i] = "0";

            data[3] = "32";
            data[7] = TakCommands.CMD_CONTROL;
            data[11] = onOff ? "1" : "0";
            data[15] = deviceType;
            data[19] = loop.ToString();
            data[23] = address.ToString();
            data[27] = subAddress.ToString();
            data[31] = zone.ToString();

            return data;
        }

        protected virtual void OnSendImmediateRequest(SendImmediateEventArgs e)
        {
            SendImmediateRequest?.Invoke(this, e);
        }

        private void HandleReconnect()
        {
            if (_reconnect)
            {
                _reconnect = false;
                NotifyClient("Reconnect Set To False");

                if (_txQueue.Count > 0)
                {
                    NotifyClient("Turn Send timer on");
                    OnStartTxTimer(new TimerEventArgs { Interval =  1000});
                }

                if (_rxQueue.Count > 0)
                {
                    NotifyClient("Turn Send timer RX on");
                    OnStartRxTimer(new TimerEventArgs { Interval =  1000});
                }
            }
        }

        private void QueueMessage(string[] data, TransmissionType rxTx, bool stopSending)
        {
            if (rxTx == TransmissionType.RX)
            {
                var rxControl = new TakControlRX
                {
                    EncodedPacketRX = data,
                    RXTX = rxTx
                };

                if (_rxQueue.Count >= 0)
                {
                    _rxQueue.Enqueue(rxControl);
                    NotifyClient($"Add to RX Queue - Count: {_rxQueue.Count}");
                    OnStartRxTimer(new TimerEventArgs { Interval =  1000});
                }
                else
                {
                    if (_commError)
                    {
                        ClearAllQueues();
                        NotifyClient("Comm Error - Queues cleared");
                    }
                    else
                    {
                        _rxQueue.Enqueue(rxControl);
                        NotifyClient($"Add to RX Queue (multiple) - Count: {_rxQueue.Count}");
                        OnStartRxTimer(new TimerEventArgs { Interval =  1000});
                    }
                }
            }
            else // TX
            {
                var txControl = new TakControl
                {
                    EncodedPacket = data,
                    RXTX = rxTx
                };

                if (_txQueue.Count == 0)
                {
                    _txQueue.Enqueue(txControl);
                    NotifyClient($"Add to TX Queue - Count: {_txQueue.Count}");
                    OnStartTxTimer(new TimerEventArgs { Interval =  1000});
                }
                else
                {
                    if (_commError)
                    {
                        ClearAllQueues();
                        NotifyClient("Comm Error - Queues cleared");
                    }
                    else
                    {
                        _txQueue.Enqueue(txControl);
                        NotifyClient($"Add to TX Queue (multiple) - Count: {_txQueue.Count}");
                        OnStartTxTimer(new TimerEventArgs { Interval =  1000});
                    }
                }
            }
        }

        private void ClearAllQueues()
        {
            _rxQueue.Clear();
            _txQueue.Clear();
        }

        protected virtual void OnStartTxTimer(TimerEventArgs e)
        {
            StartTxTimer?.Invoke(this, e);
        }

        protected virtual void OnStartRxTimer(TimerEventArgs e)
        {
            StartRxTimer?.Invoke(this, e);
        }

        private void TxTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (_txTimer != null)
            {
               // _txTimer.Stop();
            }

            var message = DequeueNextTxMessage();
            if (message != null)
            {
                Console.WriteLine($"[TX TIMER] Sending queued message: {string.Join(",", message.EncodedPacket)}");
                // Implement actual network send here
                SendBytesToPanel(message.EncodedPacket);
                // If more messages in queue, restart timer
                if (TxQueueCount > 0)
                {
                  //  _txTimer.Start();
                }
            }
        }

        private void RxTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            //_rxTimer.Stop();

            var message = DequeueNextRxMessage();
            if (message != null)
            {
                Console.WriteLine($"[RX TIMER] Sending queued message: {string.Join(",", message.EncodedPacket)}");
                // Implement actual network send here
                SendBytesToPanel(message.EncodedPacket);

                // If more messages in queue, restart timer
                //if (RxQueueCount > 0)
                {
                    //_rxTimer.Start();
                }
            }
        }

        public TakControl DequeueNextTxMessage()
        {
            return _txQueue.Count > 0 ? _txQueue.Dequeue() : null;
        }

        public TakControlRX DequeueNextRxMessage()
        {
            return _rxQueue.Count > 0 ? _rxQueue.Dequeue() : null;
        }

        private void SendBytesToPanel(string[] tosend)
        {
            if (string.IsNullOrWhiteSpace(gsIPAddress) || string.IsNullOrWhiteSpace(gsIPPort))
            {
                NotifyClient("IP address or port is not set.");
                return;
            }

            if (!int.TryParse(gsIPPort, out int port))
            {
                NotifyClient($"Invalid port: {gsIPPort}");
                return;
            }

            byte[] data = convertstringarraytobytearray(tosend);
            try
            {
                EnsureConnected();

                using (var stream = client.GetStream())
                {
                    // Send data
                    stream.Write(data, 0, data.Length);
                    stream.Flush();

                    string logFilePath = @"C:\temp\c#amxlog.txt";
                    string hexString = BitConverter.ToString(data);
                    string decimalString = string.Join(" ", data);
                    File.AppendAllText(logFilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} Sent: {decimalString}{Environment.NewLine}");

                    Thread.Sleep(1000); // wait for response

                    while (stream.DataAvailable)
                    {
                        // Read response
                        var responseBuffer = new byte[1024];
                        int bytesRead = stream.Read(responseBuffer, 0, responseBuffer.Length);
                        if (bytesRead > 0)
                        {
                            // Convert response to hex string for readability
                            string responseHex = BitConverter.ToString(responseBuffer, 0, bytesRead);
                            NotifyClient($"Received response ({bytesRead} bytes): {responseHex}");
                            Console.WriteLine("Subsequent Received response: " + responseHex);

                            DecodeMessage(responseHex);

                            //if (responseHex.Length > 23)  // don't ACK an ACK
                            //{
                            //    sendtotaktis(TakSendType.TAKSendEventACKRX, glSerialNo, clientID: 1);
                            //}

                            // ACK 
                            tosend = new string[12];
                            for (int i = 0; i < 12; i++)
                                tosend[i] = "0";
                            tosend[3] = 12.ToString();
                            tosend[7] = 134.ToString();
                            tosend[8] = glSerialNo[0].ToString();
                            tosend[9] = glSerialNo[1].ToString();
                            tosend[10] = glSerialNo[2].ToString();
                            tosend[11] = glSerialNo[3].ToString();

                            data = convertstringarraytobytearray(tosend);

                            stream.Write(data, 0, data.Length);
                            stream.Flush();

                            logFilePath = @"C:\temp\c#amxlog.txt";
                            hexString = BitConverter.ToString(data);
                            decimalString = string.Join(" ", data);
                            File.AppendAllText(logFilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} Sent: {decimalString}{Environment.NewLine}");

                            Thread.Sleep(100); // wait for response
                        }
                        else
                        {
                            NotifyClient("No response received from panel.");
                        }
                    }
                }

                NotifyClient($"Sent {data.Length} bytes to {gsIPAddress}:{port}");
            }
            catch (Exception ex)
            {
                NotifyClient($"Error sending bytes: {ex.Message}");
            }
        }

        private void EnsureConnected()
        {
            if (client == null || !client.Connected)
            {
                client?.Close();
                client = new TcpClient();
                client.Connect(gsIPAddress, int.Parse(gsIPPort));
            }
        }



        private void StartListening()
        {
            NetworkStream stream = client.GetStream();
            while (true)
            {
                try
                {
                    if (client == null || !client.Connected)
                    {
                        EnsureConnected();
                        Thread.Sleep(100); // wait before retrying
                        continue;
                    }
                    if (stream.DataAvailable)
                    {
                        var buffer = new byte[1024];
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 8)
                        {
                            string responseHex = BitConverter.ToString(buffer, 0, bytesRead);
                            NotifyClient($"Received response ({bytesRead} bytes): {responseHex}");
                            DecodeMessage(responseHex);
                        }
                    }
                    Thread.Sleep(1000);
                }
                catch (ObjectDisposedException)
                {
                    NotifyClient("NetworkStream has been disposed, reconnecting...");
                }
                catch (Exception ex)
                {
                    NotifyClient($"Unexpected error: {ex.Message}");
                }
            }
        }
 
        private void DecodeMessage(string responseHex)
        {
            string sLocationText = "";
            string sPanelText = "";
            string sZoneText = "";
            string sEventGroup = "";
            string sEventType = "";
            string sEventCode = "";
            string sNode = "";
            string sAddressType = "";
            string sAddress = "";
            string sLoop = "";
            string sZone = "";
            string sSubAddress = "";
            string sInputAction = "";
            string sMessageType = "";
            string sTimeStamp = "";
            int iCharCount = 0;
            string[] hexStrings = responseHex.Split('-');
            byte[] aryHexMessage = hexStrings.Select(h => Convert.ToByte(h, 16)).ToArray();

            if (aryHexMessage.Length > 8)
            {
                // Skip the first 8 elements and create a new array
                if (hexStrings[0] == "00" & hexStrings[1] == "00" & hexStrings[2] == "00" & hexStrings[3] == "08" & hexStrings[4] == "00" & hexStrings[5] == "00" & hexStrings[6] == "00" & hexStrings[7] == "01")
                {
                    aryHexMessage = aryHexMessage.Skip(8).ToArray();
                }

                while (iCharCount < aryHexMessage.Length)
                {
                    switch (iCharCount)
                    {
                        case 4:
                        case 5:
                        case 6:
                        case 7:
                            sMessageType += aryHexMessage[iCharCount].ToString("X2"); // format as 2-digit hex
                            break;

                        case 8:

                            glSerialNo[0] = aryHexMessage[iCharCount];
                            break;

                        case 9:

                            glSerialNo[1] = aryHexMessage[iCharCount];
                            break;

                        case 10:

                            glSerialNo[2] = aryHexMessage[iCharCount];
                            break;

                        case 11:

                            glSerialNo[3] = aryHexMessage[iCharCount];
                            break;

                        case 12:
                        case 13:
                        case 14:
                        case 15:

                            sEventGroup += aryHexMessage[iCharCount].ToString();
                            break;

                        case 16:
                        case 17:
                        case 18:
                        case 19:

                            sEventType += aryHexMessage[iCharCount].ToString();
                            break;

                        case 20:
                        case 21:
                        case 22:
                        case 23:
                            sEventCode += aryHexMessage[iCharCount].ToString();
                            break;

                        case 24:
                        case 25:
                        case 26:
                        case 27:

                            sNode += aryHexMessage[iCharCount].ToString();
                            break;

                        case 28:
                        case 29:
                        case 30:
                        case 31:

                            sAddressType += aryHexMessage[iCharCount].ToString();
                            break;

                        case 32:
                        case 33:
                        case 34:
                        case 35:
                            sAddress += aryHexMessage[iCharCount].ToString();
                            break;

                        case 36:
                        case 37:
                        case 38:
                        case 39:
                            sSubAddress += aryHexMessage[iCharCount].ToString();
                            break;

                        case 40:
                        case 41:
                        case 42:
                        case 43:

                            sLoop += aryHexMessage[iCharCount].ToString();
                            break;

                        case 44:
                        case 45:
                        case 46:
                        case 47:

                            sZone += aryHexMessage[iCharCount].ToString();
                            break;

                        case 48:
                        case 49:
                        case 50:
                        case 51:

                            sInputAction += hexStrings[iCharCount].ToString();
                            break;

                        case 52:
                        case 53:
                        case 54:
                        case 55:
                            // If you want the timestamp as a string of numbers
                            sTimeStamp += aryHexMessage[iCharCount]; // pad with 0 if needed
                            break;

                        case int n when (n >= 56 && n <= 135):
                            if (aryHexMessage[iCharCount] != 0)
                                sLocationText += Convert.ToChar(aryHexMessage[iCharCount]);
                            break;

                        case int n when (n >= 136 && n <= 167):
                            if (aryHexMessage[iCharCount] != 0)
                                sPanelText += Convert.ToChar(aryHexMessage[iCharCount]);
                            break;

                        case int n when (n >= 168 && n <= 248):
                            if (aryHexMessage[iCharCount] != 0)
                                sZoneText += Convert.ToChar(aryHexMessage[iCharCount]);
                            break;
                    }
                    iCharCount++;
                }
                // Replace all '0' with spaces, trim, then convert spaces back to '0'
                sMessageType = sMessageType.Replace('0', ' ').Trim().Replace(' ', '0');

                // If the result is empty or null, set to "0"
                if (string.IsNullOrEmpty(sMessageType))
                {
                    sMessageType = "0";
                }
                long iMessageType = Convert.ToInt64(sMessageType, 16);

                if (sEventGroup == "")
                {
                    sEventGroup = "0";
                }

                if (sEventCode == "")
                {
                    sEventCode = "0";
                }

                if (sEventType == "")
                {
                    sEventType = "0";
                }

                if (sNode == "")
                {
                    sNode = "0";
                }

                if (sAddress == "")
                {
                    sAddress = "0";
                }

                if (sSubAddress == "")
                {
                    sSubAddress = "0";
                }

                if (sAddressType == "")
                {
                    sAddressType = "0";
                }

                if (sZone == "")
                {
                    sZone = "0";
                }

                if (sLoop == "")
                {
                    sLoop = "0";
                }

                if (sInputAction == "")
                {
                    sInputAction = "0";
                }

                if (sTimeStamp == "")
                {
                    sTimeStamp = "0";
                }

                int iNode = Convert.ToInt32(sNode);
                int iAddress = Convert.ToInt32(sAddress);
                long lEventCode = Convert.ToInt64(sEventCode);
                long lEventGroup = Convert.ToInt64(sEventGroup);
                long lEventType = Convert.ToInt64(sEventType);
                int iAddressType = Convert.ToInt32(sAddressType);
                int iSubAddress = Convert.ToInt32(sSubAddress);
                int iLoop = Convert.ToInt32(sLoop);
                int iZone = Convert.ToInt32(sZone);
                int iInputAction = Convert.ToInt32(sInputAction);
                long lTimeStamp = Convert.ToInt64(sTimeStamp);
                enmTAKMessageType gMessageType = (enmTAKMessageType)iMessageType;
                enmTAKEventType gEventType = (enmTAKEventType)lEventType;
                enmTAKEventCode gEventCode = (enmTAKEventCode)lEventCode;

                switch (iMessageType)
                {
                    case 0:    // NAK
                        break;
                    case 1:    // ACK
                        break;
                    case 2:    // Packet Type Event ID

        //                sendtotaktis(TakSendType.TAKSendStartConnectionMonitoringTX, glSerialNo, clientID: 1);

                        break;
                    case 133:  // Start Event Message

                        ParseTAKMessage(gMessageType, glSerialNo, lEventGroup, gEventType, gEventCode, iNode, iAddressType, iAddress, iSubAddress, iLoop, iZone, iInputAction, lTimeStamp, sLocationText, sPanelText, sZoneText, "", true);

                        break;
                }
            }
        }

        private void send_response_amx(int evnum, string message1, string message2, string message3 = "")
        {
            string friendlymessage = message2 + (message3.Length > 0 ? (" " + message3) : "");

            // Signal the event back to the main service, so that it can be logged
            this.NotifyClient(friendlymessage, false);

            CSAMXSingleton.CS.SendAlarmToAMX(evnum, message1, message2, message3);
            CSAMXSingleton.CS.FlushMessages();
        }
        private byte[] convertstringarraytobytearray(string[] stringArray)
        {
            List<byte> ret = new List<byte>();
            foreach (string str in stringArray)
            {
                if (str == null) break; // bail if we get a null;
                byte b = Convert.ToByte(str);
                ret.Add(b);
            }
            return ret.ToArray();
        }

        #endregion

        public class TcpClientWithEvents : IDisposable
        {
            private TcpClient _client;
            private NetworkStream _stream;
            private CancellationTokenSource _cts;

            /// <summary>Triggered whenever new data arrives from the panel.</summary>
            public event EventHandler<byte[]> DataReceived;

            /// <summary>Triggered when connection is closed or an error occurs.</summary>
            public event EventHandler<string> ConnectionClosed;

            public bool IsConnected => _client?.Connected ?? false;

            public TcpClientWithEvents()
            {
                _client = new TcpClient();
            }

            /// <summary>Connects asynchronously to the panel.</summary>
            public async Task ConnectAsync(string host, int port)
            {
                _cts = new CancellationTokenSource();

                await _client.ConnectAsync(host, port);
                _stream = _client.GetStream();

                // Start background listener
                _ = Task.Run(() => ListenAsync(_cts.Token));
            }

            /// <summary>Sends data asynchronously to the panel.</summary>
            public async Task SendAsync(byte[] data)
            {
                if (_stream == null) throw new InvalidOperationException("Not connected.");
                await _stream.WriteAsync(data, 0, data.Length);
                await _stream.FlushAsync();
            }

            /// <summary>Continuously listens for incoming data in the background.</summary>
            private async Task ListenAsync(CancellationToken token)
            {
                var buffer = new byte[1024];

                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, token);
                        if (bytesRead == 0)
                        {
                            ConnectionClosed?.Invoke(this, "Connection closed by remote host");
                            break;
                        }

                        var data = new byte[bytesRead];
                        Array.Copy(buffer, data, bytesRead);

                        DataReceived?.Invoke(this, data);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when Dispose() is called
                }
                catch (Exception ex)
                {
                    ConnectionClosed?.Invoke(this, $"TCP read error: {ex.Message}");
                }
            }

            public void Dispose()
            {
                _cts?.Cancel();
                _stream?.Dispose();
                _client?.Close();
            }
        }
    }
}

