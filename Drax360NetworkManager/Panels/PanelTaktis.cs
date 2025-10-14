using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Instrumentation;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.Remoting.Messaging;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Xml.Linq;
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
        #endregion

        #region Events
        public event EventHandler<SendImmediateEventArgs> SendImmediateRequest;
        public event EventHandler<TimerEventArgs> StartTxTimer;
        public event EventHandler<TimerEventArgs> StartRxTimer;
        #endregion

        #region public properties
        public int TxQueueCount => _txQueue.Count;
        public int RxQueueCount => _rxQueue.Count;
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
            sendtotaktis(TakSendType.TAKSendHeartBeatTX,clientID: 1);
        }

        private string gsIPAddress;
        private string gsIPPort;
        public override void StartUp(int fakemode)
        {
            gsIPAddress = base.GetSetting<string>(ksettingsetupsection, "PanelIPAddress");
            gsIPPort = base.GetSetting<string>(ksettingsetupsection, "IPPort");


            // RJ switched off timers
            //_messageSender.LogMessage += OnLogMessage;
            //SendImmediateRequest += OnSendImmediateRequest;
            //StartTxTimer += OnStartTxTimer;
            //StartRxTimer += OnStartRxTimer;


            sendtotaktis(TakSendType.TAKSendRequestActEvents, clientID: 1);

            //sendtotaktis(TakSendType.TAKSendRequestActEventsTX, clientID: 1);


            // Initialize timers
            _txTimer = new System.Timers.Timer();
            _txTimer.Elapsed += TxTimer_Elapsed;
            // added a start
            _txTimer.Start();

            _rxTimer = new System.Timers.Timer();
            _rxTimer.Elapsed += RxTimer_Elapsed;

            _rxTimer.Start();

        }

        


        private void OnSendImmediateRequest(object sender, SendImmediateEventArgs e)
        {
            SendImmediateRequest?.Invoke(this, e);
        }

        public PanelTaktis(string baselogfolder, string identifier) : base(baselogfolder, identifier, "TAKMan","TAK")
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
            //serialNoStr[0] = "0";
            //serialNoStr[1] = "0";
            //serialNoStr[2] = "1";
            //serialNoStr[3] = "227";
            switch (sendType)
            {
                case TakSendType.TAKSendRequestActEvents:
                    NotifyClient("Request Active Events RX");
                    _messageSent = true;
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
                    _messageSent = true;
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
                    dataString = string.Join(",", data);
                    Console.WriteLine("Send Event ACK RX: " + dataString);
                    rxTx = TransmissionType.RX;
                    break;

                case TakSendType.TAKSendEventACKTX:
                    NotifyClient("Send Event ACK TX");
                    immediateTxSend = true;
                    _messageSent = true;
                    data = CreateMessageWithSerialNo(12, TakCommands.CMD_EVENT_ACK, serialNoStr);
                    dataString = string.Join(",", data);
                    Console.WriteLine("Send Event ACK TX: " + dataString);
                    rxTx = TransmissionType.TX;
                    break;

                case TakSendType.TAKSendACK:
                    NotifyClient("Send ACK Immediate");
                    _messageSent = true;
                    immediateRxSend = true;
                    data = CreateBasicMessage(8, TakCommands.CMD_ACK);
                    dataString = string.Join(",", data);
                    Console.WriteLine("Send ACK Immediate: " + dataString);
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
                    OnStartTxTimer(new TimerEventArgs {Interval = 1000 });
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

        private void TxTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (_txTimer != null)
            {
                _txTimer.Stop();
            }

            var message = DequeueNextTxMessage();
            if (message != null)
            {
                Console.WriteLine($"[TX TIMER] Sending queued message: {string.Join(",", message.EncodedPacket)}");
                // Implement actual network send here
                SendBytesToPanel( message.EncodedPacket);
                // If more messages in queue, restart timer
                if (TxQueueCount > 0)
                {
                    _txTimer.Start();
                }
            }
        }

        private void RxTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            _rxTimer.Stop();

            var message = DequeueNextRxMessage();
            if (message != null)
            {
                Console.WriteLine($"[RX TIMER] Sending queued message: {string.Join(",", message.EncodedPacket)}");
                // Implement actual network send here
               SendBytesToPanel(message.EncodedPacket);

                // If more messages in queue, restart timer
                //if (RxQueueCount > 0)
                {
                    _rxTimer.Start();
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
                using (var client = new TcpClient())
                {
                    client.Connect(gsIPAddress, port);
                    using (var stream = client.GetStream())
                    {
                        // Send data
                        stream.Write(data, 0, data.Length);
                        stream.Flush();

                        Thread.Sleep(1000); // wait for response

                        // Read response
                        var responseBuffer = new byte[1024];
                        int bytesRead = stream.Read(responseBuffer, 0, responseBuffer.Length);
                        if (bytesRead > 0)
                        {
                            // Convert response to hex string for readability
                            string responseHex = BitConverter.ToString(responseBuffer, 0, bytesRead);
                            NotifyClient($"Received response ({bytesRead} bytes): {responseHex}");

                            DecodeMessage(responseHex);

                            sendtotaktis(TakSendType.TAKSendEventACKRX, clientID: 1);
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
            string[] sSerialNo = new string[4];
            int iCharCount = 0;
            string[] aryHexMessage = responseHex.Split('-');
            // Skip the first 8 elements and create a new array
            aryHexMessage = aryHexMessage.Skip(8).ToArray();

            while (iCharCount < aryHexMessage.Length)
            {
                switch (iCharCount)
                {
                    case 4:
                    case 5:
                    case 6:
                    case 7:
                        sMessageType += aryHexMessage[iCharCount].ToString();
                        break;

                    case 8:

                        //glSerialNo[0] = Convert.ToInt64("&H" + aryHexMessage[iCharCount], 16);
                        sSerialNo[0] = aryHexMessage[iCharCount].ToString();

                        break;

                    case 9:

                        //glSerialNo[1] = Convert.ToInt64("&H" + aryHexMessage[iCharCount], 16);
                        sSerialNo[1] = aryHexMessage[iCharCount].ToString();

                        break;

                    case 10:

                        //glSerialNo[2] = Convert.ToInt64("&H" + aryHexMessage[iCharCount], 16);
                        sSerialNo[2] = aryHexMessage[iCharCount].ToString();

                        break;

                    case 11:

                        //glSerialNo[3] = Convert.ToInt64("&H" + aryHexMessage[iCharCount], 16);
                        sSerialNo[3] = aryHexMessage[iCharCount].ToString();

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

                        sInputAction += aryHexMessage[iCharCount].ToString();
                        break;

                    case 52:
                    case 53:
                    case 54:
                    case 55:

                        sTimeStamp += aryHexMessage[iCharCount].ToString();
                        break;

                    case int n when (n >= 56 && n <= 135):
                        int sAscii = Convert.ToInt32(aryHexMessage[iCharCount], 16);
                        if (sAscii != 0)
                            sLocationText += Convert.ToChar(sAscii);
                        break;

                    case int n when (n >= 136 && n <= 167):

                        if (string.IsNullOrEmpty(aryHexMessage[iCharCount])) aryHexMessage[iCharCount] = "0";
                        sAscii = Convert.ToInt32(aryHexMessage[iCharCount], 16);
                        if (sAscii != 0)
                            sPanelText += Convert.ToChar(sAscii);
                        break;

                    case int n when (n >= 168 && n <= 248):

                        if (string.IsNullOrEmpty(aryHexMessage[iCharCount])) aryHexMessage[iCharCount] = "0";
                        sAscii = Convert.ToInt32(aryHexMessage[iCharCount], 16);
                        if (sAscii != 0)
                            sZoneText += Convert.ToChar(sAscii);
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
            long iNode = Convert.ToInt64(sNode, 16);
            long iAddress = Convert.ToInt64(sAddress, 16);
            int evnum = 0;

            switch (iMessageType)
            {
                case 0:    // NAK
                    break;
                case 1:    // ACK
                    break;
                case 2:    // Packet Type Event ID
                    break;
                case 133:  // Start Event Message

                    evnum = CSAMXSingleton.CS.MakeInputNumber(Convert.ToInt32(iNode), Convert.ToInt32(sLoop), Convert.ToInt32(iAddress), 15);
                    send_response_amx(evnum, "", "Test Message");
                    break;
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
    }


    #endregion
}

