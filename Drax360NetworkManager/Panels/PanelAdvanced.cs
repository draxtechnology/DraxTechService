using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Drax360Service.Panels
{
    internal class PanelAdvanced : AbstractPanel
    {
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

        bool AcknowledgeMessage = false;
        int AdvancedDestinationAddress = 0;
        int AdvancedSourceAddress = 0;
        int ControlPacketSequence = 0;
        

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

        public string Zone = "";
        public PanelAdvanced(string identifier) : base(identifier, "AdvMan")
        {
            if (!String.IsNullOrEmpty(identifier))
            {
                heartbeat_timer = new Timer(heartbeat_timer_callback, this.Identifier, 1000, kHeartbeatDelaySeconds * 1000);
            }
        }
        public override void Parse(byte[] buffer)

        {
            this.buffer.AddRange(buffer);
            int foundat = -1;
            int bufferlength = this.buffer.Count;

            byte[] ourmessage = this.buffer.ToArray();
            for (int i = 0; i < ourmessage.Length; i++)
            {
                if (ourmessage[i] == 255)
                {
                    foundat = i;
                    break;
                }
            }
            ;
            if (foundat <= 0) return;
            this.buffer.Clear();
            string strmsg = Encoding.UTF8.GetString(ourmessage, 0, foundat - 1);
            if (ourmessage[0] != 254) return;
            if (ourmessage[1] == 128 & ourmessage[2] == 0 & ourmessage[3] == 0 & ourmessage[4] == 0 & ourmessage[5] == 1) return;
            string cmd = strmsg.Substring(1, 2);

            int node = (int)ourmessage[7];
            int loopnumber = (int)ourmessage[8];
            int deviceaddress = (int)ourmessage[9];
            int devicesubaddress = (int)ourmessage[10];
            int zone = (int)ourmessage[11];
            string devicetype = GetAdvancedDeviceType((int)ourmessage[15]);
            string devicetext = string.Empty;
            for (int i = 16; i < 16 + 12 && i < ourmessage.Length; i++)
            {
                devicetext += (char)ourmessage[i];
            }

            Console.WriteLine(strmsg);

            byte packetsequece = Convert.ToByte(ourmessage[4]);
            // send acknowledge

            Byte[] stracknoledge = new Byte[] { kAdvancedStart, 1, 0, packetsequece, 1, kAdvanedEnd };
            //string stracknoledge = (Convert.ToChar(1).ToString() + Convert.ToChar(0).ToString() + Convert.ToChar(packetsequece).ToString() + Convert.ToChar(1).ToString());
            //FireFire("FIRE FIRE");
            serialsend(stracknoledge);
        }

        public static string GetAdvancedDeviceType(int deviceType)
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

            Byte[] heartbeat = new Byte[] { kAdvancedStart, 42, 0, 1, kAdvanedEnd };
            //string heartbeat = ((char)42).ToString() + (char)0 + (char)1;
            serialsend(heartbeat);

            // sendserial(Convert.ToChar(42).ToString() + Convert.ToChar(0).ToString() + Convert.ToChar(1).ToString());
        }

        public override void OnStartUp()
        {
            Byte[] start = new Byte[] { kAdvancedStart, 128, 0, 0, 2, 41, 8, 0, 0, 0, 0, 1, 1, 240, 250, 5, 195, kAdvanedEnd };
            serialsend(start);
        }

        public override void Evacuate(string passedvalues)
        {
            Byte[] evac = new Byte[] { 61, 0, 1, 0, 0, 0, 0 };

            Byte[] evacnew = DefineControl(evac);

           // Byte[] evactest = new Byte[] { kAdvancedStart, 128, 0, 0, 4, 61, 7, 1, 0, 0, 0, 0, 240, 225, 100, kAdvanedEnd };  VB6 Example Send String

            serialsend(evacnew);
        }
        public override void EvacuateNetwork(string passedvalues)
        {
            Console.WriteLine("GOT EVACUATE NETWORK PLEASE SEND TO SERIAL PORT TO SILENCE ALARM");
        }
        public override void Silence(string passedvalues)
        {
            Byte[] silence = new Byte[] { 60, 0, 32, 0, 0, 0 };

            Byte[] silencenew = DefineControl(silence);

            serialsend(silencenew);
        }

        public override void Alert(string passedvalues)
        {
            Byte[] alert = new Byte[] { 61, 0, 2, 0, 0, 0, 0 };

            Byte[] alertnew = DefineControl(alert);

            serialsend(alertnew);
        }

        public override void MuteBuzzers(string passedvalues)
        {
            Console.WriteLine("GOT MUTE BUZZERS PLEASE SEND TO SERIAL PORT TO SILENCE ALARM");
        }

        public override void Reset(string passedvalues)
        {
            Byte[] reset = new Byte[] { 60, 0, 2, 0, 0, 0 };

            Byte[] resetnew = DefineControl(reset);

            serialsend(resetnew);
        }
        public override void DisableDevice(string passedvalues)
        {
            string[] parts = passedvalues.Split(',');

            int node = 1, loop = 0, zone = 0, device = 0;

            if (parts.Length > 0) int.TryParse(parts[0], out node);
            if (parts.Length > 1) int.TryParse(parts[1], out loop);
            if (parts.Length > 2) int.TryParse(parts[2], out zone);
            if (parts.Length > 3) int.TryParse(parts[3], out device);

            Byte[] disiceabledev = new Byte[] { 70, 0, 0x85, 1, (byte)node, (byte)device, 0,1 };

            Byte[] disiceabledevnew = DefineControl(disiceabledev);

            serialsend(disiceabledevnew);
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

            Byte[] enabledevicenew = DefineControl(enabledevice);

            serialsend(enabledevicenew);
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

            Byte[] disablezonenew = DefineControl(disablezone);

            serialsend(disablezonenew);
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

            Byte[] enablezonenew = DefineControl(enablezone);

            serialsend(enablezonenew);
        }

        public byte[] DefineControl(byte[] evac)
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
            byte[] processed = DoTheAdvancedCrcCalculation(fullPacket.ToArray());
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

        private byte[] DoTheAdvancedCrcCalculation(byte[] message)
        {
            // Append two spaces (0x20) for CRC storage
            byte[] extended = new byte[message.Length + 2];
            Array.Copy(message, extended, message.Length);
            extended[extended.Length - 2] = 0x20;
            extended[extended.Length - 1] = 0x20;

            // Call external CRC function - assumes it modifies `extended` in-place
            bool success = AdvancedCrcCalculate(extended, extended.Length);

            if (!success)
            {
                return Array.Empty<byte>();
            }

            return extended;
        }

        private bool AdvancedCrcCalculate(byte[] data, int length)
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
    }
}