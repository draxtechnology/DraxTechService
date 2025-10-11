using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using static Drax360Service.Panels.PanelTaktis;



namespace Drax360Service.Panels
{

    public enum TransmissionType
    {
        RX,
        TX
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
    internal class PanelTaktis : AbstractPanel
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
        #endregion

        #region Events
        public event EventHandler<SendImmediateEventArgs> SendImmediateRequest;
        public event EventHandler<TimerEventArgs> StartTxTimer;
        public event EventHandler<TimerEventArgs> StartRxTimer;
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
            sendtotaktis(
                       TakSendType.TAKSendHeartBeatTX,
                       clientID: 1);
        }

        public override void StartUp(int fakemode)
        {
            string gsIPAddress = base.GetSetting<string>(ksettingsetupsection, "PanelIPAddress");
            string gsIPPort = base.GetSetting<string>(ksettingsetupsection, "IPPort");
            
        }

        public PanelTaktis(string baselogfolder, string identifier) : base(baselogfolder, identifier, "TAKMan","TAK")
        {
            if (!String.IsNullOrEmpty(identifier))
            {
                heartbeat_timer = new Timer(heartbeat_timer_callback, this.Identifier, 1000, kHeartbeatDelaySeconds * 1000);
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


                // RJ Removed 

                /*
                // Handle message sent state
                if (_messageSent)
                {
                    stopSending = true;
                    // Timer would be disabled here
                }
                */

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

            switch (sendType)
            {
                case TakSendType.TAKSendRequestActEvents:
                    NotifyClient("Request Active Events RX");
                    _messageSent = true;
                    data = CreateBasicMessage(8, TakCommands.CMD_REQUEST_ACTIVE_EVENTS);
                    rxTx = TransmissionType.RX;
                    if (_reconnect)
                        immediateRxSend = true;
                    break;

                case TakSendType.TAKSendRequestActEventsTX:
                    NotifyClient("Request Active Events TX");
                    _messageSent = true;
                    data = CreateBasicMessage(8, TakCommands.CMD_REQUEST_ACTIVE_EVENTS);
                    rxTx = TransmissionType.TX;
                    if (_reconnect)
                        immediateTxSend = true;
                    break;

                case TakSendType.TAKSendRequestEventLog:
                    _requestEventLogSent = true;
                    NotifyClient(_reconnect ? "Send Request Event Log - Immediate" : "Send Request Event Log");
                    _messageSent = true;
                    data = CreateMessageWithSerialNo(12, TakCommands.CMD_REQUEST_EVENT_LOG, serialNoStr);
                    rxTx = TransmissionType.RX;
                    HandleReconnect();
                    break;

                case TakSendType.TAKSendRequestEventLogEx:
                    NotifyClient(_reconnect ? "Send Request Event Log EX - Immediate" : "Send Request Event Log EX");
                    _messageSent = true;
                    _requestEventLogEXSent = true;
                    data = CreateMessageWithSerialNo(12, TakCommands.CMD_REQUEST_EVENT_LOG, serialNoStr);
                    rxTx = TransmissionType.RX;
                    HandleReconnect();
                    break;

                case TakSendType.TAKSendEventACKRX:
                    NotifyClient("Send Event ACK RX");
                    immediateRxSend = true;
                    _messageSent = true;
                    data = CreateMessageWithSerialNo(12, TakCommands.CMD_EVENT_ACK, serialNoStr);
                    rxTx = TransmissionType.RX;
                    break;

                case TakSendType.TAKSendEventACKTX:
                    NotifyClient("Send Event ACK TX");
                    immediateTxSend = true;
                    _messageSent = true;
                    data = CreateMessageWithSerialNo(12, TakCommands.CMD_EVENT_ACK, serialNoStr);
                    rxTx = TransmissionType.TX;
                    break;

                case TakSendType.TAKSendACK:
                    NotifyClient("Send ACK Immediate");
                    _messageSent = true;
                    immediateRxSend = true;
                    data = CreateBasicMessage(8, TakCommands.CMD_ACK);
                    rxTx = TransmissionType.RX;
                    break;

                case TakSendType.TAKSendNACK:
                    NotifyClient("Send NACK");
                    _messageSent = true;
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
                    OnStartTxTimer(new TimerEventArgs { Interval = 1000 });
                }

                if (_rxQueue.Count > 0)
                {
                    NotifyClient("Turn Send timer RX on");
                    OnStartRxTimer(new TimerEventArgs { Interval = 1000 });
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

                if (_rxQueue.Count == 0)
                {
                    _rxQueue.Enqueue(rxControl);
                    NotifyClient($"Add to RX Queue - Count: {_rxQueue.Count}");
                    OnStartRxTimer(new TimerEventArgs { Interval = 1000 });
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
                    NotifyClient($"Add to TX Queue - Count: {_txQueue.Count}");
                    OnStartTxTimer(new TimerEventArgs { Interval = 1000 });
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
}
