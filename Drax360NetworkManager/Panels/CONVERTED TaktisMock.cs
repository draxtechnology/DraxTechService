using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/*
namespace TakTisControl
{
    /*
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

    public enum TransmissionType
    {
        RX,
        TX
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

    #region Classes
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
    #endregion

    public class TakTisMessageSender
    {
        #region Fields
        private readonly Queue<TakControl> _txQueue = new Queue<TakControl>();
        private readonly Queue<TakControlRX> _rxQueue = new Queue<TakControlRX>();

        private bool _messageSent;
        private bool _reconnect;
        private bool _txRestart;
        private bool _commError;
        private bool _forceReset;
        private bool _requestEventLogSent;
        private bool _requestEventLogEXSent;
        private bool _connectionMonitoringSent;
        private bool _connectionMonitoringSentTX;
        private bool _connectionStopMonitoringSentTX;
        private bool _connectionStopMonitoringSentRX;
        private bool _heartBeatSent;

        private int _heartBeatCount;
        private long _numHeartbeats;
        private string[] _timeArray;
        #endregion

        #region Events
        public event EventHandler<LogEventArgs> LogMessage;
        public event EventHandler<SendImmediateEventArgs> SendImmediateRequest;
        public event EventHandler<TimerEventArgs> StartTxTimer;
        public event EventHandler<TimerEventArgs> StartRxTimer;
        #endregion

        #region Public Properties
        public int TxQueueCount => _txQueue.Count;
        public int RxQueueCount => _rxQueue.Count;
        public bool CommError
        {
            get => _commError;
            set => _commError = value;
        }
        #endregion

        #region Main Method
        public void SendToTakTis(
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
                Log($"SendToTakTis - Type: {sendType}");

                bool stopSending = false;
                TransmissionType rxTx = TransmissionType.TX;
                bool immediateRxSend = false;
                bool immediateTxSend = false;

                // Adjust loop index (VB code had piLoop - 1)
                if (loop >= 0)
                {
                    loop = loop - 1;
                    Log($"Loop adjusted to: {loop}");
                }

                // Handle message sent state
                if (_messageSent)
                {
                    stopSending = true;
                    // Timer would be disabled here
                }

                // Build message based on type
                string[] dataToSend = BuildMessage(
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
                    Log($"Unknown send type: {sendType}");
                    return;
                }

                // Handle immediate send
                if (immediateTxSend || immediateRxSend)
                {
                    immediateRxSend = false;
                    immediateTxSend = false;

                    Log($"Send {rxTx} Immediate");
                    OnSendImmediateRequest(new SendImmediateEventArgs
                    {
                        Data = dataToSend,
                        TransmissionType = rxTx
                    });
                    return;
                }

                // Queue the message
                QueueMessage(dataToSend, rxTx, stopSending);
            }
            catch (Exception ex)
            {
                Log($"Error in SendToTakTis: {ex.Message}");
            }
        }
        #endregion

        #region Message Building
        private string[] BuildMessage(
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
            string[] serialNoStr = ConvertSerialNumber(serialNo);
            string clientIDStr = clientID.ToString();
            string panelNo = node.ToString();

            switch (sendType)
            {
                case TakSendType.TAKSendRequestActEvents:
                    Log("Request Active Events RX");
                    _messageSent = true;
                    data = CreateBasicMessage(8, TakCommands.CMD_REQUEST_ACTIVE_EVENTS);
                    rxTx = TransmissionType.RX;
                    if (_reconnect)
                        immediateRxSend = true;
                    break;

                case TakSendType.TAKSendRequestActEventsTX:
                    Log("Request Active Events TX");
                    _messageSent = true;
                    data = CreateBasicMessage(8, TakCommands.CMD_REQUEST_ACTIVE_EVENTS);
                    rxTx = TransmissionType.TX;
                    if (_reconnect)
                        immediateTxSend = true;
                    break;

                case TakSendType.TAKSendRequestEventLog:
                    _requestEventLogSent = true;
                    Log(_reconnect ? "Send Request Event Log - Immediate" : "Send Request Event Log");
                    _messageSent = true;
                    data = CreateMessageWithSerialNo(12, TakCommands.CMD_REQUEST_EVENT_LOG, serialNoStr);
                    rxTx = TransmissionType.RX;
                    HandleReconnect();
                    break;

                case TakSendType.TAKSendRequestEventLogEx:
                    Log(_reconnect ? "Send Request Event Log EX - Immediate" : "Send Request Event Log EX");
                    _messageSent = true;
                    _requestEventLogEXSent = true;
                    data = CreateMessageWithSerialNo(12, TakCommands.CMD_REQUEST_EVENT_LOG, serialNoStr);
                    rxTx = TransmissionType.RX;
                    HandleReconnect();
                    break;

                case TakSendType.TAKSendEventACKRX:
                    Log("Send Event ACK RX");
                    immediateRxSend = true;
                    _messageSent = true;
                    data = CreateMessageWithSerialNo(12, TakCommands.CMD_EVENT_ACK, serialNoStr);
                    rxTx = TransmissionType.RX;
                    break;

                case TakSendType.TAKSendEventACKTX:
                    Log("Send Event ACK TX");
                    immediateTxSend = true;
                    _messageSent = true;
                    data = CreateMessageWithSerialNo(12, TakCommands.CMD_EVENT_ACK, serialNoStr);
                    rxTx = TransmissionType.TX;
                    break;

                case TakSendType.TAKSendACK:
                    Log("Send ACK Immediate");
                    _messageSent = true;
                    immediateRxSend = true;
                    data = CreateBasicMessage(8, TakCommands.CMD_ACK);
                    rxTx = TransmissionType.RX;
                    break;

                case TakSendType.TAKSendNACK:
                    Log("Send NACK");
                    _messageSent = true;
                    data = CreateBasicMessage(8, TakCommands.CMD_NACK);
                    rxTx = TransmissionType.RX;
                    break;

                case TakSendType.TAKSendStartConnectionMonitoringRX:
                    _connectionMonitoringSent = true;
                    Log(_reconnect ? "Send Start Connection Immediate RX" : "Send Start Connection RX");
                    if (_reconnect)
                        immediateRxSend = true;
                    data = CreateConnectionMessage(clientIDStr, TakCommands.CMD_START_MONITORING);
                    rxTx = TransmissionType.RX;
                    break;

                case TakSendType.TAKSendStartConnectionMonitoringTX:
                    _connectionMonitoringSentTX = true;
                    Log((_txRestart ? "Send Start Connection Immediate TX: " : "Send Start Connection TX: ") +
                        _connectionMonitoringSentTX);
                    if (_txRestart)
                        immediateTxSend = true;
                    data = CreateConnectionMessage(clientIDStr, TakCommands.CMD_START_MONITORING);
                    rxTx = TransmissionType.TX;
                    break;

                case TakSendType.TAKSendStopConnectionMonitoringTX:
                    _connectionMonitoringSent = false;
                    _connectionStopMonitoringSentTX = true;
                    Log("Send Stop Connection TX");
                    data = CreateConnectionMessage(clientIDStr, TakCommands.CMD_STOP_MONITORING);
                    rxTx = TransmissionType.TX;
                    break;

                case TakSendType.TAKSendStopConnectionMonitoringRX:
                    _connectionMonitoringSent = false;
                    _connectionStopMonitoringSentRX = true;
                    Log("Send Stop Connection RX");
                    data = CreateConnectionMessage(clientIDStr, TakCommands.CMD_STOP_MONITORING);
                    rxTx = TransmissionType.RX;
                    break;

                case TakSendType.TAKSendHeartBeatTX:
                    immediateTxSend = true;
                    Log($"Send Heartbeat TX {immediateTxSend}");
                    _heartBeatCount++;
                    _numHeartbeats++;
                    _heartBeatSent = true;
                    data = CreateHeartbeatMessage(clientIDStr);
                    rxTx = TransmissionType.TX;
                    break;

                case TakSendType.TAKSendHeartBeatRX:
                    Log("Send Heartbeat RX");
                    _heartBeatCount++;
                    _numHeartbeats++;
                    _heartBeatSent = true;
                    data = CreateHeartbeatMessage(clientIDStr);
                    rxTx = TransmissionType.RX;
                    break;

                case TakSendType.TAKSendQueryANALDetails:
                    Log($"TAKSendQueryANALDetails - Node:{node} Loop:{loop} Addr:{address} SubAddr:{subAddress}");
                    _messageSent = true;
                    data = CreateDeviceQueryMessage(node, loop, address, subAddress, TakCommands.CMD_QUERY_ANAL_DETAILS);
                    break;

                case TakSendType.TAKSendControlEnableDevice:
                    Log("Enable Device");
                    _messageSent = true;
                    data = CreateDeviceQueryMessage(node, loop, address, subAddress, TakCommands.CMD_ENABLE_DEVICE);
                    break;

                case TakSendType.TAKSendControlDisableDevice:
                    Log("Disable Device");
                    _messageSent = true;
                    data = CreateDeviceQueryMessage(node, loop, address, subAddress, TakCommands.CMD_DISABLE_DEVICE);
                    break;

                case TakSendType.TAKSendControlEnableZone:
                    Log("Enable Zone");
                    _messageSent = true;
                    data = CreateZoneMessage(zone, TakCommands.CMD_ENABLE_ZONE);
                    break;

                case TakSendType.TAKSendControlDisableZone:
                    Log("Disable Zone");
                    _messageSent = true;
                    data = CreateDisableZoneMessage(zone, subAddress);
                    break;

                case TakSendType.TAKSendControlReset:
                    Log(_forceReset ? $"Reset Immediate {node}:{panelNo}" : $"Reset {node}:{panelNo}");
                    _messageSent = true;
                    if (_forceReset)
                        immediateTxSend = true;
                    data = CreatePanelControlMessage(panelNo, TakCommands.CMD_RESET);
                    break;

                case TakSendType.TAKSendControlSilence:
                    Log("Silence");
                    _messageSent = true;
                    data = CreatePanelControlMessage(node.ToString(), TakCommands.CMD_SILENCE);
                    break;

                case TakSendType.TAKSendControlStartAlert:
                    Log("Start Alert");
                    _messageSent = true;
                    data = CreatePanelControlMessage(node.ToString(), TakCommands.CMD_START_ALERT);
                    break;

                case TakSendType.TAKSendControlStartEVAC:
                    Log("Start Evac");
                    _messageSent = true;
                    data = CreatePanelControlMessage(node.ToString(), TakCommands.CMD_START_EVAC);
                    break;

                case TakSendType.TAKSendSetTime:
                    Log("Set Time");
                    _messageSent = true;
                    data = CreateSetTimeMessage();
                    break;

                case TakSendType.TAKSendControlPIOOutput:
                    Log("PIO Output");
                    _messageSent = true;
                    data = CreateExtendedControlMessage(onOff, DeviceTypes.PIO_OUTPUT, loop, address, subAddress, zone);
                    break;

                case TakSendType.TAKSendControlIOChannel:
                    Log("IO Channel");
                    _messageSent = true;
                    data = CreateExtendedControlMessage(onOff, DeviceTypes.IO_CHANNEL, loop, address, subAddress, zone);
                    break;

                case TakSendType.TAKSendControlSubAddress:
                    Log("Sub Address");
                    _messageSent = true;
                    data = CreateExtendedControlMessage(onOff, DeviceTypes.SUB_ADDRESS, loop, address, subAddress, zone);
                    break;

                case TakSendType.TAKSendControlSounder:
                    Log("Sounder");
                    _messageSent = true;
                    data = CreateExtendedControlMessage(onOff, DeviceTypes.SOUNDER, loop, address, subAddress, zone);
                    break;

                case TakSendType.TAKSendControlZone:
                    Log("Control Zone");
                    _messageSent = true;
                    data = CreateExtendedControlMessage(onOff, DeviceTypes.ZONE, loop, address, subAddress, zone);
                    break;

                case TakSendType.TAKSendControlSilenceBuzzer:
                    Log("Silence Buzzer");
                    _messageSent = true;
                    data = CreatePanelControlMessage(node.ToString(), TakCommands.CMD_SILENCE_BUZZER);
                    break;

                case TakSendType.TAKSendControlResound:
                    Log("Resound");
                    _messageSent = true;
                    data = CreatePanelControlMessage(node.ToString(), TakCommands.CMD_RESOUND);
                    break;

                case TakSendType.TAKSendControlStartTest:
                    Log("Start Test");
                    _messageSent = true;
                    data = CreatePanelControlMessage(node.ToString(), TakCommands.CMD_START_TEST);
                    break;

                default:
                    Log($"Unhandled send type: {sendType}");
                    break;
            }

            return data;
        }
        #endregion

        #region Message Creation Helpers
        private string[] CreateBasicMessage(int length, string command)
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
            for (int i = 0; i < 8; i++)
                data[i] = "0";
            data[3] = "12";
            data[7] = command;
            data[11] = clientID;
            return data;
        }

        private string[] CreateHeartbeatMessage(string clientID)
        {
            var data = new string[12];
            for (int i = 0; i < 8; i++)
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
                    Log($"Set Time{i}: {data[8 + i]}");
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

        private string[] ConvertSerialNumber(long[] serialNo)
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
        #endregion

        #region Queue Management
        private void QueueMessage(string[] data, TransmissionType rxTx, bool stopSending)
        {
            if (rxTx == TransmissionType.RX)
            {
                var rxControl = new TakControlRX
                {
                    EncodedPacketRX = data,
                    RXTX = rxTx
                };

                if (_rxQueue.Count == 0)
                {
                    _rxQueue.Enqueue(rxControl);
                    Log($"Add to RX Queue - Count: {_rxQueue.Count}");
                    OnStartRxTimer(new TimerEventArgs { Interval = 1000 });
                }
                else
                {
                    if (_commError)
                    {
                        ClearAllQueues();
                        Log("Comm Error - Queues cleared");
                    }
                    else
                    {
                        _rxQueue.Enqueue(rxControl);
                        Log($"Add to RX Queue (multiple) - Count: {_rxQueue.Count}");
                        OnStartRxTimer(new TimerEventArgs { Interval = 1000 });
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
                    Log($"Add to TX Queue - Count: {_txQueue.Count}");
                    OnStartTxTimer(new TimerEventArgs { Interval = 1000 });
                }
                else
                {
                    if (_commError)
                    {
                        ClearAllQueues();
                        Log("Comm Error - Queues cleared");
                    }
                    else
                    {
                        _txQueue.Enqueue(txControl);
                        Log($"Add to TX Queue (multiple) - Count: {_txQueue.Count}");
                        OnStartTxTimer(new TimerEventArgs { Interval = 1000 });
                    }
                }
            }
        }

        private void ClearAllQueues()
        {
            _rxQueue.Clear();
            _txQueue.Clear();
        }

        public TakControl DequeueNextTxMessage()
        {
            return _txQueue.Count > 0 ? _txQueue.Dequeue() : null;
        }

        public TakControlRX DequeueNextRxMessage()
        {
            return _rxQueue.Count > 0 ? _rxQueue.Dequeue() : null;
        }
        #endregion

        #region Helper Methods
        private void HandleReconnect()
        {
            if (_reconnect)
            {
                _reconnect = false;
                Log("Reconnect Set To False");

                if (_txQueue.Count > 0)
                {
                    Log("Turn Send timer on");
                    OnStartTxTimer(new TimerEventArgs { Interval = 1000 });
                }

                if (_rxQueue.Count > 0)
                {
                    Log("Turn Send timer RX on");
                    OnStartRxTimer(new TimerEventArgs { Interval = 1000 });
                }
            }
        }

        public void SetTimeArray(string[] timeArray)
        {
            _timeArray = timeArray;
        }

        public void SetReconnect(bool value)
        {
            _reconnect = value;
        }

        public void SetTxRestart(bool value)
        {
            _txRestart = value;
        }

        public void SetForceReset(bool value)
        {
            _forceReset = value;
        }
        #endregion

        #region Event Raising
        protected virtual void Log(string message)
        {
            LogMessage?.Invoke(this, new LogEventArgs { Message = message });
        }

        protected virtual void OnSendImmediateRequest(SendImmediateEventArgs e)
        {
            SendImmediateRequest?.Invoke(this, e);
        }

        protected virtual void OnStartTxTimer(TimerEventArgs e)
        {
            StartTxTimer?.Invoke(this, e);
        }

        protected virtual void OnStartRxTimer(TimerEventArgs e)
        {
            StartRxTimer?.Invoke(this, e);
        }
        #endregion
    }

    #region Event Args
    public class LogEventArgs : EventArgs
    {
        public string Message { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class SendImmediateEventArgs : EventArgs
    {
        public string[] Data { get; set; }
        public TransmissionType TransmissionType { get; set; }
    }

    public class TimerEventArgs : EventArgs
    {
        public int Interval { get; set; }
    }
    #endregion

    #region Network Manager Interface
    /// <summary>
    /// Interface for TAK Network Manager implementations
    /// </summary>
    public interface ITakNetworkManager
    {
        void SendImmediate(string[] data, TransmissionType type);
        void StartTxTimer(int interval);
        void StartRxTimer(int interval);
        void StopTxTimer();
        void StopRxTimer();
        bool IsConnected { get; }
    }
    #endregion

    #region Example Network Manager Implementation
    /// <summary>
    /// Example implementation of TAK Network Manager
    /// </summary>
    public class TakNetworkManager : ITakNetworkManager
    {
        private System.Timers.Timer _txTimer;
        private System.Timers.Timer _rxTimer;
        private readonly TakTisMessageSender _messageSender;

        public bool IsConnected { get; private set; }

        public TakNetworkManager()
        {
            _messageSender = new TakTisMessageSender();

            // Subscribe to events
            _messageSender.LogMessage += OnLogMessage;
            _messageSender.SendImmediateRequest += OnSendImmediateRequest;
            _messageSender.StartTxTimer += OnStartTxTimer;
            _messageSender.StartRxTimer += OnStartRxTimer;

            // Initialize timers
            _txTimer = new System.Timers.Timer();
            _txTimer.Elapsed += TxTimer_Elapsed;

            _rxTimer = new System.Timers.Timer();
            _rxTimer.Elapsed += RxTimer_Elapsed;
        }

        public void SendImmediate(string[] data, TransmissionType type)
        {
            try
            {
                Console.WriteLine($"[IMMEDIATE] Sending {type} message: {string.Join(",", data)}");
                // Implement actual network send here
                // e.g., socket.Send(ConvertToBytes(data));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending immediate: {ex.Message}");
            }
        }

        public void StartTxTimer(int interval)
        {
            _txTimer.Stop();
            _txTimer.Interval = interval;
            _txTimer.Start();
        }

        public void StartRxTimer(int interval)
        {
            _rxTimer.Stop();
            _rxTimer.Interval = interval;
            _rxTimer.Start();
        }

        public void StopTxTimer()
        {
            _txTimer.Stop();
        }

        public void StopRxTimer()
        {
            _rxTimer.Stop();
        }

        private void TxTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            _txTimer.Stop();

            var message = _messageSender.DequeueNextTxMessage();
            if (message != null)
            {
                Console.WriteLine($"[TX TIMER] Sending queued message: {string.Join(",", message.EncodedPacket)}");
                // Implement actual network send here

                // If more messages in queue, restart timer
                if (_messageSender.TxQueueCount > 0)
                {
                    _txTimer.Start();
                }
            }
        }

        private void RxTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            _rxTimer.Stop();

            var message = _messageSender.DequeueNextRxMessage();
            if (message != null)
            {
                Console.WriteLine($"[RX TIMER] Sending queued message: {string.Join(",", message.EncodedPacket)}");
                // Implement actual network send here

                // If more messages in queue, restart timer
                if (_messageSender.RxQueueCount > 0)
                {
                    _rxTimer.Start();
                }
            }
        }

        private void OnLogMessage(object sender, LogEventArgs e)
        {
            Console.WriteLine($"[{e.Timestamp:HH:mm:ss.fff}] {e.Message}");
        }

        private void OnSendImmediateRequest(object sender, SendImmediateEventArgs e)
        {
            SendImmediate(e.Data, e.TransmissionType);
        }

        private void OnStartTxTimer(object sender, TimerEventArgs e)
        {
            StartTxTimer(e.Interval);
        }

        private void OnStartRxTimer(object sender, TimerEventArgs e)
        {
            StartRxTimer(e.Interval);
        }

        public TakTisMessageSender GetMessageSender()
        {
            return _messageSender;
        }

        public void Dispose()
        {
            _txTimer?.Dispose();
            _rxTimer?.Dispose();
        }
    }
    #endregion

    #region Usage Example
    /// <summary>
    /// Example usage of the TAK message sender
    /// </summary>
    
    
     public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("TAK Taktis Message Sender Example\n");

            // Create network manager
            using (var networkManager = new TakNetworkManager())
            {
                var sender = networkManager.GetMessageSender();

                // Example 1: Request Active Events
                Console.WriteLine("\n=== Example 1: Request Active Events ===");
                sender.SendToTakTis(
                    TakSendType.TAKSendRequestActEvents,
                    clientID: 1);

                System.Threading.Thread.Sleep(100);

                // Example 2: Send Heartbeat
                Console.WriteLine("\n=== Example 2: Send Heartbeat TX ===");
                sender.SendToTakTis(
                    TakSendType.TAKSendHeartBeatTX,
                    clientID: 1);

                System.Threading.Thread.Sleep(100);

                // Example 3: Reset Panel
                Console.WriteLine("\n=== Example 3: Reset Panel ===");
                sender.SetForceReset(true);
                sender.SendToTakTis(
                    TakSendType.TAKSendControlReset,
                    node: 1);
                sender.SetForceReset(false);

                System.Threading.Thread.Sleep(100);

                // Example 4: Enable Device
                Console.WriteLine("\n=== Example 4: Enable Device ===");
                sender.SendToTakTis(
                    TakSendType.TAKSendControlEnableDevice,
                    node: 1,
                    loop: 1,
                    address: 5,
                    subAddress: 0);

                System.Threading.Thread.Sleep(100);

                // Example 5: Disable Zone
                Console.WriteLine("\n=== Example 5: Disable Zone ===");
                sender.SendToTakTis(
                    TakSendType.TAKSendControlDisableZone,
                    zone: 3,
                    subAddress: 0);

                System.Threading.Thread.Sleep(100);

                // Example 6: Control Sounder
                Console.WriteLine("\n=== Example 6: Turn On Sounder ===");
                sender.SendToTakTis(
                    TakSendType.TAKSendControlSounder,
                    loop: 1,
                    address: 10,
                    subAddress: 1,
                    zone: 5,
                    onOff: true);

                System.Threading.Thread.Sleep(100);

                // Example 7: Set Time
                Console.WriteLine("\n=== Example 7: Set Panel Time ===");
                var now = DateTime.Now;
                sender.SetTimeArray(new[]
                {
                    now.Second.ToString(),
                    now.Minute.ToString(),
                    now.Hour.ToString(),
                    now.Day.ToString()
                });
                sender.SendToTakTis(TakSendType.TAKSendSetTime);

                System.Threading.Thread.Sleep(100);

                // Example 8: Start Connection Monitoring
                Console.WriteLine("\n=== Example 8: Start Connection Monitoring ===");
                sender.SendToTakTis(
                    TakSendType.TAKSendStartConnectionMonitoringTX,
                    clientID: 1);

                System.Threading.Thread.Sleep(100);

                // Example 9: Send Event Acknowledgment with Serial Number
                Console.WriteLine("\n=== Example 9: Event Acknowledgment ===");
                long[] serialNumber = { 0, 0, 0, 123 };
                sender.SendToTakTis(
                    TakSendType.TAKSendEventACKRX,
                    serialNo: serialNumber);

                System.Threading.Thread.Sleep(100);

                // Example 10: Start Evacuation
                Console.WriteLine("\n=== Example 10: Start Evacuation ===");
                sender.SendToTakTis(
                    TakSendType.TAKSendControlStartEVAC,
                    node: 1);

                // Wait for queued messages to process
                Console.WriteLine("\n=== Waiting for queued messages to process ===");
                System.Threading.Thread.Sleep(5000);

                Console.WriteLine("\n=== Complete ===");
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
    #endregion

    #region Utility Classes
    /// <summary>
    /// Helper class for message validation and conversion
    /// </summary>
    public static class TakMessageHelper
    {
        /// <summary>
        /// Converts string array to byte array for transmission
        /// </summary>
        public static byte[] ConvertToBytes(string[] data)
        {
            if (data == null) return new byte[0];

            var bytes = new List<byte>();
            foreach (var item in data)
            {
                if (byte.TryParse(item, out byte value))
                {
                    bytes.Add(value);
                }
            }
            return bytes.ToArray();
        }

        /// <summary>
        /// Validates message structure
        /// </summary>
        public static bool ValidateMessage(string[] data)
        {
            if (data == null || data.Length < 8)
                return false;

            // Validate length field
            if (!int.TryParse(data[3], out int length))
                return false;

            return data.Length == length;
        }

        /// <summary>
        /// Calculates checksum for message (implement based on protocol)
        /// </summary>
        public static byte CalculateChecksum(string[] data)
        {
            byte checksum = 0;
            var bytes = ConvertToBytes(data);

            foreach (var b in bytes)
            {
                checksum ^= b; // XOR checksum example
            }

            return checksum;
        }

        /// <summary>
        /// Formats message for display/logging
        /// </summary>
        public static string FormatMessage(string[] data)
        {
            if (data == null) return "null";
            return $"[{string.Join(", ", data)}] (Length: {data.Length})";
        }

        /// <summary>
        /// Converts message to hex string
        /// </summary>
        public static string ToHexString(string[] data)
        {
            if (data == null) return string.Empty;

            var bytes = ConvertToBytes(data);
            return BitConverter.ToString(bytes).Replace("-", " ");
        }
    }

    /// <summary>
    /// Message statistics tracker
    /// </summary>
    public class MessageStatistics
    {
        public int TotalMessagesSent { get; set; }
        public int HeartbeatsSent { get; set; }
        public int EventAcksSent { get; set; }
        public int ControlMessagesSent { get; set; }
        public int ErrorCount { get; set; }
        public DateTime LastMessageTime { get; set; }
        public TimeSpan AverageResponseTime { get; set; }

        public void IncrementMessageCount(TakSendType type)
        {
            TotalMessagesSent++;
            LastMessageTime = DateTime.Now;

            if (type == TakSendType.TAKSendHeartBeatRX || type == TakSendType.TAKSendHeartBeatTX)
                HeartbeatsSent++;
            else if (type == TakSendType.TAKSendEventACKRX || type == TakSendType.TAKSendEventACKTX)
                EventAcksSent++;
            else
                ControlMessagesSent++;
        }

        public override string ToString()
        {
            return $"Total: {TotalMessagesSent}, Heartbeats: {HeartbeatsSent}, " +
                   $"Event ACKs: {EventAcksSent}, Control: {ControlMessagesSent}, " +
                   $"Errors: {ErrorCount}, Last: {LastMessageTime:HH:mm:ss}";
        }
    }
    #endregion
    
}*/