using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;

namespace Drax360Service.Panels
{
    internal class PanelAdvanced : AbstractPanel
    {
        #region constants
        private const byte kackbyte = 0x06;
        private const byte kheartbeatdelayseconds = 5;
        private const byte kAdvancedStart = 254;
        private const byte kAdvanedEnd = 255;

        private const string adtUNKNOWNDEVICE = "Unknown Device";
        private const string adtAPOLLOIONISATION = "Ionisation Smoke";
        private const string adtAPOLLOOPTICAL = "Optical Smoke";
        private const string adtAPOLLOMULTISENSOR = "Multisensor";
        private const string adtAPOLLOHEAT = "Heat";
        private const string adtZONEMONITOR = "Zone Monitor";
        private const string adtCALLPOINT = "Call point";
        private const string adtTEMPERATURESENSOR = "Temperature Sensor";
        private const string adtVOLTS1 = "Volts";
        private const string adtVOLTS2 = "Volts";
        private const string adtVOLTS3 = "Volts";
        private const string adtSWITCH = "Switch";
        private const string adtSOUNDER = "Sounder";
        private const string adtMONITOREDRELAY = "Monitored Relay";
        private const string adtRELAY = "Relay";
        private const string adtMONITOR = "Monitor";
        private const string adtCURRENT = "Current";
        private const string adtCURRENT2 = "Current";
        private const string adtCO_FIRE = "Carbon Monoxide - Fire";
        private const string adtCO_GASSENSOR = "Carbon Monoxide - Gas Sensor";
        private const string adtFLAMEDETECTOR = "Flame Detector";
        private const string adtSWITCH_MONITORED = "Switch (Monitored)";
        private const string adtHOCHIKIIONISATION = "Ionisation Smoke";
        private const string adtHOCHIKIOPTICAL = "Optical Smoke";
        private const string adtHOCHIKIMULTISENSOR = "Multisensor";
        private const string adtHOCHIKIHEAT = "Heat";
        private const string adtDOUBLEADDRESS = "Double Address";
        private const string adtBEACON = "Beacon";
        private const string adtMULTIHEAT = "Multisensor Heat";
        private const string adtRATEOFRISEHEAT = "Rate of Rise Heat";
        private const string adtOPTICALSMOKE = "Optical Smoke";
        private const string adtFLAME = "Flame";
        private const string adtINPUT1 = "Input";
        private const string adtINPUT2 = "Input";
        #endregion

        #region private variables
        private bool AcknowledgeMessage = false;
        private  int AdvancedDestinationAddress = 0;
        private int AdvancedSourceAddress = 0;
        private int ControlPacketSequence = 0;
        int p1 = 0;
        int p2 = 0;
        int p3 = 0;
        int p4 = 0;
        int evnum = 0;
        string message2 = "";
        #endregion

        #region constructors
        public PanelAdvanced(string baselogfolder, string identifier) : base(baselogfolder, identifier, "AdvMan", "ADV")
        {
            if (!String.IsNullOrEmpty(identifier))
            {
                heartbeat_timer = new Timer(heartbeat_timer_callback, this.Identifier, 500, kHeartbeatDelaySeconds * 1000);
            }
        }
        #endregion

        #region public methods
        public override string FakeString
        {
            get
            {

                byte[] readbhyte = new byte[]
                {
                        254, 128, 0, 0, 26, 10, 19, 1, 1, 1, 0, 1, 0, 1, 4, 2, 83, 77, 79, 75, 69, 32, 49, 0,
                        10, 12, 1, 1, 3, 0, 1, 0, 1, 4, 2, 0, 10, 12, 1, 1, 4, 0, 1, 0, 1, 4, 6, 0,
                        10, 12, 1, 1, 5, 0, 1, 0, 1, 4, 21, 0, 10, 12, 1, 1, 6, 0, 1, 0, 1, 4, 11, 0,
                        240, 73, 214, 255, 254, 128, 0, 0, 26, 10, 19, 1, 1, 1, 0, 1, 0, 1, 4, 2, 83, 77, 79, 75,
                        69, 32, 49, 0, 10, 12, 1, 1, 3, 0, 1, 0, 1, 4, 2, 0, 10, 12, 1, 1, 4, 0, 1, 0,
                        1, 4, 6, 0, 10, 12, 1, 1, 5, 0, 1, 0, 1, 4, 21, 0, 10, 12, 1, 1, 6, 0, 1, 0,
                        1, 4, 11, 0, 240, 73, 214, 255
                };
                return Encoding.Default.GetString(readbhyte);
            }
        }
        public override void Parse(byte[] buffer)
        {
            base.Parse(buffer);

            int removebytes = 0;
            List<byte[]> chunks = Elements.Chunker(buffer, 254, 255, out removebytes);

            if (chunks.Count == 0)
            {
                return;
            }


            foreach (var chunk in chunks)
            {
                processmessage(chunk);
            }

            this.buffer.Clear();
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

            serialport.ReadBufferSize = 14000;
            serialport.WriteBufferSize = 5000;

            serialport.ReadTimeout = 500;
            serialport.ParityReplace = (byte)0;
            serialport.ReceivedBytesThreshold = 1;
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

                Byte[] start = new Byte[] { 40, 4, 1, 0 };
                Byte[] startnew = definecontrol(start);
                serialsend(startnew);


                Byte[] start1 = new Byte[] { 41, 0, 0, 0, 0, 0, 1, 1 };
                Byte[] startnew1 = definecontrol(start1);
                serialsend(startnew1);
            }
        }

       

        public override void Evacuate(string passedvalues)
        {
            Byte[] evac = new Byte[] { 61, 0, 1, 0, 0, 0, 0 };

            Byte[] evacnew = definecontrol(evac);

            // Byte[] evactest = new Byte[] { kAdvancedStart, 128, 0, 0, 4, 61, 7, 1, 0, 0, 0, 0, 240, 225, 100, kAdvanedEnd };  VB6 Example Send String

            serialsend(evacnew);

            p1 = 15; p2 = 1;
            p3 = 0; p4 = 70;

            p2 = p2 + this.Offset;
            message2 = "Evacuate";

            evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
            send_response_amx(evnum, "", message2);
        }
        public override void EvacuateNetwork(string passedvalues)
        {
            Console.WriteLine("GOT EVACUATE NETWORK PLEASE SEND TO SERIAL PORT TO SILENCE ALARM");
        }
        public override void Silence(string passedvalues)
        {
            Byte[] silence = new Byte[] { 60, 0, 32, 0, 0, 0 };

            Byte[] silencenew = definecontrol(silence);

            serialsend(silencenew);

            p1 = 15; p2 = 1;
            p3 = 0; p4 = 70;

            p2 = p2 + this.Offset;
            message2 = "Alarms Silenced";

            evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
            send_response_amx(evnum, "", message2);
        }

        public override void Alert(string passedvalues)
        {
            Byte[] alert = new Byte[] { 61, 0, 2, 0, 0, 0, 0 };

            Byte[] alertnew = definecontrol(alert);

            serialsend(alertnew);

            p1 = 15; p2 = 1;
            p3 = 0; p4 = 70;

            p2 = p2 + this.Offset;
            message2 = "Alert";

            evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
            send_response_amx(evnum, "", message2);
        }

        public override void MuteBuzzers(string passedvalues)
        {
            Console.WriteLine("GOT MUTE BUZZERS PLEASE SEND TO SERIAL PORT TO SILENCE ALARM");
        }

        public override void Reset(string passedvalues)
        {
            Byte[] reset = new Byte[] { 60, 0, 2, 0, 0, 0 };

            Byte[] resetnew = definecontrol(reset);

            serialsend(resetnew);

            p1 = 15; p2 = 1;
            p3 = 0; p4 = 70;

            p2 = p2 + this.Offset;
            message2 = "Alarms Reset";

            evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
            send_response_amx(evnum, "", message2);
        }
        public override void DisableDevice(string passedvalues)
        {
            string[] parts = passedvalues.Split(',');

            int node = 1, loop = 0, zone = 0, device = 0;

            if (parts.Length > 0) int.TryParse(parts[0], out node);
            if (parts.Length > 1) int.TryParse(parts[1], out loop);
            if (parts.Length > 2) int.TryParse(parts[2], out zone);
            if (parts.Length > 3) int.TryParse(parts[3], out device);

            Byte[] disiceabledev = new Byte[] { 70, 0, 0x85, 1, (byte)node, (byte)device, 0, 1 };

            Byte[] disiceabledevnew = definecontrol(disiceabledev);

            serialsend(disiceabledevnew);

            int inputtype = 4;
            string text = "";
            bool on = true;

            node = node + this.Offset;
            SendEvent("Advanced", NwmData.IsolationToAmx, inputtype, text, on, node, loop, device);

        }
        public override void EnableDevice(string passedvalues)
        {
            string[] parts = passedvalues.Split(',');

            int node = 1, loop = 0, zone = 0, device = 0;

            if (parts.Length > 0) int.TryParse(parts[0], out node);
            if (parts.Length > 1) int.TryParse(parts[1], out loop);
            if (parts.Length > 2) int.TryParse(parts[2], out zone);
            if (parts.Length > 3) int.TryParse(parts[3], out device);

            Byte[] enabledevice = new Byte[] { 70, 0, 0x85, 1, (byte)node, (byte)device, 0, 0 };

            Byte[] enabledevicenew = definecontrol(enabledevice);

            serialsend(enabledevicenew);

            int inputtype = 4;
            string text = "";
            bool on = false;

            node = node + this.Offset;
            SendEvent("Advanced", NwmData.IsolationToAmx, inputtype, text, on, node, loop, device);
        }

        public override void DisableZone(string passedvalues)
        {
            string[] parts = passedvalues.Split(',');

            int node = 1, loop = 0, zone = 0, device = 0;

            if (parts.Length > 0) int.TryParse(parts[0], out node);
            if (parts.Length > 1) int.TryParse(parts[1], out loop);
            if (parts.Length > 2) int.TryParse(parts[2], out zone);
            if (parts.Length > 3) int.TryParse(parts[3], out device);

            byte lowByte = (byte)(zone % 256);
            byte highByte = (byte)(zone / 256);

            Byte[] disablezone = new Byte[] { 70, 0, 0x83, lowByte, highByte, 0, 0, 1 };

            Byte[] disablezonenew = definecontrol(disablezone);

            serialsend(disablezonenew);

            int inputtype = 15;
            string text = "";
            bool on = true;

            node = node + this.Offset;
            SendEvent("Advanced", NwmData.IsolationToAmx, inputtype, text, on, node, loop, device);
        }
        public override void EnableZone(string passedvalues)
        {
            string[] parts = passedvalues.Split(',');

            int node = 1, loop = 0, zone = 0, device = 0;

            if (parts.Length > 0) int.TryParse(parts[0], out node);
            if (parts.Length > 1) int.TryParse(parts[1], out loop);
            if (parts.Length > 2) int.TryParse(parts[2], out zone);
            if (parts.Length > 3) int.TryParse(parts[3], out device);

            byte lowByte = (byte)(zone % 256);
            byte highByte = (byte)(zone / 256);

            Byte[] enablezone = new Byte[] { 70, 0, 0x83, lowByte, highByte, 0, 0, 0 };

            Byte[] enablezonenew = definecontrol(enablezone);

            serialsend(enablezonenew);

            int inputtype = 15;
            string text = "";
            bool on = false;

            node = node + this.Offset;
            SendEvent("Advanced", NwmData.IsolationToAmx, inputtype, text, on, node, loop, device);
        }
        #endregion

        #region private methods
        private byte[] definecontrol(byte[] evac)
        {
            // Convert input into a mutable list for easier manipulation
            List<byte> controlBytes = new List<byte>(evac);

            if (controlBytes.Count >= 2)
            {
                controlBytes[1] = (byte)controlBytes.Count;
            }

            // Add 0xF0 if not acknowledging
            if (!AcknowledgeMessage)
            {
                controlBytes.Add(0xF0);
            }

            // Prepend 4 header bytes: Start (0x80), Destination, Source, Sequence
            List<byte> header = new List<byte>
            {
                0x80,                          // Start marker
                (byte)AdvancedDestinationAddress,   // Destination
                (byte)AdvancedSourceAddress         // Source
            };

            // Set sequence number
            if (AcknowledgeMessage)
            {
                header.Add(0x00);
            }
            else
            {
                ControlPacketSequence++;
                if (ControlPacketSequence > 200)
                    ControlPacketSequence = 1;

                header.Add((byte)ControlPacketSequence);
            }

            // Combine header and control bytes
            List<byte> fullPacket = new List<byte>();
            fullPacket.AddRange(header);
            fullPacket.AddRange(controlBytes);

            // Do CRC and adjust for clash characters
            byte[] processed = doadvancedcrccalculation(fullPacket.ToArray());
            processed = MakeControlClashCharacters(processed);

            // Wrap with SOM (0xFE) and EOM (0xFF)
            List<byte> finalPacket = new List<byte>
            {
                0xFE
            };
            finalPacket.AddRange(processed);
            finalPacket.Add(0xFF);

            return finalPacket.ToArray();
        }
        private bool processmessage(byte[] ourmessage)
        {
            string hex = BitConverter.ToString(ourmessage);
            this.NotifyClient("Received (Hex): " + hex, false);
            string numeric = string.Join(" ", ourmessage.Select(b => b.ToString()));
            this.NotifyClient("Received (Numeric): " + numeric, false);


            string strmsg = Encoding.UTF8.GetString(ourmessage, 0, ourmessage.Length - 1);

            if (ourmessage.Length >= 6 &&
                ourmessage[1] == 128 && ourmessage[2] == 0 &&
                ourmessage[3] == 0 && ourmessage[4] == 0 &&
                ourmessage[5] == 1)
            {
                return true; // skip this specific pattern
            }

            
            string filePath = @"C:\Temp\Advanced_c#.txt";
            string messageText = string.Join(" ", ourmessage);
            string logLine = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                           + " - " + messageText;
            File.AppendAllText(filePath, logLine + Environment.NewLine);
       
            int removebytes = 0;

            List<byte[]> chunks = advancedchunker(1, ourmessage.Skip(3).ToArray(), 240, out removebytes);

            foreach (var chunk in chunks)
            {
                int node = 0;
                int loopnumber = 0;
                int deviceaddress = 0;
                int devicesubaddress = 0;
                int zone = 0;
                int inputtype = 13;
                int evnum1 = 0;
                bool on = true;

                switch (chunk[0])
                {
                    case 1:  // Acknowledgement
                        Console.WriteLine("Acknowledgement");
                        break;

                    case 15:   // Output Activated by BMS 
                        Console.WriteLine("BMS");
                        node = (int)chunk[2];
                        loopnumber = (int)chunk[3];
                        deviceaddress = (int)chunk[4];
                        devicesubaddress = (int)chunk[5];

                        if ((int)chunk[6] == 1)
                        {
                            evnum1 = CSAMXSingleton.CS.MakeInputNumber(node, loopnumber, deviceaddress, inputtype);
                            CSAMXSingleton.CS.SendAlarmToAMX(evnum1, "", "", "");
                            CSAMXSingleton.CS.FlushMessages();
                        }
                        else
                        {
                            evnum1 = CSAMXSingleton.CS.MakeInputNumber(node, loopnumber, deviceaddress, inputtype, false);
                            CSAMXSingleton.CS.SendAlarmToAMX(evnum1, "", "", "");
                            CSAMXSingleton.CS.FlushMessages();
                        }
                        break;

                    case 10:   // Device Status
                        Console.WriteLine("Device Status");
                        node = (int)chunk[2];
                        loopnumber = (int)chunk[3];
                        deviceaddress = (int)chunk[4];
                        devicesubaddress = (int)chunk[5];
                        zone = (int)chunk[6];
                        int devicestate = (int)chunk[8];
                        string devicetype = getadvanceddevicetype((int)chunk[10]);
                        string devicetext = string.Empty;
                        for (int i = 11; i < 12 + 12 && i < chunk.Length; i++)
                        {
                            devicetext += (char)chunk[i];
                        }

                        Console.WriteLine(strmsg);

                        if ((int)chunk[9] == 0)   // Device Enabled
                        {
                            inputtype = 4;
                            on = false;
                        }
                        if ((int)chunk[9] == 4)   // Device Disabled
                        {
                            inputtype = 4;
                            on = true;
                        }

                        switch (chunk[8])
                        {
                            case 3:   // Device Missing
                                inputtype = 8;
                                on = true;
                                devicetext = devicetype + " Device Missing";
                                break;

                            case 25:  // Silence
                                inputtype = 15;
                                on = true;
                                deviceaddress = 61;
                                devicetext = "Silence Key";
                                break;

                            case 26:  // Resound
                                inputtype = 15;
                                on = true;
                                deviceaddress = 63;
                                devicetext = "Resound Key";
                                break;

                            case 27:  // Mute
                                inputtype = 15;
                                on = true;
                                deviceaddress = 60;
                                devicetext = "Mute Key";
                                break;

                            case 28:  // Reset
                                inputtype = 15;
                                on = true;
                                deviceaddress = 59;
                                devicetext = "Reset Key";
                                break;

                            case 30:  // Pre Alarm
                                inputtype = 15;
                                on = true;
                                deviceaddress = 59;
                                devicetext = "Pre Alarm Key";
                                break;

                            case 31:  // Security Alert
                                inputtype = 15;
                                on = true;
                                deviceaddress = 59;
                                devicetext = "Security Alert";
                                break;

                            case 32:   // Evacuate
                                inputtype = 15;
                                on = true;
                                deviceaddress = 62;
                                devicetext = "Evacuate Key";
                                break;

                            case 33:   // Fire Alarm
                                inputtype = 0;
                                on = true;
                                break;

                            case 34:   // Fire Test
                                inputtype = 0;
                                on = true;
                                break;
                        }
                        evnum1 = CSAMXSingleton.CS.MakeInputNumber(node, loopnumber, deviceaddress, inputtype, on);
                        string message1 = devicetext;
                        CSAMXSingleton.CS.SendAlarmToAMX(evnum1, message1, "", "");
                        CSAMXSingleton.CS.FlushMessages();
                        break;

                    default:
                        Console.WriteLine("Unknown Command: " + chunk[0]);
                        this.NotifyClient("Unknown Command: " + chunk[0], false);

                        // Mike should we return false here?

                        break;
                }

                byte packetsequence = ourmessage[0];

                // send acknowledge
                AcknowledgeMessage = true;
                Byte[] stracknowledge = new Byte[] { 1, 0, packetsequence, 1 };
                Byte[] stracknowledgenew = definecontrol(stracknowledge);
                serialsend(stracknowledgenew);
                AcknowledgeMessage = false;

                string result = BitConverter.ToString(stracknowledge);
                this.NotifyClient("Sent " + result, false);
            }
            return true;
        }
        
        private string getadvanceddevicetype(int deviceType)
        {
            switch (deviceType)
            {
                case 0: return adtUNKNOWNDEVICE;
                case 1: return adtAPOLLOIONISATION;
                case 2: return adtAPOLLOOPTICAL;
                case 3: return adtAPOLLOMULTISENSOR;
                case 4: return adtAPOLLOHEAT;
                case 5: return adtZONEMONITOR;
                case 6: return adtCALLPOINT;
                case 7: return adtTEMPERATURESENSOR;
                case 8: return adtVOLTS1;
                case 9: return adtVOLTS2;
                case 10: return adtVOLTS3;
                case 11: return adtSWITCH;
                case 12: return adtSOUNDER;
                case 13: return adtMONITOREDRELAY;
                case 14: return adtRELAY;
                case 15: return adtMONITOR;
                case 16: return adtCURRENT;
                case 17: return adtCURRENT;
                case 18: return adtCO_FIRE;
                case 19: return adtCO_GASSENSOR;
                case 20: return adtFLAMEDETECTOR;
                case 21: return adtSWITCH_MONITORED;
                case 22: return adtHOCHIKIIONISATION;
                case 23: return adtHOCHIKIOPTICAL;
                case 24: return adtHOCHIKIMULTISENSOR;
                case 25: return adtHOCHIKIHEAT;
                case 26: return adtDOUBLEADDRESS;
                case 27: return adtBEACON;
                case 28: return adtMULTIHEAT;
                case 29: return adtRATEOFRISEHEAT;
                case 30: return adtOPTICALSMOKE;
                case 31: return adtFLAME;
                case 32: return adtINPUT1;
                case 33: return adtINPUT2;
                default: return string.Empty;        // unknown / unsupported
            }
        }


        protected override void heartbeat_timer_callback(object sender)
        {

            Console.WriteLine("Sent Heartbeat");

            // VB6 Code
            // sPanelNumber = HBT_Panel1 + giMainOffset
            // Call SendToAdvanced(Chr$(42) +Chr$(0) + Chr$(HBT_Panel1), False, sPanelNumber)

            //Byte[] heartbeat = new Byte[] { kAdvancedStart, 42, 0, 1, kAdvanedEnd };
            //string heartbeat = ((char)42).ToString() + (char)0 + (char)1;
            //serialsend(heartbeat);


            //Byte[] heartbeat = new Byte[] { 42, 0, 1 };
            Byte[] heartbeat = new Byte[] { 40, 4, 1 };

            Byte[] heartbeatnew = definecontrol(heartbeat);
            serialsend(heartbeatnew);

            // sendserial(Convert.ToChar(42).ToString() + Convert.ToChar(0).ToString() + Convert.ToChar(1).ToString());
        }

        
        private byte[] doadvancedcrccalculation(byte[] message)
        {
            // Append two spaces (0x20) for CRC storage
            byte[] extended = new byte[message.Length + 2];
            Array.Copy(message, extended, message.Length);
            extended[extended.Length - 2] = 0x20;
            extended[extended.Length - 1] = 0x20;

            // Call external CRC function - assumes it modifies `extended` in-place
            bool success = advancedcrccalculate(extended, extended.Length);

            if (!success)
            {
                return Array.Empty<byte>();
            }

            return extended;
        }

        private bool advancedcrccalculate(byte[] data, int length)
        {
            if (length < 2) return false;

            ushort crc = 0xFFFF;

            for (int i = 0; i < length - 2; i++) // skip last 2 bytes
            {
                crc ^= (ushort)(data[i]);
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    else
                        crc >>= 1;
                }
            }

            // Place CRC in last two bytes (little-endian)
            data[length - 2] = (byte)(crc & 0xFF);        // Low byte
            data[length - 1] = (byte)((crc >> 8) & 0xFF); // High byte

            return true;
        }
        private byte[] MakeControlClashCharacters(byte[] message)
        {
            List<byte> result = new List<byte>();

            foreach (byte b in message)
            {
                if (b < 0xFA)
                {
                    result.Add(b);
                }
                else
                {
                    result.Add(0xFA);
                    result.Add((byte)(b - 0xFA));
                }
            }

            return result.ToArray();
        }

        private void send_response_amx(int evnum, string message1, string message2, string message3 = "")
        {
            string friendlymessage = message2 + (message3.Length > 0 ? (" " + message3) : "");

            // Signal the event back to the main service, so that it can be logged
            this.NotifyClient(friendlymessage, false);

            CSAMXSingleton.CS.SendAlarmToAMX(evnum, message1, message2, message3);
            CSAMXSingleton.CS.FlushMessages();

        }

        // Advanced chunker to handle length-prefixed chunks with end byte
        private List<byte[]> advancedchunker(int lengthofchar, byte[] data, byte end, out int removelength)
        {
            List<byte[]> chunks = new List<byte[]>();
            int startpos = 1;
            removelength = 0;

            while (true)
            {
                if (startpos + lengthofchar >= data.Length)
                {
                    break;
                }

                byte workingcommand = data[startpos];
                if (workingcommand == end)
                {
                    break;
                }

                byte chunklength = data[startpos + lengthofchar];
                byte[] chunk = data.Skip(startpos).Take(chunklength).ToArray();
                chunks.Add(chunk);
                removelength += chunk.Length;
                // mover to end pos
                startpos += chunk.Length;

            }

            return chunks;
        }
    }
    #endregion 
}