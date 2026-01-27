using System;
using System.Collections.Generic;
using System.Diagnostics.SymbolStore;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Messaging;
using System.Security.Policy;
using System.Threading;
using System.Timers;
using static Drax360Service.Panels.PanelTaktis;
using static System.Net.Mime.MediaTypeNames;

namespace Drax360Service.Panels
{
    internal class PanelMorleyZX : AbstractPanel
    {
        // Morley Protocol Constants
        private const byte MORLEY_HEADER_RESPONSE = 250;
        private const byte MORLEY_MSG_END = 253;
        private const byte MORLEY_HEADER_TWOWAY = 241;
        private const byte MORLEY_HEADER_BROADCAST = 252;
        private const byte MORLEY_SPECIAL_BYTE = 240;
        private const byte QUICK_STATUS_COMMAND = 16;
        private const byte QUICK_STATUS_RESPONSE = 17;
        private const byte DETAILED_ALARM_COMMAND = 20;
        private const byte DETAILED_ALARM_RESPONSE = 21;
        private const int MORLEY_MSG_IDENT_INDX = 3;
        private const int EVENT_LOG_RESPONSE = 57;
        private const int ASK_DEVICE_STATE_COMMAND = 62;   // TODO
        private const int ASK_DEVICE_STATE_RESPONSE = 63;
        private const int MAX_CONSECUTIVE_FAILURES = 10000;
        // Response timeout
        private const int RESPONSE_TIMEOUT = 20; // mSec = 2 Sec
        private const int RESPONSE_TIMEOUT_EXTENDED = 30; // mSec = 9.5 Sec
        private const int RESPONSE_TIMEOUT_SUPER_EXTENDED = 32; 

        private const byte MASTER_PANEL_ID = 1;
        private const byte SOURCE_ID = 3; // PC/Host ID

        private List<byte> receiveBuffer = new List<byte>();
        private System.Timers.Timer pollTimer;
        private bool isPollingEnabled = true;
        private bool waitingForDetailedResponse = false;
        private byte previousPanelStatusBitset = 0;
        private bool g_booIsolationEcho = true;
        List<int> garyZoneArray = new List<int>();
        private int consecutiveFailures = 0;
        private DateTime lastSuccessfulResponse = DateTime.MinValue;
        private int g_intResponseTimeout = RESPONSE_TIMEOUT;
        private int g_intAmx1Offset = 0;
        private int g_bytMasterPanelID = 0;
        private bool connectionLostNotified = false;

        private enum MorleyEventPriority
        {
            InTestMode = 1,
            MinorFault = 10,
            DeviceDisabled = 20,
            SeriousFault = 30,
            Security = 46,
            PreAlarm = 56,
            DayModeAlarm = 58,
            UnlocatedFire = 62,
            FireAlert = 64,
            FrontPanelEvacuate = 66,
            BombAlert = 68,
            FullFire = 70
        }

        public enum enmMorleyDeviceDetailCode : byte
        {
            Device_Status = 0,
            Device_Type = 1,
            Device_Analogue_Raw = 2,
            Device_Analogue_Conditioned = 3
        }

        public enum MorleyEventNature
        {
            PreAlarmSignal = 4,
            FireAlarmSignal = 5,
            PanelReset = 8,
            EvacuationAlarm = 9,
            DetectorContaminated = 13,
            NoReplyFromDetector = 15,
            ExternalLinkMasterFailed = 18,
            DetectorDataCorrupted = 19,
            ProblemWithLoopWiring = 21,
            ProblemWithSounderCircuit = 22,
            ProblemWithPSU = 23,
            EarthFault = 25,
            ZoneTotallyDisabled = 27,
            NoReplyFromSlave = 28,
            BadReplyFromSlave = 29,
            WalkTest = 33,
            ZonePartiallyDisabled = 34,
            DetectorDisabled = 35,
            RelayOutputsDisabled = 36,
            SounderOutputsDisabled = 37
        }

        public enum MorleyDetectorType
        {
            ApolloShopUnit = 1,
            SounderDevice = 2,
            IOUnit = 3,
            IonisationDetector = 4,
            ZoneMonitor = 5,
            OpticalDetector = 6,
            HeatDetector = 7,
            CallPoint = 8,
            RelayDetector = 9,
            AnyNonSpecificSensor = 10,
            Mefs8WayInput = 11,
            MiniRepeater = 12,
            TestBox = 13,
            NoDetector = 14
        }
        public string alarmText = "";

        public override string FakeString
        {
            get =>

                //* MorleyZX

                "FA-03-01-3F-01-00-00-00-00-00-00-44-FD";
        }
        public PanelMorleyZX(string baselogfolder, string identifier) : base(baselogfolder, identifier, "MorleyMan", "MORLEY")
        {
            if (!String.IsNullOrEmpty(identifier))
            {
                heartbeat_timer = new System.Threading.Timer(heartbeat_timer_callback, this.Identifier, 1000, kHeartbeatDelaySeconds * 1000);
            }
        }

        public override void StartUp(int fakemode)
        {
            g_intResponseTimeout = RESPONSE_TIMEOUT;

            g_bytMasterPanelID = base.GetSetting<int>(ksettingsetupsection, "MasterPanelID");
            g_intAmx1Offset = base.GetSetting<int>(ksettingsetupsection, "AMX1Offset");

            if (base.GetSetting<int>(ksettingsetupsection, "ExtendedResponseTimeout") > 0)
            {
                g_intResponseTimeout = RESPONSE_TIMEOUT_EXTENDED;
            }
            if (base.GetSetting<int>(ksettingsetupsection, "SuperResponseTimeout") > 0)
            {
                g_intResponseTimeout = RESPONSE_TIMEOUT_SUPER_EXTENDED;
            }

            int settingbaudrate = 9600;
            string settingparity = "None";
            int settingdatabits = 8;
            int settingstopbits = 1;

            if (fakemode > 0)
            {
                return;
            }

            // we are a real serial port 
            serialport = new SerialPort(this.Identifier);
            serialport.BaudRate = settingbaudrate;

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
            serialport.Encoding = System.Text.Encoding.GetEncoding(28591); // ISO-8859-1 for binary
            serialport.DtrEnable = false;
            serialport.RtsEnable = false;
            serialport.ReadBufferSize = 8000;
            serialport.WriteBufferSize = 200;
            serialport.ReadTimeout = SerialPort.InfiniteTimeout;
            serialport.WriteTimeout = SerialPort.InfiniteTimeout;
            serialport.ReceivedBytesThreshold = 1; // Match VB6 RThreshold = 1

            // Set up event handler BEFORE opening
            serialport.ErrorReceived += SerialPort_ErrorReceived;
            serialport.DataReceived += SerialPort_DataReceived;

            if (serialport.IsOpen)
            {
                serialport.Close();
            }

            base.NotifyClient("Attempting Open " + serialport.PortName, false);

            try
            {
                serialport.Open();

                // Clear buffers after opening
                serialport.DiscardInBuffer();
                serialport.DiscardOutBuffer();

                base.NotifyClient("Successfully opened " + serialport.PortName, false);

                // Give the port a moment to stabilize
                Thread.Sleep(100);

                // Send initial poll command
                MorleyQuickPanelStatus(MASTER_PANEL_ID);

                // Start continuous polling timer (VB6 uses 3000ms interval)
                StartPollingTimer();
                lastSuccessfulResponse = System.DateTime.Now;
            }
            catch (Exception e)
            {
                base.NotifyClient("Failed To Open " + serialport.PortName + " " + e.ToString(), false);
            }
        }

        private void StartPollingTimer()
        {
            pollTimer = new System.Timers.Timer(3000); // Poll every 3 seconds like VB6
            pollTimer.Elapsed += PollTimer_Elapsed;
            pollTimer.AutoReset = true;
            pollTimer.Start();
            Console.WriteLine("Polling timer started (3 second interval)");
        }

        private void PollTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            // First, check if port is still physically present
            if (!IsPortStillAvailable())
            {
                int evnum = CSAMXSingleton.CS.MakeInputNumber(g_bytMasterPanelID, 0, 9, 15, true);
                send_response_amx(evnum, "", "Master Panel Offline or Not Responding");
                connectionLostNotified = true;
            }

            // Check if port is still open
            if (serialport?.IsOpen != true)
            {
                int evnum = CSAMXSingleton.CS.MakeInputNumber(g_bytMasterPanelID, 0, 9, 15, true);
                send_response_amx(evnum, "", "Master Panel Offline or Not Responding");
                connectionLostNotified = true;
            }

            // Try to send poll command
            bool sendSuccess = MorleyQuickPanelStatus(MASTER_PANEL_ID);

            if (!sendSuccess)
            {
                consecutiveFailures++;
                if (consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
                {
                    int evnum = CSAMXSingleton.CS.MakeInputNumber(g_bytMasterPanelID, 0, 9, 15, true);
                    send_response_amx(evnum, "", "Master Panel Offline or Not Responding");
                    connectionLostNotified = true;
                }
            }

            // Check response timeout
            if (lastSuccessfulResponse != DateTime.MinValue && (DateTime.Now - lastSuccessfulResponse).TotalSeconds > g_intResponseTimeout)
            {
                int evnum = CSAMXSingleton.CS.MakeInputNumber(g_bytMasterPanelID, 0, 9, 15, true);
                send_response_amx(evnum, "", "Master Panel Offline or Not Responding");
                connectionLostNotified = true;
            }
        }

        private bool IsPortStillAvailable()
        {
            // Check if the COM port still exists in the system
            string[] availablePorts = SerialPort.GetPortNames();
            return availablePorts.Contains(serialport.PortName);
        }
        private void SerialPort_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            base.NotifyClient($"Serial port error detected: {e.EventType}");

            switch (e.EventType)
            {
                case SerialError.Frame:
                case SerialError.Overrun:
                case SerialError.RXOver:
                case SerialError.RXParity:
                case SerialError.TXFull:
                    // These might indicate cable disconnect or hardware issues
                    //HandleSerialPortFailure("Hardware error detected");
                    base.NotifyClient($"Master Panel Offline or Not Responding: {serialport.CtsHolding}");
                    int evnum = CSAMXSingleton.CS.MakeInputNumber(g_bytMasterPanelID, 0, 9, 15, true);
                    send_response_amx(evnum, "", "Master Panel Offline or Not Responding");
                    connectionLostNotified = true;
                    break;
            }
        }

        public bool MorleyQuickPanelStatus(byte panelID)
        {
            byte[] command = new byte[6];
            command[0] = QUICK_STATUS_COMMAND;  // Quick Status Request (16)
            command[1] = 0;
            command[2] = 0;
            command[3] = 0;
            command[4] = 0;
            command[5] = 0;

            bool success = DoTwoWayCommand(panelID, command);
            return success;
        }

        public bool MorleyGetDeviceStatus(byte panelId, byte loop, byte deviceAddress)
        {
            byte[] command;
            enmMorleyDeviceDetailCode deviceDetailCode;

            deviceDetailCode = enmMorleyDeviceDetailCode.Device_Status; // Get Device Status only

            command = new byte[5];

            command[0] = ASK_DEVICE_STATE_COMMAND; // Ask Device Status identifier
            command[1] = loop;
            command[2] = deviceAddress;
            command[3] = 1;
            command[4] = (byte)deviceDetailCode;

            System.Diagnostics.Debug.WriteLine("tx: ASK_DEVICE_STATE_COMMAND");

            bool success = DoTwoWayCommand(panelId, command);
            return success;
        }


        public void MorleyDetailedAlarmInfo(byte panelId, byte alarmNumber)
        {
            byte[] command = new byte[2];
            command[0] = DETAILED_ALARM_COMMAND;  // Detailed Alarm Info Request (18)
            command[1] = alarmNumber;

            DoTwoWayCommand(panelId, command);
        }

        private bool DoTwoWayCommand(byte panelId, byte[] commandCode)
        {
            try
            {
                int commandLen = commandCode.Length;

                // Build message: Header + PanelId + SourceID + Command + ChecksumHigh + ChecksumLow + End
                byte[] message = new byte[commandLen + 6];

                // Build Message Header
                message[0] = MORLEY_HEADER_TWOWAY;  // Message Header (241)
                message[1] = panelId;                // Panel ID
                message[2] = SOURCE_ID;              // Sender ID (3)

                // Copy Command bytes to the Message bytes
                for (int i = 0; i < commandLen; i++)
                {
                    message[i + 3] = commandCode[i];
                }

                // Prepare for checksum and end marker
                int checksumIndex = commandLen + 3;
                message[checksumIndex] = 0;      // Checksum High (placeholder)
                message[checksumIndex + 1] = 0;  // Checksum Low (placeholder)
                message[checksumIndex + 2] = MORLEY_MSG_END;  // End of Message (253)

                // Calculate Checksum
                CalcMorleyCheckSum(message, out message[checksumIndex], out message[checksumIndex + 1]);

                // Convert special bytes (escape bytes >= 240)
                byte[] finalMessage = ConvertToSpecialBytes(message);

                // Send to Panel
                if (serialport.IsOpen)
                {
                    serialport.Write(finalMessage, 0, finalMessage.Length);

                    Console.WriteLine("Sent command: " + BitConverter.ToString(finalMessage));
                    base.NotifyClient("Sent command: " + BitConverter.ToString(finalMessage), false);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in DoTwoWayCommand: " + ex.Message);
                base.NotifyClient("Error in DoTwoWayCommand: " + ex.Message, false);
                return false;
            }
        }

        private void CalcMorleyCheckSum(byte[] message, out byte highChecksum, out byte lowChecksum)
        {
            long sum = 0;

            // Exclude Message Header (0), Checksum High, Checksum Low and End of message
            // Sum from index 1 to (length - 3)
            int dataLen = message.Length - 3;
            for (int i = 1; i < dataLen; i++)
            {
                sum += message[i];
            }

            // CheckSum High = Sum / 256
            highChecksum = GetHighByte(sum);

            // CheckSum Low = Sum % 256
            lowChecksum = GetLowByte(sum);
        }

        private byte GetHighByte(long number)
        {
            return (byte)(number / 256);
        }

        private byte GetLowByte(long number)
        {
            return (byte)(number % 256);
        }

        private byte[] ConvertToSpecialBytes(byte[] message)
        {
            List<byte> temp = new List<byte>();
            int oldLen = message.Length;

            // Copy first byte (header), which is not expanded into special chars
            temp.Add(message[0]);

            // Process all bytes except first and last
            for (int i = 1; i < oldLen - 1; i++)
            {
                if (message[i] >= MORLEY_SPECIAL_BYTE)
                {
                    // Split byte >= 240 into two bytes:
                    // First byte = 240 (escape marker)
                    // Second byte = original - 240
                    temp.Add(MORLEY_SPECIAL_BYTE);
                    temp.Add((byte)(message[i] - MORLEY_SPECIAL_BYTE));
                }
                else
                {
                    temp.Add(message[i]);
                }
            }

            // Copy the end of message char without expanding it
            temp.Add(message[oldLen - 1]);

            return temp.ToArray();
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                SerialPort sp = (SerialPort)sender;

                while (sp.BytesToRead > 0)
                {
                    int b = sp.ReadByte();
                    receiveBuffer.Add((byte)b);

                    Console.WriteLine($"Received byte: {b} (0x{b:X2})");

                    // Detect message boundaries
                    if (b == MORLEY_HEADER_RESPONSE)
                    {
                        Console.WriteLine("*** MORLEY RESPONSE HEADER (250) ***");
                        base.NotifyClient("*** MORLEY RESPONSE HEADER DETECTED ***", false);
                        receiveBuffer.Clear();
                        receiveBuffer.Add((byte)b);
                    }
                    else if (b == MORLEY_MSG_END)
                    {
                        Console.WriteLine("*** MESSAGE END (253) ***");
                        ProcessReceivedMessage(receiveBuffer.ToArray());
                        receiveBuffer.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in DataReceived: " + ex.Message);
                base.NotifyClient("Error in DataReceived: " + ex.Message, false);
            }
        }

        private void ProcessReceivedMessage(byte[] message)
        {
            string msgInfo = "Complete message received (" + message.Length + " bytes): " +
                           BitConverter.ToString(message);
            //Console.WriteLine(msgInfo);
            base.NotifyClient(msgInfo, false);

            // Decode special bytes if present
            byte[] decoded = DecodeSpecialBytes(message);
            string decodedInfo = "Decoded message: " + BitConverter.ToString(decoded);
            //Console.WriteLine(decodedInfo);

            // Show ASCII representation
            string asciiInfo = "ASCII: " + GetAsciiRepresentation(decoded);
            //Console.WriteLine(asciiInfo);
            base.NotifyClient(asciiInfo, false);

            // ONLY set this AFTER confirming the message is valid
            OnValidResponseReceived();

            // Parse the message structure
            ParseMorleyMessage(decoded);
        }
        private void OnValidResponseReceived()
        {
            lastSuccessfulResponse = DateTime.Now;
            consecutiveFailures = 0;
            if (connectionLostNotified)
            {
                int evnum = CSAMXSingleton.CS.MakeInputNumber(g_bytMasterPanelID, 0, 9, 15, false);   // clear the event from AMX
                send_response_amx(evnum, "", "Master Panel Offline or Not Responding");
                connectionLostNotified = false;
            }
            base.NotifyClient($"Valid response at {DateTime.Now:HH:mm:ss.fff}");
        }

        private void SendDeviceStatusToAMX1(MorleyEventPriority eventPriority, MorleyEventNature eventNature, MorleyDetectorType detectorType, ref int p1)
        {

            int amx1StatusIPNumber = 0;     // Used for Panel events only

            bool isFireDetector = (detectorType == MorleyDetectorType.CallPoint) ||
                                 (detectorType == MorleyDetectorType.HeatDetector) ||
                                 (detectorType == MorleyDetectorType.IonisationDetector) ||
                                 (detectorType == MorleyDetectorType.OpticalDetector) ||
                                 (detectorType == MorleyDetectorType.TestBox);

            // First try Event Nature
            switch (eventNature)
            {
                case MorleyEventNature.PreAlarmSignal:
                    p1 = 2;
                    break;

                case MorleyEventNature.DetectorDisabled:
                    p1 = 4;
                    break;

                case MorleyEventNature.DetectorContaminated:
                    p1 = 10;
                    break;

                case MorleyEventNature.ZonePartiallyDisabled:
                case MorleyEventNature.ZoneTotallyDisabled:
                    p1 = 15;
                    detectorType = MorleyDetectorType.NoDetector;
                    break;

                case MorleyEventNature.ProblemWithSounderCircuit:
                    p1 = 15;
                    detectorType = MorleyDetectorType.NoDetector;
                    break;

                default:
                    // Get it from Event Priority
                    switch (eventPriority)
                    {
                        case MorleyEventPriority.FullFire:
                        case MorleyEventPriority.FireAlert:
                        case MorleyEventPriority.UnlocatedFire:
                            // Based on Device Type
                            if (isFireDetector)
                                p1 = 0;
                            else
                                p1 = 3;
                            break;

                        case MorleyEventPriority.BombAlert:
                        case MorleyEventPriority.Security:
                            p1 = 1;
                            break;

                        case MorleyEventPriority.DeviceDisabled:
                            p1 = 4;
                            break;

                        case MorleyEventPriority.PreAlarm:
                            p1 = 2;
                            break;

                        case MorleyEventPriority.SeriousFault:
                        case MorleyEventPriority.MinorFault:
                            p1 = 8;
                            break;

                        default:
                            // Check for PassAll flag (assuming g_bytNWMPassAll equivalent)
                            if (eventPriority != MorleyEventPriority.DayModeAlarm)
                            {
                                p1 = 4;
                            }
                            break;
                    }
                    break;
            }
        }

        private string GetAsciiRepresentation(byte[] data)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (byte b in data)
            {
                if (b >= 32 && b <= 126) // Printable ASCII
                {
                    sb.Append((char)b);
                }
                else
                {
                    sb.Append($"[{b:X2}]"); // Show hex for non-printable
                }
            }
            return sb.ToString();
        }

        private void ParseMorleyMessage(byte[] decoded)
        {
            if (decoded.Length < 5) return;

            try
            {
                Console.WriteLine("=== Message Parse ===");
                Console.WriteLine($"Header: {decoded[0]} (0x{decoded[0]:X2})");
                Console.WriteLine($"Source: {decoded[1]}");
                Console.WriteLine($"Panel ID (Dest): {decoded[2]}");  // This is the actual Panel ID
                Console.WriteLine($"Command/Response: {decoded[3]} (0x{decoded[3]:X2})");

                // Parse based on response identifier (index 3)
                switch (decoded[MORLEY_MSG_IDENT_INDX])
                {
                    case QUICK_STATUS_RESPONSE:  // 17 (0x11)
                        ParseQuickStatusResponse(decoded);
                        break;

                    case DETAILED_ALARM_RESPONSE:  // 21 (0x15)
                        ParseDetailedAlarmResponse(decoded, false);
                        break;

                    case ASK_DEVICE_STATE_RESPONSE:  // 63 (0x3F)
                        ParseDeviceStatusResponse(decoded);
                        break;

                    default:
                        Console.WriteLine($"Unrecognised Response Identifier: {decoded[MORLEY_MSG_IDENT_INDX]}");
                        break;
                }

                Console.WriteLine("====================");

            }
            catch (Exception ex)
            {
                Console.WriteLine("Error parsing message: " + ex.Message);
            }
        }

        private void ParseDeviceStatusResponse(byte[] response)
        {
            if (response.Length < 5) return;

            try
            {
                Console.WriteLine("--- Device Status Response ---");

                byte panelID = response[2];
                byte newStates = response[4];  // Panel status bitset

                Console.WriteLine($"Panel ID: {panelID}");
                Console.WriteLine($"Status Bitset: {newStates} (0x{newStates:X2}, Binary: {Convert.ToString(newStates, 2).PadLeft(8, '0')})");

                // Check if status has changed
                if (newStates != previousPanelStatusBitset)
                {
                    byte diffs = (byte)(newStates ^ previousPanelStatusBitset);
                    Console.WriteLine($">>> STATUS CHANGED! Diff: {diffs} (0x{diffs:X2})");
                    Console.WriteLine($"Previous: {previousPanelStatusBitset} (0x{previousPanelStatusBitset:X2})");
                    Console.WriteLine($"New:      {newStates} (0x{newStates:X2})");
                    int tInputNumber = 0;
                    string tInputText = "";
                    // Check each bit for changes
                    byte bit = 1;
                    for (int n = 0; n < 8; n++)
                    {
                        if ((diffs & bit) != 0)
                        {
                            bool isOn = (newStates & bit) != 0;


                            // Get input number and text based on bit position (matching VB6)
                            switch (n)
                            {
                                case 0:
                                    tInputNumber = 1;
                                    tInputText = "Buzzer Muted";
                                    break;
                                case 1:
                                    tInputNumber = 2;
                                    tInputText = "Alarms Silenced";
                                    break;
                                case 2:
                                    tInputNumber = 3;
                                    tInputText = "General Disablement";
                                    break;
                                case 3:
                                    tInputNumber = 4;
                                    tInputText = "Panel in Fire";
                                    break;
                                case 4:
                                    tInputNumber = 5;
                                    tInputText = "Panel in Fault";
                                    break;
                                case 5:
                                    tInputNumber = 6;
                                    tInputText = "Panel in Pre-alarm";
                                    break;
                                case 6:
                                    tInputNumber = 7;
                                    tInputText = "Panel in Test Mode";
                                    break;
                                case 7:
                                    tInputNumber = 8;
                                    tInputText = "Panel in Delay Mode Period";
                                    break;
                                default:
                                    tInputNumber = 255;
                                    tInputText = "???";
                                    break;
                            }

                            string onOff = isOn ? "ON" : "OFF";
                            Console.WriteLine($"  Input #{tInputNumber}: {tInputText} = {onOff}");

                            //                            SendDeviceStatusToAMX1((MorleyEventPriority)response[5], (MorleyEventNature)response[56], (MorleyDetectorType)response[11]);

                            int evnum = CSAMXSingleton.CS.MakeInputNumber(panelID, 0, tInputNumber, 15, isOn);
                            send_response_amx(evnum, "", tInputText);

                            string notifyMsg = $"Panel {panelID} Input {tInputNumber}: {tInputText} = {onOff}";
                            base.NotifyClient(notifyMsg, false);

                            // Special notifications for critical states
                            if (n == 3 && isOn) // Panel in Fire
                            {
                                base.NotifyClient("********** PANEL IN FIRE STATE **********", false);
                            }
                            else if (n == 4 && isOn) // Panel in Fault
                            {
                                base.NotifyClient("********** PANEL IN FAULT STATE **********", false);
                            }
                            else if (n == 6 && isOn) // Test Mode
                            {
                                base.NotifyClient(">>> Panel entered TEST MODE", false);
                            }
                        }

                        bit = (byte)((bit << 1) & 0xFF);  // Shift left, mask to byte
                    }

                    previousPanelStatusBitset = newStates;
                }
                else
                {
                    Console.WriteLine("No status change from previous poll");
                }

                // Always display current status bits
                Console.WriteLine("Current Panel Status:");
                DisplayPanelStatusBits(newStates);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ParseDeviceStatusResponse: {ex.Message}");
            }
        }


        private string GetPanelStatusText(int bitPosition)
        {
            switch (bitPosition)
            {
                case 0: return "Buzzer Muted";
                case 1: return "Alarms Silenced";
                case 2: return "General Disablement";
                case 3: return "Panel in Fire";
                case 4: return "Panel in Fault";
                case 5: return "Panel in Pre-alarm";
                case 6: return "Panel in Test Mode";
                case 7: return "Panel in Delay Mode Period";
                default: return "Unknown";
            }
        }

        private void DisplayPanelStatusBits(byte statusBitset)
        {
            bool anySet = false;
            for (int n = 0; n < 8; n++)
            {
                byte bit = (byte)(1 << n);
                bool isSet = (statusBitset & bit) != 0;
                if (isSet)
                {
                    Console.WriteLine($"  ✓ Bit {n}: {GetPanelStatusText(n)}");
                    anySet = true;
                }
            }
            if (!anySet)
            {
                Console.WriteLine("  (All status bits clear - panel normal)");
            }
        }
        private void ParseQuickStatusResponse(byte[] response)
        {
            Console.WriteLine("--- Quick Status Response ---");

            byte panelID = response[2];
            int eventCount = response[4];      // Number of alarms/faults
            int priority = response[5];         // Priority level

            Console.WriteLine($"Panel ID: {panelID}");
            Console.WriteLine($"Event Count: {eventCount}");
            Console.WriteLine($"Priority: {priority}");



            base.NotifyClient($"Quick Status: {eventCount} events, Priority {priority}", false);

            // If there are alarms, request detailed info for each
            if (eventCount > 0 && priority > 0)
            {
                Console.WriteLine($"Requesting detailed info for {eventCount} alarm(s)...");
                waitingForDetailedResponse = true;  // Stop polling until we get responses

                for (byte alarmNum = 1; alarmNum <= eventCount; alarmNum++)
                {
                    MorleyDetailedAlarmInfo(panelID, alarmNum);
                    Thread.Sleep(200); // Longer delay between requests
                }
                waitingForDetailedResponse = false;  // No alarms, resume polling
            }
            else
            {
                waitingForDetailedResponse = false;  // No alarms, resume polling
            }

            int p1 = 15;
            int p2 = response[2]; // Source Panel ID
            int p3 = response[7]; // Loop number
            int p4 = response[9]; // Device Address

            MorleyGetDeviceStatus((byte)p2, (byte)p3, (byte)p4);
        }

        private void send_response_amx(int evnum, string message1, string message2, string message3 = "")
        {
            string friendlymessage = message2 + (message3.Length > 0 ? (" " + message3) : "");

            // Signal the event back to the main service, so that it can be logged
            this.NotifyClient(friendlymessage, false);

            CSAMXSingleton.CS.SendAlarmToAMX(evnum, message2, message1, message3);
            CSAMXSingleton.CS.FlushMessages();
        }

        private void ParseDetailedAlarmResponse(byte[] response, bool blnFromTestForm)
        {
            if (response.Length < 56) return;

            Console.WriteLine("--- Detailed Alarm Response ---");

            byte panelID = response[2];                 // Use index 2 like VB6
            byte loop = response[7];                    // Loop number (0 if panel event)
            byte zoneNumber = response[8];              // Zone number
            byte deviceAddress = response[9];           // Device address (0 if panel event)
            byte analogueValue = response[10];          // Analogue value
            byte detectorType = response[11];           // Detector type
            byte eventType = response[52];              // Event nature code
            byte originatingPanelID = response[53];     // Panel ID where event originated
            byte subAddress = response[55];             // Sub address




            MorleyEventNature? morleyEvent = null;  // Nullable enum
            if (Enum.IsDefined(typeof(MorleyEventNature), (int)eventType))
            {
                morleyEvent = (MorleyEventNature)eventType;

            }
            else
            {
                // Unknown / unsupported event from panel
                return; // or log / handle gracefully
            }

            // Extract alarm text (bytes 12-51, 40 characters)
            alarmText = "";
            for (int i = 12; i <= 51; i++)
            {
                if (response[i] != 0)
                    alarmText += (char)response[i];
            }
            alarmText = alarmText.Trim();

            Console.WriteLine($"Panel ID: {panelID}");
            Console.WriteLine($"Loop: {loop}");
            Console.WriteLine($"Zone: {zoneNumber}");
            Console.WriteLine($"Device Address: {deviceAddress}");
            Console.WriteLine($"Analogue Value: {analogueValue}");
            Console.WriteLine($"Detector Type: {detectorType}");
            Console.WriteLine($"Event Type: {eventType} ({GetEventTypeName(eventType)})");
            Console.WriteLine($"Originating Panel: {originatingPanelID}");
            Console.WriteLine($"Sub Address: {subAddress}");
            Console.WriteLine($"Alarm Text: '{alarmText}'");

            // The VB6 format: Panel-Loop-Device/EventType
            string eventKey = $"{panelID}-{loop}-{deviceAddress}/{eventType}";
            Console.WriteLine($"Event Key (VB6 format): {eventKey}");

            string notifyMsg = $"ALARM [{eventKey}]: Zone {zoneNumber}, " +
                             $"Type: {GetEventTypeName(eventType)}, Text: {alarmText}";
            base.NotifyClient(notifyMsg, false);

            // Log fire alarms specially
            if ((EnmMorleyEventNature)eventType == EnmMorleyEventNature.Fire_Alarm_Signal) // Fire_Alarm_Signal
            {
                Console.WriteLine("********** FIRE ALARM **********");
                base.NotifyClient("********** FIRE ALARM **********", false);
            }
            else if ((EnmMorleyEventNature)eventType == EnmMorleyEventNature.Panel_Reset) // Panel_Reset
            {
                Console.WriteLine("********** PANEL RESET **********");
                base.NotifyClient("********** PANEL RESET **********", false);
            }

            int p1 = 15;
            int p2 = response[2]; // Source Panel ID
            int p3 = response[7]; // Loop number
            int p4 = response[9]; // Device Address
            bool on = true;
            int evnum = 0;
            string message2 = "";

            if (originatingPanelID > 0)
            {
                // if from Panel - Add to AlarmQ
                // if from TestBox - use m_objTestAlarms
                if (blnFromTestForm)
                {
                    //               if (m_objTestAlarms == null)
                    //               {
                    //                   m_objTestAlarms = new MorleyDeviceAlarm();
                    //               }

                    //               objDeviceAlarm = m_objTestAlarms;
                    //               objDeviceAlarm.Initialise(
                    //                   bytOrginatingPanelID,
                    //                   bytLoop,
                    //                   bytDeviceAddress,
                    //                   (byte)enmEventType
                    //               );
                    //               objDeviceAlarm.FromTestBox = true;
                }
                else
                {

                    // James 28/07/2006
                    if (morleyEvent == MorleyEventNature.ZoneTotallyDisabled || morleyEvent == MorleyEventNature.ZonePartiallyDisabled)
                    {
                        p4 = (byte)(response[8] + 30);

                        // **James 060214
                        AddToZoneArray(p4);
                        // *****
                    }
                    else if (morleyEvent == MorleyEventNature.ProblemWithSounderCircuit)
                    {

                        if (alarmText.ToLower().Contains("sounder a") || alarmText.Contains("soundera"))
                            p4 = 1;
                        else if (alarmText.ToLower().Contains("sounder b") || alarmText.Contains("sounderb"))
                            p4 = 2;
                        else if (alarmText.ToLower().Contains("sounder c") || alarmText.Contains("sounderc"))
                            p4 = 3;
                        else if (alarmText.ToLower().Contains("sounder d") || alarmText.Contains("sounderd"))
                            p4 = 4;
                        else
                            p4 = 0;

                        p4 = (byte)(p4 + 20);
                    }

                }
            }

            // Decode status bits from position 4
            byte statusByte = response[4];
            if ((statusByte & 0x01) == 0)
            {
                on = false; // Cleared
            }
            else
            {
                on = true; // Active
            }
            if (alarmText != null)
            {
                message2 = alarmText;
            }

            alarmText = GetMorleyDeviceType((MorleyDetectorType)response[11]);
            //            SendDeviceStatusToAMX1((MorleyEventPriority)response[5], (MorleyEventNature)response[56], (MorleyDetectorType)response[11], ref p1);

            if (p4 > 0)
            {
                evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1, on);
                send_response_amx(evnum, alarmText, message2);
            }

            // Resume polling after receiving detailed response
            waitingForDetailedResponse = false;
        }

        private string GetEventTypeName(byte eventType)
        {
            switch (eventType)
            {
                case 0: return "Panel Reset";
                case 1: return "Fire Alarm Signal";
                case 2: return "Fault";
                case 4: return "Supervisory";
                case 8: return "Test";
                case 16: return "Zone Disabled";
                case 32: return "Sounder Problem";
                default: return $"Unknown ({eventType})";
            }
        }

        private byte[] DecodeSpecialBytes(byte[] message)
        {
            List<byte> decoded = new List<byte>();

            for (int i = 0; i < message.Length; i++)
            {
                if (message[i] == MORLEY_SPECIAL_BYTE && i + 1 < message.Length)
                {
                    // Reconstruct original byte
                    decoded.Add((byte)(message[i + 1] + MORLEY_SPECIAL_BYTE));
                    i++; // Skip next byte as it's part of the escape sequence
                }
                else
                {
                    decoded.Add(message[i]);
                }
            }

            return decoded.ToArray();
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
            send_message(ActionType.kEVACTUATE, passedvalues);
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
            string[] parts = passedvalues.Split(',');

            int node = 1, loop = 0, zone = 0, device = 0, giDomainNumber = 0, inputtype = 0;

            if (parts.Length > 0) int.TryParse(parts[0], out node);
            if (parts.Length > 1) int.TryParse(parts[1], out loop);
            if (parts.Length > 2) int.TryParse(parts[2], out zone);
            if (parts.Length > 3) int.TryParse(parts[3], out device);

            DateTime now = DateTime.Now;

            int sHour = now.Hour;
            int sMinute = now.Minute;
            int sSecond = now.Second;

            int sYear = int.Parse(now.ToString("yy"));   // Two-digit year
            int sMonth = int.Parse(now.ToString("MM"));  // Two-digit month
            int sDay = int.Parse(now.ToString("dd"));    // Two-digit day
            int sDayWeek = ((int)now.DayOfWeek + 6) % 7 + 1;// Sunday = 1, Monday = 2, etc.
            bool on = true;

            string text = action.ToString();

            switch (action)
            {
                case ActionType.kEVACTUATE:
                    MorleyEvacuateDetailed((byte)node);
                    //MorleyEvacuate((byte)node);
                    break;

                case ActionType.kRESET:
                    MorleyReset((byte)node);
                    break;

                case ActionType.kSILENCE:
                    MorleyReset((byte)node);
                    break;

                case ActionType.kMUTEBUZZERS:
                    MorleyMute((byte)node);
                    break;

                case ActionType.kDISABLEDEVICE:
                    MorleyIsolateDevice((byte)node, (byte)loop, (byte)device, true);
                    break;

                case ActionType.kENABLEDEVICE:
                    MorleyIsolateDevice((byte)node, (byte)loop, (byte)device, false);
                    break;

                case ActionType.kDISABLEZONE:
                    MorleyIsolateZone((byte)node, (byte)loop, true);
                    break;

                case ActionType.kENABLEZONE:
                    MorleyIsolateZone((byte)node, (byte)loop, false);
                    break;
            }
        }

        private void AddToZoneArray(int deviceAddress)
        {
            try
            {
                // Check if the deviceAddress already exists
                if (!garyZoneArray.Contains(deviceAddress))
                {
                    garyZoneArray.Add(deviceAddress);

                    base.NotifyClient($"Zone Array add to array : Count {garyZoneArray.Count}  Device Address : {deviceAddress}");
                }
            }
            catch (Exception ex)
            {
                // In VB6, Err.Number = 9 was handled (index out of bounds)
                // In C#, List.Add never fails for this, but we catch any unexpected exceptions
                base.NotifyClient($"Error - {ex.HResult} ({ex.Message}) in procedure AddToZoneArray");
            }
        }

        public static string GetMorleyDeviceType(MorleyDetectorType detectorType)
        {
            switch (detectorType)
            {
                case MorleyDetectorType.TestBox:
                    return "Test Box";
                case MorleyDetectorType.ApolloShopUnit:
                    return "Apollo Shop Unit";
                case MorleyDetectorType.SounderDevice:
                    return "Sounder";
                case MorleyDetectorType.IOUnit:
                    return "I/O Unit";
                case MorleyDetectorType.IonisationDetector:
                    return "Ionisation Detector";
                case MorleyDetectorType.ZoneMonitor:
                    return "Zone Monitor";
                case MorleyDetectorType.OpticalDetector:
                    return "Optical Detector";
                case MorleyDetectorType.HeatDetector:
                    return "Heat Detector";
                case MorleyDetectorType.CallPoint:
                    return "Call Point";
                case MorleyDetectorType.RelayDetector:
                    return "Relay";
                case MorleyDetectorType.AnyNonSpecificSensor:
                    return "Non-Specific Sensor";
                case MorleyDetectorType.Mefs8WayInput:
                    return "Mefs 8-Way Input";
                case MorleyDetectorType.MiniRepeater:
                    return "Mini Repeater";
                case MorleyDetectorType.NoDetector:
                    return "No Detector";
                default:
                    Console.WriteLine("Unknown Morley Device Type: " + (int)detectorType);
                    return "Unknown Device";
            }
        }

        public void MorleyIsolateDevice(byte bytPanelID, byte bytLoopID, byte bytDeviceID, bool blnIsolate)
        {
            byte[] bytCommand;
            byte bytIsolate;

            if (blnIsolate)
            {
                bytIsolate = 100;
            }
            else
            {
                bytIsolate = 101;
            }

            // C# arrays are zero-based, so create array of size 8 (indices 12-19 in VB6)
            bytCommand = new byte[20]; // Create array large enough to use indices 12-19

            bytCommand[12] = bytIsolate;     // Isolate/De-Isolate
            bytCommand[13] = 4;              // Include Call points
            bytCommand[14] = 0;              // From Zone
            bytCommand[15] = 0;              // To Zone
            bytCommand[16] = bytPanelID;     // Panel
            bytCommand[17] = bytLoopID;      // Loop
            bytCommand[18] = bytDeviceID;    // Device
            bytCommand[19] = 0;              // Applies to Loop or Device

            bool success = DoDetailedBroadCommand(ref bytCommand);
            if (success)
            {
                bytCommand = null; // Equivalent to Erase in VB6

                // Now do the echo back
                if (g_booIsolationEcho)
                {
                    EchoMorleyIsolation(bytPanelID, bytLoopID, bytDeviceID, blnIsolate);
                }
            }
        }

        public void MorleyIsolateZone(byte bytPanelID,byte bytZoneID,bool blnIsolate)
        {
            byte[] bytCommand;
            byte bytIsolate;

            if (blnIsolate)
            {
                bytIsolate = 100;    // Asc("d")
            }
            else
            {
                bytIsolate = 101;    // Asc("e")
            }

            // C# arrays are zero-based, so create array large enough to use indices 12-19
            bytCommand = new byte[20];

            bytCommand[12] = bytIsolate;     // Isolate/De-Isolate
            bytCommand[13] = 4;              // Include Call points
            bytCommand[14] = bytZoneID;      // From Zone
            bytCommand[15] = bytZoneID;      // To Zone
            bytCommand[16] = bytPanelID;     // Panel
            bytCommand[17] = 0;              // Loop
            bytCommand[18] = 0;              // Device
            bytCommand[19] = 1;              // Applies to Zone

            DoDetailedBroadCommand(ref bytCommand);

            bytCommand = null; // Equivalent to Erase
        }

        public void MorleyIsolateSounderRelay(byte bytPanelID,                                      string strSounderRelay,                                      bool blnIsolate)
        {
            byte[] bytCommand;
            byte bytIsolate;

            if (blnIsolate)
            {
                bytIsolate = 100;    // Asc("d")
            }
            else
            {
                bytIsolate = 101;    // Asc("e")
            }

            // C# arrays are zero-based, so create array large enough to use indices 12-19
            bytCommand = new byte[20];

            bytCommand[12] = bytIsolate;     // Isolate/De-Isolate
            bytCommand[13] = 0;              //
            bytCommand[14] = 0;              // From Zone
            bytCommand[15] = 0;              // To Zone
            bytCommand[16] = bytPanelID;     // Panel

            base.NotifyClient("Isolate panel ID: " + bytPanelID.ToString());   // !!!!!!!!!!!

            bytCommand[17] = 0;              // Loop
            bytCommand[18] = 0;              // Device

            if (strSounderRelay.ToUpper() == "RELAY")
            {
                bytCommand[19] = 3;          // Applies to Relays
            }
            else
            {
                bytCommand[19] = 2;          // Applies to Sounders
            }

            DoDetailedBroadCommand(ref bytCommand);

            bytCommand = null; // Equivalent to Erase
        }

        public void MorleyMute(byte bytPanelID)
        {
            DoSimpleBroadCommand(2, bytPanelID);
        }
        public void MorleyEvacuate(byte bytPanelID)
        {
            DoSimpleBroadCommand(3, bytPanelID);
        }
        public void MorleySilenceAlarms(byte bytPanelID)
        {
            DoSimpleBroadCommand(5, bytPanelID);
        }
        public void MorleyReset(byte bytPanelID)
        {
            DoSimpleBroadCommand(6, bytPanelID);
        }
        public void MorleyResoundAlarms(byte bytPanelID)
        {
            DoSimpleBroadCommand(9, bytPanelID);
        }
        public void MorleyEvacuateDetailed(byte bytPanelID)
        {
            byte[] bytCommand;

            // C# arrays are zero-based, so create array large enough to use indices 12-19
            bytCommand = new byte[20];
            bytCommand[12] = (byte)Convert.ToChar("c");  // More explicit conversion
            bytCommand[13] = 0;              // not used
            bytCommand[14] = 3;              // 3 = evacuate
            bytCommand[15] = 0;              // used for class change
            bytCommand[16] = bytPanelID;     // Panel
            bytCommand[17] = 0;              // Loop
            bytCommand[18] = 0;              // Device
            bytCommand[19] = 6;              // Applies to controls

            DoDetailedBroadCommand(ref bytCommand);

            bytCommand = null; // Equivalent to Erase
        }
        private bool DoSimpleBroadCommand(byte bytCommandCode, byte bytPanelID)
        {
            byte[] bytMsg;

            bytMsg = new byte[7]; // 0 to 6

            // Build Message
            bytMsg[0] = MORLEY_HEADER_BROADCAST;  // Message Header
            bytMsg[1] = bytPanelID;               // Destination ID
            bytMsg[2] = 0;                        // Sender ID
            bytMsg[3] = bytCommandCode;           // Command
            bytMsg[4] = 0;                        // Checksum - High byte
            bytMsg[5] = 0;                        // Checksum - Low byte
            bytMsg[6] = MORLEY_MSG_END;           // End of Message

            // Calculate Checksum
            CalcMorleyCheckSum(bytMsg, out bytMsg[4], out bytMsg[5]);

            // Check and convert special bytes
            ConvertToSpecialBytes(bytMsg);

            // Send Twice - Panel will not return any indication the message is successful
            //AddToCommandQ(bytMsg, Broadcast_Command, High_Priorty);

            bool success = serialsend(bytMsg);
            bytMsg = null; // Equivalent to Erase
            return success;
        }
        private bool DoDetailedBroadCommand(ref byte[] bytCommandCode)
        {
            byte[] bytMsg;

            bytMsg = new byte[23]; // 0 to 22

            // Build Message
            bytMsg[0] = MORLEY_HEADER_BROADCAST;  // Message Header
            bytMsg[1] = 0;                        // Destination ID
            bytMsg[2] = 0;                        // Sender ID
            bytMsg[3] = 7;                        // Command

            // 16 bytes of Data bytes 4 to 19
            bytMsg[4] = 0;                        // Reserved
            bytMsg[5] = 0;                        // Reserved
            bytMsg[6] = 0;                        // Reserved
            bytMsg[7] = 0;                        // Reserved
            bytMsg[8] = 0;                        // Reserved
            bytMsg[9] = 0;                        // Reserved
            bytMsg[10] = 0;                       // Reserved
            bytMsg[11] = 0;                       // Reserved

            bytMsg[12] = bytCommandCode[12];      // Mode
            bytMsg[13] = bytCommandCode[13];      // Method
            bytMsg[14] = bytCommandCode[14];      // from Zone
            bytMsg[15] = bytCommandCode[15];      // To Zone
            bytMsg[16] = bytCommandCode[16];      // Panel ID
            bytMsg[17] = bytCommandCode[17];      // Data
            bytMsg[18] = bytCommandCode[18];      // Data
            bytMsg[19] = bytCommandCode[19];      // Applies to

            bytMsg[20] = 0;                       // Checksum - High byte
            bytMsg[21] = 0;                       // Checksum - Low byte
            bytMsg[22] = MORLEY_MSG_END;          // End of Message

            // MsgBox bytMsg(12)
            // Calculate Checksum
            CalcMorleyCheckSum(bytMsg, out bytMsg[20], out bytMsg[21]);

            // Check and convert special bytes
            ConvertToSpecialBytes(bytMsg);

            // string tStr = "";
            // for (int n = 0; n <= 22; n++)
            // {
            //     tStr = tStr + (char)bytMsg[n];
            // }
            // DebugPrintString(tStr, false);
            // DebugPrintString(tStr, true);

            // Send Twice - Panel will not return any indication the message is successful
            //AddToCommandQ(bytMsg, Broadcast_Command, High_Priorty);

            bool success = serialsend(bytMsg);
            bytMsg = null; // Equivalent to Erase
            return success;
        }

        public void EchoMorleyIsolation(byte bytPanelID, byte bytLoopID, byte bytDeviceID, bool blnIsolate)
        {
            int NumIsols;

            try
            {
                NumIsols = 0;   // Count how many there are

                if (bytPanelID != 0)
                {
                    // Fake an "input" from the remote
                    //evNum = MakeInputNumber((int)bytPanelID, (int)bytLoopID, (int)bytDeviceID, 4);

                    int evnum = CSAMXSingleton.CS.MakeInputNumber(bytPanelID, bytLoopID, bytDeviceID, 15, blnIsolate);
                    //                    if (blnIsolate)
                    //                    {
                    //                        evNum = evNum + 0x80000000;
                    //                    }
                    send_response_amx(evnum, "", "");

                    if (blnIsolate == true)
                    {
                        // If intAmxInputType == 15 || intAmxInputType == 8
                        //   AddToEventQueue(evNum);
                        // End If
                    }
                    else
                    {
                        //   RemoveFromEventQueue(evNum);
                    }

                    // Signal alarm
                    //WriteNWMData(tStr, 1, evNum, DLL.Dat[0], ps, "", "", blnIsolate ? 1 : 0);
                    NumIsols = NumIsols + 1;

                    // Now send a fake isolation signal to log and isolation list

                    evnum = CSAMXSingleton.CS.MakeInputNumber(bytPanelID, bytLoopID, bytDeviceID, 4);
                    send_response_amx(evnum, "", "");
                    // Signal isolation
                    //WriteNWMData(tStr, 2, evNum, DLL.Dat[0], ps, "", "", blnIsolate ? 1 : 0);
                }

                if (NumIsols > 0)
                {
                    // Only queue data when some isols were found
                    //FlushAMX1Messages();
                }
            }
            catch (Exception ex)
            {
            }
        }
    }
}