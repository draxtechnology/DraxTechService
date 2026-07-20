using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DraxTechnology.Panels
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

        // ICD AddrType enum values used in PACKET_TYPE_CONTROL (9.1.37).
        // Spec: addr_type_sub_address(0), addr_type_zone(4), addr_type_pio_input(6),
        // addr_type_pio_output(7), addr_type_io_channel(8), addr_type_sounder(12).
        public static class DeviceTypes
        {
            public const string SUB_ADDRESS = "0";
            public const string ZONE = "4";
            public const string PIO_OUTPUT = "7";
            public const string IO_CHANNEL = "8";
            public const string SOUNDER = "12";
        }
        #endregion

        #region private fields
        // _messageSent / _reconnect / _forceReset are still read by
        // buildmessage() to gate a few logging branches; the legacy
        // queue/timer indirection that originally drove them is gone.
        private bool _messageSent = false;
        private bool _reconnect = false;
        private bool _forceReset = false;

        private int _heartBeatCount = 0;
        private long _numHeartbeats = 0;

        // Write-only status flags for the connection-monitoring / heartbeat state
        // machine. The write side is in place (connection handling sets them), but
        // the read side that acts on them is still TODO — so they read as "assigned
        // but never used". Suppress CS0414 until the read side is wired up.
#pragma warning disable CS0414
        private bool _heartBeatSent = false;

        private bool _connectionMonitoringSent = false;
        private bool _connectionMonitoringSentTX = true;
        private bool _connectionStopMonitoringSentRX = false;
        private bool _connectionStopMonitoringSentTX = false;
        private bool _requestEventLogEXSent = false;
#pragma warning restore CS0414

        // Two TCP connections, per ICD section 6.2 / sample sequences:
        // - RX streams the event log (REQUEST_EVENT_LOG_EX -> EVENT_START/CLEAR
        //   -> EVENT_ACK). Spec is explicit: this connection cannot be used for
        //   anything else, not even connection monitoring.
        // - TX carries START_CONNECTION_MONITORING + the 20s heartbeat plus all
        //   control commands (Reset/Silence/Evac/Disable/etc).
        // Each channel owns its own socket, write lock, assembly buffer and
        // reader Task. WriteFrame routes by TransmissionType from buildmessage.
        private sealed class Channel
        {
            public readonly string Name;
            public TcpClient Client;
            public NetworkStream Stream;
            public readonly object Lock = new object();
            public readonly List<byte> Assembly = new List<byte>();
            // Send queue + ACK gate: enforces ICD §6.4 ("client needs to wait
            // for this response before sending the next query/command"). Only
            // the TX channel uses GateOnSend = true, and per-frame Gate=false
            // exempts EVENT_ACKs on either channel - they don't get their own
            // reply, so gating on one would stall the pump for the full ACK
            // timeout per acked event.
            public readonly Queue<(byte[] Data, bool Gate)> SendQueue = new Queue<(byte[], bool)>();
            public readonly object QueueLock = new object();
            public readonly System.Threading.ManualResetEventSlim AckGate
                = new System.Threading.ManualResetEventSlim(true);
            public bool GateOnSend;
            // ICD §6.5: panel drops the connection after 5 NACKs. We log a
            // warning when we see 4 in a row to make the impending drop
            // visible; the count resets on (re)connect.
            public int NackCount;
            public Channel(string name, bool gateOnSend)
            {
                Name = name;
                GateOnSend = gateOnSend;
            }
        }
        private readonly Channel _txCh = new Channel("TX", gateOnSend: true);
        private readonly Channel _rxCh = new Channel("RX", gateOnSend: false);
        #endregion

        #region public properties
        public long[] glSerialNo = new long[4];
        #endregion
        public override string FakeString => throw new NotImplementedException();
        public override string PanelVersion => "1.0.0.0";
        public override int NumHeartbeats => (int)_numHeartbeats;

        // AMX dispatches these as CSV "node,loop,zone,device". Each override
        // parses the CSV via the base helper and routes to the matching
        // TakSendType so buildmessage() emits the correct wire frame.
        public override void Alert(string passedValues)
        {
            ParsePassedValues(passedValues, out int node, out _, out _, out _);
            sendtotaktis(TakSendType.TAKSendControlStartAlert, node: node);
        }

        public override void Reset(string passedValues)
        {
            ParsePassedValues(passedValues, out int node, out _, out _, out _);
            sendtotaktis(TakSendType.TAKSendControlReset, node: node);
        }

        public override void Silence(string passedValues)
        {
            ParsePassedValues(passedValues, out int node, out _, out _, out _);
            sendtotaktis(TakSendType.TAKSendControlSilence, node: node);
        }

        public override void Evacuate(string passedValues)
        {
            ParsePassedValues(passedValues, out int node, out _, out _, out _);
            sendtotaktis(TakSendType.TAKSendControlStartEVAC, node: node);
        }

        public override void EvacuateNetwork(string passedValues)
        {
            // No dedicated network-evac frame on TAK; broadcast EVAC to node 0
            // (the legacy VB convention for "all nodes on this loop").
            sendtotaktis(TakSendType.TAKSendControlStartEVAC, node: 0);
        }

        public override void MuteBuzzers(string passedValues)
        {
            ParsePassedValues(passedValues, out int node, out _, out _, out _);
            sendtotaktis(TakSendType.TAKSendControlSilenceBuzzer, node: node);
        }

        public override void DisableDevice(string passedValues)
        {
            ParsePassedValues(passedValues, out int node, out int loop, out _, out int device);
            sendtotaktis(TakSendType.TAKSendControlDisableDevice, node: node, loop: loop, address: device);
        }

        public override void EnableDevice(string passedValues)
        {
            ParsePassedValues(passedValues, out int node, out int loop, out _, out int device);
            sendtotaktis(TakSendType.TAKSendControlEnableDevice, node: node, loop: loop, address: device);
        }

        public override void DisableZone(string passedValues)
        {
            ParsePassedValues(passedValues, out _, out _, out int zone, out int device);
            sendtotaktis(TakSendType.TAKSendControlDisableZone, zone: zone, subAddress: device);
        }

        public override void EnableZone(string passedValues)
        {
            ParsePassedValues(passedValues, out _, out _, out int zone, out _);
            sendtotaktis(TakSendType.TAKSendControlEnableZone, zone: zone);
        }

        public override void Analogue(string passedvalues)
        {
            ParsePassedValues(passedvalues, out int node, out int loop, out _, out int device);
            sendtotaktis(TakSendType.TAKSendQueryANALDetails, node: node, loop: loop, address: device);
        }
        protected override void heartbeat_timer_callback(object sender)
        {
            base.heartbeat_timer_callback(sender);
            sendtotaktis(TakSendType.TAKSendHeartBeatTX, clientID: _clientID);
        }

        // Populated from Takman.ini in the constructor; no hardcoded fallbacks.
        private string gsIPAddress;
        private string gsIPPort;
        private int _clientID = 1;
        private readonly string _baselogfolder;

        private CancellationTokenSource _readerCts;
        private Task _txReaderTask;
        private Task _rxReaderTask;
        private Task _txPumpTask;
        private Task _rxPumpTask;
        private string _txLogPath;
        // ACK-gate timeout: how long the pump waits for the server's ACK/NACK
        // before giving up on a sent frame and moving to the next. ICD doesn't
        // pin this; 5s matches the heartbeat grace period mentioned in 10.2.1.
        private const int kAckTimeoutMs = 5000;

        public override void StartUp(int fakemode)
        {
            if (fakemode > 0)
            {
                NotifyClient("TAKTIS FakeMode - readers not started");
                return;
            }

            if (string.IsNullOrEmpty(gsIPAddress) || string.IsNullOrEmpty(gsIPPort))
            {
                NotifyClient("TAKTIS StartUp aborted - PanelIPAddress/IPPort missing from Takman.ini");
                return;
            }

            _readerCts?.Cancel();
            _readerCts = new CancellationTokenSource();
            var token = _readerCts.Token;
            // Two readers + two pumps, one pair per channel. Reader owns socket
            // lifecycle; pump drains the channel's send queue and (on TX) waits
            // for ACK/NACK between sends.
            _rxReaderTask = Task.Run(() => ReaderLoop(_rxCh, token));
            _txReaderTask = Task.Run(() => ReaderLoop(_txCh, token));
            _rxPumpTask = Task.Run(() => PumpLoop(_rxCh, token));
            _txPumpTask = Task.Run(() => PumpLoop(_txCh, token));
        }

        private void ConnectChannel(Channel ch)
        {
            NotifyClient($"TAKTIS [{ch.Name}] connecting to {gsIPAddress}:{gsIPPort}");
            var c = new TcpClient();
            c.Connect(gsIPAddress, Convert.ToInt32(gsIPPort));
            lock (ch.Lock)
            {
                ch.Client = c;
                ch.Stream = c.GetStream();
            }
            ch.NackCount = 0;
            ch.AckGate.Set();
            NotifyClient($"TAKTIS [{ch.Name}] connected");
        }

        private void CloseChannel(Channel ch)
        {
            lock (ch.Lock)
            {
                try { ch.Stream?.Close(); } catch { }
                try { ch.Client?.Close(); } catch { }
                ch.Stream = null;
                ch.Client = null;
            }
            // Release the pump if it's blocked waiting for an ACK that will
            // never arrive on a dead channel.
            ch.AckGate.Set();
        }

        // Routes by TransmissionType (set by buildmessage) and enqueues. The
        // channel's PumpLoop will drain the queue, doing the actual socket
        // write and (on TX) waiting for ACK/NACK between sends. Callers never
        // block on the wire - heartbeat/control commands queue and return.
        private void WriteFrame(byte[] data, TransmissionType rxTx, bool gate = true)
        {
            Channel ch = (rxTx == TransmissionType.RX) ? _rxCh : _txCh;
            lock (ch.QueueLock)
            {
                ch.SendQueue.Enqueue((data, gate));
            }
        }

        private bool WriteFrameRaw(Channel ch, byte[] data)
        {
            lock (ch.Lock)
            {
                if (ch.Stream == null || ch.Client == null || !ch.Client.Connected)
                {
                    NotifyClient($"TAKTIS [{ch.Name}] write skipped - not connected");
                    return false;
                }
                try
                {
                    ch.Stream.Write(data, 0, data.Length);
                    ch.Stream.Flush();
                    LogTransport($"{ch.Name}>", data);
                    return true;
                }
                catch (Exception ex)
                {
                    NotifyClient($"TAKTIS [{ch.Name}] write failed: {ex.Message}");
                    try { ch.Stream?.Close(); } catch { }
                    try { ch.Client?.Close(); } catch { }
                    ch.Stream = null;
                    ch.Client = null;
                    return false;
                }
            }
        }

        // Drains a channel's send queue. On TX, waits for the AckGate to be
        // signalled (by the reader on receipt of an ACK/NACK) before sending
        // the next frame. RX bypasses the gate; its writes are EVENT_ACK
        // replies and the one-shot REQUEST_EVENT_LOG_EX, neither of which
        // expects a per-message response.
        private void PumpLoop(Channel ch, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                byte[] data = null;
                bool gate = false;
                lock (ch.QueueLock)
                {
                    if (ch.SendQueue.Count > 0) (data, gate) = ch.SendQueue.Dequeue();
                }
                if (data == null)
                {
                    try { Task.Delay(25, token).Wait(token); } catch { break; }
                    continue;
                }

                bool gateThisFrame = ch.GateOnSend && gate;
                if (gateThisFrame) ch.AckGate.Reset();
                bool sent = WriteFrameRaw(ch, data);
                if (!sent)
                {
                    if (gateThisFrame) ch.AckGate.Set();
                    continue;
                }
                if (gateThisFrame)
                {
                    try
                    {
                        if (!ch.AckGate.Wait(kAckTimeoutMs, token))
                        {
                            NotifyClient($"TAKTIS [{ch.Name}] ACK timeout - resuming pump");
                        }
                    }
                    catch (OperationCanceledException) { break; }
                }
            }
            NotifyClient($"TAKTIS [{ch.Name}] pump stopped");
        }

        private void LogTransport(string direction, byte[] data)
        {
            if (string.IsNullOrEmpty(_txLogPath)) return;
            try
            {
                string decimalString = string.Join(" ", data);
                File.AppendAllText(_txLogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {direction}: {decimalString}{Environment.NewLine}");
            }
            catch { /* logging is best-effort */ }
        }

        // One reader per Channel. Connects, drains frames, hands each off to
        // ProcessFrame (which knows whether to ACK), reconnects on error with
        // a 2s backoff. The start-of-day sequence is channel-specific.
        private void ReaderLoop(Channel ch, CancellationToken token)
        {
            byte[] buffer = new byte[4096];
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (ch.Client == null || !ch.Client.Connected)
                    {
                        ConnectChannel(ch);
                        ch.Assembly.Clear();
                        SendChannelStartOfDay(ch);
                    }

                    int bytesRead = ch.Stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        NotifyClient($"TAKTIS [{ch.Name}] peer closed - reconnecting");
                        CloseChannel(ch);
                        try { Task.Delay(2000, token).Wait(token); } catch { }
                        continue;
                    }

                    for (int i = 0; i < bytesRead; i++) ch.Assembly.Add(buffer[i]);
                    DrainFrames(ch);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested) break;
                    NotifyClient($"TAKTIS [{ch.Name}] reader: {ex.Message}");
                    CloseChannel(ch);
                    try { Task.Delay(2000, token).Wait(token); } catch { break; }
                }
            }
            NotifyClient($"TAKTIS [{ch.Name}] reader stopped");
        }

        private void SendChannelStartOfDay(Channel ch)
        {
            if (ch == _rxCh)
            {
                // RX is the event-log stream. Per ICD: once a connection is
                // streaming the event log it cannot be used for anything else.
                // We don't issue REQUEST_ACTIVE_EVENTS here because EVENT_LOG_EX
                // delivers historic + live events from the given serial number.
                sendtotaktis(TakSendType.TAKSendRequestEventLogEx, glSerialNo);
            }
            else // TX
            {
                // TX carries control + heartbeat. REQUEST_ACTIVE_EVENTS_TX is
                // a one-shot snapshot terminated by EVENT_ID; after that the
                // server may close, but the heartbeat keeps the channel alive.
                sendtotaktis(TakSendType.TAKSendRequestActEventsTX);
                sendtotaktis(TakSendType.TAKSendStartConnectionMonitoringTX, clientID: _clientID);
            }
        }

        // TAK frames are length-prefixed: bytes 0..3 form a big-endian uint32
        // length covering the whole frame. We may get multiple frames in one
        // Read or a frame split across reads; assemble until we have a full
        // frame, hand it off, then loop.
        private void DrainFrames(Channel ch)
        {
            while (ch.Assembly.Count >= 4)
            {
                int frameLen = (ch.Assembly[0] << 24) | (ch.Assembly[1] << 16)
                             | (ch.Assembly[2] << 8) | ch.Assembly[3];
                if (frameLen <= 0 || frameLen > 65535)
                {
                    NotifyClient($"TAKTIS [{ch.Name}] bad frame length {frameLen} - flushing buffer");
                    ch.Assembly.Clear();
                    return;
                }
                if (ch.Assembly.Count < frameLen) return;

                byte[] frame = new byte[frameLen];
                ch.Assembly.CopyTo(0, frame, 0, frameLen);
                ch.Assembly.RemoveRange(0, frameLen);
                ProcessFrame(ch, frame);
            }
        }

        private void ProcessFrame(Channel ch, byte[] frame)
        {
            LogTransport($"{ch.Name}<", frame);

            // Serial number lives at bytes 8..11 for any payload-bearing frame.
            if (frame.Length >= 12)
            {
                glSerialNo[0] = frame[8];
                glSerialNo[1] = frame[9];
                glSerialNo[2] = frame[10];
                glSerialNo[3] = frame[11];
            }

            long mt = frame.Length >= 8 ? ReadFieldU32(frame, 4) : -1;

            // ACK/NACK on TX releases the pump's gate so the next queued frame
            // can be sent. ICD §6.5: 5 NACKs in a row drops the connection -
            // log a warning at 4 so the impending drop is visible.
            if (ch == _txCh)
            {
                if (mt == 1 /* ACK */)
                {
                    ch.NackCount = 0;
                    ch.AckGate.Set();
                }
                else if (mt == 0 /* NACK */)
                {
                    ch.NackCount++;
                    if (ch.NackCount >= 4)
                    {
                        NotifyClient($"TAKTIS [{ch.Name}] NACK #{ch.NackCount} - re-registering connection monitoring (ICD §6.5)");
                        // VB6 TAKSendStopConnectionMonitoringTX then TAKSendStartConnectionMonitoringTX
                        // to re-handshake before the panel drops the connection at 5 NACKs.
                        sendtotaktis(TakSendType.TAKSendStopConnectionMonitoringTX, clientID: _clientID);
                        sendtotaktis(TakSendType.TAKSendStartConnectionMonitoringTX, clientID: _clientID);
                        ch.NackCount = 0;
                    }
                    ch.AckGate.Set();
                }
            }

            string responseHex = BitConverter.ToString(frame);
            DecodeMessage(responseHex);

            // EVENT_START/EVENT_CLEAR must be EVENT_ACKed on the channel they
            // arrived on. The panel streams its active-events snapshot on TX
            // after REQUEST_ACTIVE_EVENTS_TX and waits for each ack; leaving
            // those unacked stalls the handshake - the panel then ignores
            // heartbeats, NACKs controls, and drops the TX connection ~12s
            // after connect (VB6 TAKNetManager.bas:5458 acks both sides).
            if (mt == 133 /* EVENT_START */ || mt == 135 /* EVENT_CLEAR */)
            {
                sendtotaktis(ch == _rxCh
                    ? TakSendType.TAKSendEventACKRX
                    : TakSendType.TAKSendEventACKTX, glSerialNo);
            }
        }

        byte[] convertstringarraytobytearray(string[] stringArray)
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









        public PanelTaktis(string baselogfolder, string identifier) : base(baselogfolder, identifier, "TAKMan", "TAK")
        {
            _baselogfolder = baselogfolder;
            if (string.IsNullOrEmpty(identifier)) return;

            try
            {
                gsIPAddress = base.GetSetting<string>(ksettingsetupsection, "PanelIPAddress");
                int port = base.GetSetting<int>(ksettingsetupsection, "IPPort");
                if (port > 0) gsIPPort = port.ToString();

                int clientId = base.GetSetting<int>(ksettingsetupsection, "ClientID");
                if (clientId > 0) _clientID = clientId;

                _amx1Offset = base.GetSetting<int>(ksettingsetupsection, "giAmx1Offset");
                this.Offset = _amx1Offset;
                InitAnalogueStore();

                _ulSettings = base.GetSetting<int>(ksettingsetupsection, "ULSettings") != 0;
                _ioModuleSettings = base.GetSetting<string>(ksettingsetupsection, "IOModuleSettings") ?? "";
                _ioModuleSettingsPanels = base.GetSetting<string>(ksettingsetupsection, "IOModuleSettingsPanels") ?? "";

                if (!string.IsNullOrEmpty(_baselogfolder))
                {
                    _txLogPath = Path.Combine(_baselogfolder, "TAKTIS_transport.log");
                }

                NotifyClient($"PanelTaktis: {gsIPAddress}:{gsIPPort} clientID={_clientID} offset={_amx1Offset}");
            }
            catch (Exception ex)
            {
                NotifyClient($"PanelTaktis settings load failed: {ex.Message}");
            }

            // ICD 10.2.1 says a 20-second heartbeat, but a real panel drops an
            // unattended TX connection after ~12.6s (observed 2026-07-20) and
            // the VB6 tmrHeartbeat ran at 10s - match the VB. Faster than the
            // AbstractPanel default (60s) so we can't reuse kHeartbeatDelaySeconds.
            const int kTaktisHeartbeatMs = 10 * 1000;
            heartbeat_timer = new System.Threading.Timer(
                heartbeat_timer_callback,
                this.Identifier,
                kTaktisHeartbeatMs,
                kTaktisHeartbeatMs);
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
                    // Timer would be disabled here
                }


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

                // Writes routed by TransmissionType: TX channel for control/
                // heartbeat, RX channel for event-log requests + EVENT_ACK.
                // EVENT_ACKs never gate - the panel doesn't reply to them.
                byte[] bytes = convertstringarraytobytearray(dataToSend);
                bool gate = sendType != TakSendType.TAKSendEventACKRX
                         && sendType != TakSendType.TAKSendEventACKTX;
                WriteFrame(bytes, rxTx, gate);
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
                    data = CreateBasicMessage(8, TakCommands.CMD_REQUEST_ACTIVE_EVENTS);
                    rxTx = TransmissionType.RX;
                    immediateRxSend = true;
                    break;

                case TakSendType.TAKSendRequestActEventsTX:
                    NotifyClient("Request Active Events TX");
                    data = CreateBasicMessage(8, TakCommands.CMD_REQUEST_ACTIVE_EVENTS);
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
                    break;

                case TakSendType.TAKSendRequestEventLogEx:
                    NotifyClient(_reconnect ? "Send Request Event Log EX - Immediate" : "Send Request Event Log EX");
                    immediateRxSend = true;
                    _requestEventLogEXSent = true;
                    // VB6 TAKSendRequestEventLogEx (line 2622): gbaryDataToTX(7) = "78" — same
                    // wire type as REQUEST_EVENT_LOG (0x4E = 78), not 0x14E (334).
                    // The 0x14E comment in the original ICD note was a misread;
                    // the panel rejects type 334 as unknown, breaking live event subscription.
                    data = CreateMessageWithType32(12, 0x4E);
                    if (serialNoStr != null && serialNoStr.Length >= 4)
                    {
                        data[8] = serialNoStr[0];
                        data[9] = serialNoStr[1];
                        data[10] = serialNoStr[2];
                        data[11] = serialNoStr[3];
                    }
                    rxTx = TransmissionType.RX;
                    break;

                case TakSendType.TAKSendEventACKRX:
                    NotifyClient("Send Event ACK RX");
                    immediateRxSend = true;
                    //_messageSent = true;
                    data = CreateMessageWithSerialNo(12, TakCommands.CMD_EVENT_ACK, serialNoStr);
                    dataString = string.Join(",", data);
                    Console.WriteLine(DateTime.Now + ": " + "Send Event ACK RX: " + dataString);
                    rxTx = TransmissionType.RX;
                    break;

                case TakSendType.TAKSendEventACKTX:
                    NotifyClient("Send Event ACK TX");
                    immediateTxSend = true;
                    //_messageSent = true;
                    data = CreateMessageWithSerialNo(12, TakCommands.CMD_EVENT_ACK, serialNoStr);
                    dataString = string.Join(",", data);
                    Console.WriteLine(DateTime.Now + ": " + "Send Event ACK TX: " + dataString);
                    rxTx = TransmissionType.TX;
                    break;

                case TakSendType.TAKSendACK:
                    NotifyClient("Send ACK Immediate");
                    //_messageSent = true;
                    immediateRxSend = true;
                    data = CreateBasicMessage(8, TakCommands.CMD_ACK);
                    dataString = string.Join(",", data);
                    Console.WriteLine(DateTime.Now + ": " + "Send ACK Immediate: " + dataString);
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
                    NotifyClient($"Send Start Connection TX: {_connectionMonitoringSentTX}");
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

        // ICD layout: every frame starts with length (4 bytes, BE u32) at
        // offsets 0..3, then messageType (4 bytes, BE u32) at offsets 4..7.
        // Both fields used to fit in a single low byte (length<=248,
        // messageType<256) - except REQUEST_EVENT_LOG_EX, which is 0x14E and
        // needs the upper byte. CreateBasicMessage handles the common case;
        // CreateMessageWithType32 handles the >255 case.
        private string[] CreateBasicMessage(int length, string command)
        {
            if (length < 8) throw new ArgumentException("frame length must be >= 8", nameof(length));
            var data = new string[length];
            for (int i = 0; i < length; i++) data[i] = "0";
            data[3] = length.ToString();
            data[7] = command;
            return data;
        }

        private string[] CreateMessageWithType32(int length, int messageType)
        {
            if (length < 8) throw new ArgumentException("frame length must be >= 8", nameof(length));
            var data = new string[length];
            for (int i = 0; i < length; i++) data[i] = "0";
            data[3] = length.ToString();
            data[4] = ((messageType >> 24) & 0xff).ToString();
            data[5] = ((messageType >> 16) & 0xff).ToString();
            data[6] = ((messageType >> 8) & 0xff).ToString();
            data[7] = (messageType & 0xff).ToString();
            return data;
        }

        private string[] CreateMessageWithSerialNo(int length, string command, string[] serialNo)
        {
            var data = CreateBasicMessage(length, command);
            if (serialNo != null && serialNo.Length >= 4 && length >= 12)
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

        // ICD §9.1.34: SET_TIME payload is a 32-bit BE integer = seconds since
        // 2000-01-01 00:00:00 UTC. Length 12 = 4 (length) + 4 (type) + 4 (secs).
        private string[] CreateSetTimeMessage()
        {
            var data = CreateBasicMessage(12, TakCommands.CMD_SET_TIME);
            var epoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            long seconds = (long)(DateTime.UtcNow - epoch).TotalSeconds;
            if (seconds < 0) seconds = 0;
            data[8] = ((seconds >> 24) & 0xff).ToString();
            data[9] = ((seconds >> 16) & 0xff).ToString();
            data[10] = ((seconds >> 8) & 0xff).ToString();
            data[11] = (seconds & 0xff).ToString();
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

        // Legacy txTimer/rxTimer queue, the dual SendBytesToPanel writer that
        // disposed the live NetworkStream every send, and the original
        // StartListening read loop have all been removed; writes now serialise
        // through WriteFrame and reads happen on the single ReaderLoop.

        // Frame layout (post optional 8-byte sentinel strip):
        //   0..3   length (BE u32)
        //   4..7   message type (BE u32)
        //   8..11  serial number (4 bytes)
        //   12..15 event group (BE u32)
        //   16..19 event type (BE u32)
        //   20..23 event code (BE u32)
        //   24..27 node (BE u32)
        //   28..31 address type (BE u32)
        //   32..35 address (BE u32)
        //   36..39 sub address (BE u32)
        //   40..43 loop (BE u32)
        //   44..47 zone (BE u32)
        //   48..51 input action (BE u32)
        //   52..55 timestamp (BE u32)
        //   56..135 location text (ASCII, NUL-padded)
        //   136..167 panel text (ASCII, NUL-padded)
        //   168..248 zone text (ASCII, NUL-padded)  -- 81 bytes per VB6 DecodeMessage:4240 "Case 168 To 248"
        //
        // The legacy decoder concatenated each byte's decimal string then
        // parsed - silently wrong for any value > 255. This now reads each
        // 4-byte field as a proper big-endian uint32.
        private void DecodeMessage(string responseHex)
        {
            string[] hexStrings = responseHex.Split('-');
            byte[] frame = hexStrings.Select(h => Convert.ToByte(h, 16)).ToArray();
            if (frame.Length <= 8) return;

            int o = 0;
            if (frame.Length >= 16
                && frame[0] == 0x00 && frame[1] == 0x00 && frame[2] == 0x00 && frame[3] == 0x08
                && frame[4] == 0x00 && frame[5] == 0x00 && frame[6] == 0x00 && frame[7] == 0x01)
            {
                o = 8;
            }

            long iMessageType = ReadFieldU32(frame, o + 4);

            if (frame.Length >= o + 12)
            {
                glSerialNo[0] = frame[o + 8];
                glSerialNo[1] = frame[o + 9];
                glSerialNo[2] = frame[o + 10];
                glSerialNo[3] = frame[o + 11];
            }

            long lEventGroup = ReadFieldU32(frame, o + 12);
            long lEventType = ReadFieldU32(frame, o + 16);
            long lEventCode = ReadFieldU32(frame, o + 20);
            int iNode = (int)ReadFieldU32(frame, o + 24);
            int iAddressType = (int)ReadFieldU32(frame, o + 28);
            int iAddress = (int)ReadFieldU32(frame, o + 32);
            int iSubAddress = (int)ReadFieldU32(frame, o + 36);
            int iLoop = (int)ReadFieldU32(frame, o + 40);
            int iZone = (int)ReadFieldU32(frame, o + 44);
            int iInputAction = (int)ReadFieldU32(frame, o + 48);
            long lTimeStamp = ReadFieldU32(frame, o + 52);

            string sLocationText = ReadFieldAscii(frame, o + 56, o + 135);
            string sPanelText = ReadFieldAscii(frame, o + 136, o + 167);
            string sZoneText = ReadFieldAscii(frame, o + 168, o + 248); // VB6: "Case 168 To 248" — 81 bytes

            enmTAKMessageType gMessageType = (enmTAKMessageType)iMessageType;
            enmTAKEventType gEventType = (enmTAKEventType)lEventType;
            enmTAKEventCode gEventCode = (enmTAKEventCode)lEventCode;

            // MH 14/09/23: legacy panels report node 254 for the local panel.
            if (iNode == 254) iNode = 1;

            switch (iMessageType)
            {
                case 0:   // NAK
                case 1:   // ACK
                case 2:   // Event ID
                    break;
                case 133: // Start event
                    ParseTAKMessage(gMessageType, glSerialNo, lEventGroup, gEventType, gEventCode,
                        iNode, iAddressType, iAddress, iSubAddress, iLoop, iZone, iInputAction,
                        lTimeStamp, sLocationText, sPanelText, sZoneText, "", true);
                    break;
                case 135: // Clear event
                    ParseTAKMessage(gMessageType, glSerialNo, lEventGroup, gEventType, gEventCode,
                        iNode, iAddressType, iAddress, iSubAddress, iLoop, iZone, iInputAction,
                        lTimeStamp, sLocationText, sPanelText, sZoneText, "", false);
                    break;
                case 68:  // TAKMsgQueryAnalogDetail response
                    {
                        // Different field layout from the standard event frame —
                        // TAKNetManager.bas:5204 ("Decode Analog Value") re-decodes:
                        // node o+8, loop o+12 (0-based on the wire, +1 for display),
                        // address o+16, subaddress o+20, device type o+24,
                        // analogue value o+28, zone o+40. The standard-frame reads
                        // above (serial number, event group…) do not apply here.
                        int aNode = (int)ReadFieldU32(frame, o + 8);
                        if (aNode == 254) aNode = 1;   // legacy panels report 254 for the local panel
                        int aLoop = (int)ReadFieldU32(frame, o + 12) + 1;
                        int aAddress = (int)ReadFieldU32(frame, o + 16);
                        int aValue = (int)ReadFieldU32(frame, o + 28);

                        NotifyClient("Analogue Node Received: " + aNode, false);
                        NotifyClient("Analogue Address Received: " + aAddress, false);
                        NotifyClient("Analogue Value Received: " + aValue, false);

                        addtoanalogue("Taktis", aNode, aLoop, aAddress.ToString(), aValue);
                    }
                    break;
            }
        }

        private static long ReadFieldU32(byte[] frame, int offset)
        {
            if (offset + 3 >= frame.Length) return 0;
            return ((long)frame[offset] << 24)
                 | ((long)frame[offset + 1] << 16)
                 | ((long)frame[offset + 2] << 8)
                 | (long)frame[offset + 3];
        }

        private static string ReadFieldAscii(byte[] frame, int start, int end)
        {
            var sb = new StringBuilder();
            int last = Math.Min(end, frame.Length - 1);
            for (int i = start; i <= last; i++)
            {
                if (frame[i] != 0) sb.Append((char)frame[i]);
            }
            return sb.ToString();
        }



        #endregion
    }
}

