using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Runtime.Remoting.Messaging;
using System.Threading;

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

        private SerialPort serialport;
        private const byte MASTER_PANEL_ID = 1;
        private const byte SOURCE_ID = 3; // PC/Host ID

        private List<byte> receiveBuffer = new List<byte>();
        private System.Timers.Timer pollTimer;
        private bool isPollingEnabled = true;

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
        public PanelMorleyZX(string baselogfolder, string identifier) : base(baselogfolder, identifier, "MaxMan", "MAX")
        {
            if (!String.IsNullOrEmpty(identifier))
            {
                heartbeat_timer = new Timer(heartbeat_timer_callback, this.Identifier, 1000, kHeartbeatDelaySeconds * 1000);
            }
        }
        public override void StartUp(int fakemode)
        {
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

            // Match VB6 settings exactly
            serialport.Encoding = System.Text.Encoding.GetEncoding(28591); // ISO-8859-1 for binary
            serialport.DtrEnable = false;
            serialport.RtsEnable = false;
            serialport.ReadBufferSize = 8000;
            serialport.WriteBufferSize = 200;
            serialport.ReadTimeout = SerialPort.InfiniteTimeout;
            serialport.WriteTimeout = SerialPort.InfiniteTimeout;
            serialport.ReceivedBytesThreshold = 1; // Match VB6 RThreshold = 1

            // Set up event handler BEFORE opening
            serialport.DataReceived += SerialPort_DataReceived;
            serialport.PinChanged += SerialPort_PinChanged;

            if (serialport.IsOpen)
            {
                serialport.Close();
            }

            Console.WriteLine("Attempting Open " + serialport.PortName);
            base.NotifyClient("Attempting Open " + serialport.PortName, false);

            try
            {
                serialport.Open();

                // Clear buffers after opening
                serialport.DiscardInBuffer();
                serialport.DiscardOutBuffer();

                Console.WriteLine("Successfully opened " + serialport.PortName);
                base.NotifyClient("Successfully opened " + serialport.PortName, false);

                // Give the port a moment to stabilize
                Thread.Sleep(100);

                // Send initial poll command
                MorleyQuickPanelStatus(MASTER_PANEL_ID);

                // Start continuous polling timer (VB6 uses 3000ms interval)
                StartPollingTimer();
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed To Open " + serialport.PortName + " " + e.ToString());
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

        private void PollTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (isPollingEnabled && serialport != null && serialport.IsOpen)
            {
                MorleyQuickPanelStatus(MASTER_PANEL_ID);
            }
        }

        private void SerialPort_PinChanged(object sender, SerialPinChangedEventArgs e)
        {
            // Handle pin changes if needed
        }

        public void MorleyQuickPanelStatus(byte panelID)
        {
            byte[] command = new byte[6];
            command[0] = QUICK_STATUS_COMMAND;  // Quick Status Request (16)
            command[1] = 0;
            command[2] = 0;
            command[3] = 0;
            command[4] = 0;
            command[5] = 0;

            DoTwoWayCommand(panelID, command);
        }

        private void DoTwoWayCommand(byte panelID, byte[] commandCode)
        {
            try
            {
                int commandLen = commandCode.Length;

                // Build message: Header + PanelID + SourceID + Command + ChecksumHigh + ChecksumLow + End
                byte[] message = new byte[commandLen + 6];

                // Build Message Header
                message[0] = MORLEY_HEADER_TWOWAY;  // Message Header (241)
                message[1] = panelID;                // Panel ID
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
                serialport.Write(finalMessage, 0, finalMessage.Length);

                Console.WriteLine("Sent command: " + BitConverter.ToString(finalMessage));
                base.NotifyClient("Sent command: " + BitConverter.ToString(finalMessage), false);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in DoTwoWayCommand: " + ex.Message);
                base.NotifyClient("Error in DoTwoWayCommand: " + ex.Message, false);
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
            Console.WriteLine(msgInfo);
            base.NotifyClient(msgInfo, false);

            // Decode special bytes if present
            byte[] decoded = DecodeSpecialBytes(message);
            string decodedInfo = "Decoded message: " + BitConverter.ToString(decoded);
            Console.WriteLine(decodedInfo);

            // Show ASCII representation
            string asciiInfo = "ASCII: " + GetAsciiRepresentation(decoded);
            Console.WriteLine(asciiInfo);
            base.NotifyClient(asciiInfo, false);

            // Parse the message structure
            ParseMorleyMessage(decoded);
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
                Console.WriteLine($"Source Panel: {decoded[1]}");
                Console.WriteLine($"Dest Panel: {decoded[2]}");
                Console.WriteLine($"Command/Response: {decoded[3]} (0x{decoded[3]:X2})");

                if (decoded.Length > 4)
                {
                    Console.WriteLine($"Data bytes: {BitConverter.ToString(decoded, 4, decoded.Length - 5)}");

                    // For Quick Status Response (0x11 is response to 0x10 quick status)
                    if (decoded[3] == 0x11 && decoded.Length >= 6)
                    {
                        Console.WriteLine($"Panel Status Byte: {decoded[4]} (0x{decoded[4]:X2})");
                        Console.WriteLine($"Panel Type: {decoded[5]} (0x{decoded[5]:X2})");

                        int p1 = 0;
                        int p2 = decoded[1]; // Source Panel ID
                        int p3 = decoded[2];
                        int p4 = decoded[3];
                        int evnum = 0;
                        string message2 = "";

                        // Decode status bits
                        byte statusByte = decoded[4];
                        Console.WriteLine("Status Flags:");
                        if ((statusByte & 0x01) != 0)
                        {
                            p1 = 15;
                            Console.WriteLine("  - Alarm Active");
                            message2 = "Alarm Active";
                        }
                        if ((statusByte & 0x02) != 0)
                        {
                            p1 = 4;
                            Console.WriteLine("  - Fault Active");
                            message2 = "Fault";
                        }
                        if ((statusByte & 0x04) != 0)
                        {
                            p1 = 8;
                            Console.WriteLine("  - Disabled");
                            message2 = "Disabled";
                        }
                        if ((statusByte & 0x08) != 0)
                        {
                            Console.WriteLine("  - Test Mode");
                            p1 = 15;
                            message2 = "Test Mode";
                        }
                        if ((statusByte & 0x10) != 0)
                        {
                            p1 = 15;
                            Console.WriteLine("  - Silenced");
                            message2 = "Silenced";
                        }

                        evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
                        send_response_amx(evnum, "", message2);
                    }
                }

                // Checksum validation
                int checksumIndex = decoded.Length - 3;
                if (checksumIndex > 0)
                {
                    Console.WriteLine($"Checksum High: {decoded[checksumIndex]} (0x{decoded[checksumIndex]:X2})");
                    Console.WriteLine($"Checksum Low: {decoded[checksumIndex + 1]} (0x{decoded[checksumIndex + 1]:X2})");
                }

                Console.WriteLine($"End Marker: {decoded[decoded.Length - 1]} (0x{decoded[decoded.Length - 1]:X2})");
                Console.WriteLine("====================");

            }
            catch (Exception ex)
            {
                Console.WriteLine("Error parsing message: " + ex.Message);
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
        public override void Analogue(string passedvalues)
        {
            throw new NotImplementedException();
        }

        public virtual void send_message(ActionType action, string passedvalues)
        {
        }
    }
}