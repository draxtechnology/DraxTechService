using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ConstrainedExecution;
using System.Runtime.Remoting.Messaging;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using static System.Collections.Specialized.BitVector32;
using static System.Net.Mime.MediaTypeNames;

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
        public PanelAdvanced(string baselogfolder, string identifier) : base(baselogfolder,identifier, "AdvMan","ADV")
        {
            if (!String.IsNullOrEmpty(identifier))
            {
                heartbeat_timer = new Timer(heartbeat_timer_callback, this.Identifier, 500, kHeartbeatDelaySeconds * 1000);
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

            bool clean = true;
            foreach (var chunk in chunks)
            {
                if (!processmessage(chunk))
                {
                    clean = false;
                }
            }

            this.buffer.Clear();
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

            ourmessage = ourmessage.Skip(3).ToArray();

            if (ourmessage[1] == 1)   // Acknowledgement
            {
            }
            if (ourmessage[1] == 15)   // Output Activated by BMS 
            {
            }
            if (ourmessage[1] == 10)   // Device Status
            {
                int node = (int)ourmessage[3];
                int loopnumber = (int)ourmessage[4];
                int deviceaddress = (int)ourmessage[5];
                int devicesubaddress = (int)ourmessage[6];
                int zone = (int)ourmessage[7];
                int devicestate = (int)ourmessage[9];
                string devicetype = GetAdvancedDeviceType((int)ourmessage[11]);
                string devicetext = string.Empty;
                for (int i = 12; i < 12 + 12 && i < ourmessage.Length; i++)
                {
                    devicetext += (char)ourmessage[i];
                }

                Console.WriteLine(strmsg);

                byte packetsequence = Convert.ToByte(ourmessage[1]);
                // send acknowledge
                Byte[] stracknoledge = new Byte[] { kAdvancedStart, 1, 0, packetsequence, 1, kAdvanedEnd };
                serialsend(stracknoledge);

                string result = BitConverter.ToString(stracknoledge);
                this.NotifyClient("Sent " + result, false);

                int inputtype = 0;
                if ((int)ourmessage[10] == 4)   // device disabled
                {
                    inputtype = 4;
                }
                if ((int)ourmessage[9] == 3)   // device missing
                {
                    inputtype = 8;
                }
                int evnum1 = CSAMXSingleton.CS.MakeInputNumber(node, loopnumber, deviceaddress, inputtype);
                string message1 = devicetext;
                CSAMXSingleton.CS.SendAlarmToAMX(evnum1, message1, "", "");
                CSAMXSingleton.CS.FlushMessages();

            }
            return true;
        }

        /*
        public override void Parse(byte[] buffer)
        {
            base.Parse(buffer);
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

            /* From VB6 Example
               Public Type DLLDATA     'Array of longs passed to and from DLLs
                Dat(0 To 32) As Long
                End Type
             */

            /*
              Declare Function GetNWMData Lib "Gen_Netman.dll" Alias "_GetNWMData@24" (ByVal FileName As String, ByVal Index As Integer, LongArray As DLLDATA, ByVal DestString As String, ByVal ExText As String, ByVal ExText2 As String) As Integer

            */

            /*
            From VB6 Example
            sPanelNumber = CStr(GetNode(DLL.Dat(9)))
            
            Then calling the function to get the node number
            From C Network Manager

            DllExport __int16 WINAPI GetNode(ipnum)
                long ipnum;
            {
	            return get_board_address(ipnum);
            }
              __int16  get_board_address(ip)
            long ip;
            {
    	        return((__int16)((ip & 0x07ff0000)/0x10000));
               }
             */




            /*  From VB6 Example
                    iInputType = CInt(GetInputType(DLL.Dat(9)))
             
            
            DllExport __int16 WINAPI GetInputType(ipnum)
                long ipnum;
               {
	            return get_input_type(ipnum);
                }

            __int16  get_input_type(ip)
                long ip;
            {
	            return((__int16)((ip & 0x78000000)/0x8000000)); 	// based on offset of 2^25
            }

            int inputtype = (int)(ourmessage[6]); 
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
            serialsend(stracknoledge);

            string result = BitConverter.ToString(stracknoledge);
            this.NotifyClient("Sent " + result, false);

            
            int evnum1 = CSAMXSingleton.CS.MakeInputNumber(node, loopnumber, deviceaddress, inputtype);
            string message1 = devicetext;
            CSAMXSingleton.CS.SendAlarmToAMX(evnum1, message1, "", "");
            CSAMXSingleton.CS.FlushMessages();
        }
        */
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
            //serialsend(heartbeat);


            byte[] start3 = new byte[] { 0xFE, 0x80, 0x00, 0x00, 0x01, 0x28, 0x04, 0x01, 0x00, 0xF0, 0x5A, 0x55, 0xFF, 0x0D, 0x0A };

            serialsend(start3);
            base.NotifyClient("Sent Start3", false);

            // sendserial(Convert.ToChar(42).ToString() + Convert.ToChar(0).ToString() + Convert.ToChar(1).ToString());
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

            serialport.ReadBufferSize = 8000;
            serialport.WriteBufferSize = 200;

            serialport.ReadTimeout = 500;
            serialport.ParityReplace = (byte)0;
            serialport.ReceivedBytesThreshold = 8;
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
                Byte[] start = new Byte[] { kAdvancedStart, 128, 0, 0, 2, 41, 8, 0, 0, 0, 0, 1, 1, 240, 250, 5, 195, kAdvanedEnd };
               // serialsend(start);

                Thread.Sleep(1000);

                // SendToAdvanced Chr$(40) + Chr$(4) + Chr$(1) + Chr$(0), False, CStr(iPanelOffset)

                /*
                Byte[] start1 = new Byte[] { kAdvancedStart, 40, 4, 1,0, kAdvanedEnd };
                serialsend(start1);
                base.NotifyClient("Sent Start1", false);

                Thread.Sleep(1000);

                Byte[] start2 = new Byte[] { kAdvancedStart, 41, 0, 0, 0, 0, 0, 1, 1, kAdvanedEnd };
                serialsend(start2);
                base.NotifyClient("Sent Start2", false);
                */
                //Byte[] start3 = new Byte[] { kAdvancedStart, 128,0,0,2, 40, 4, 1, 0, kAdvanedEnd };

                // Byte[] start3 = new Byte[] { kAdvancedStart, 128, 0, 0, 2, 40, 4, 1, 0, 0, 0, 1, 1, 240, 250, 5, 195, kAdvanedEnd };
                // FE 80 00 00 01 28 04 01 00 F0 5A 55 FF 0D 0A 

                byte[] start3 = new byte[] { 0xFE, 0x80, 0x00, 0x00, 0x02, 0x29, 0x08, 0x00, 0x00, 0x00, 0x00, 0x01, 0x01, 0xF0, 0xFA, 0x05, 0xC3, 0xFF, 0x0D, 0x0A };


                // byte[] start3 = new byte[] { 0xFE, 0x80, 0x00, 0x00, 0x01, 0x28, 0x04, 0x01, 0x00, 0xF0, 0x5A, 0x55, 0xFF, 0x0D, 0x0A };

                serialsend(start3);
               // base.NotifyClient("Sent Start3", false);
            }
        }

        int p1 = 0;
        int p2 = 0;
        int p3 = 0;
        int p4 = 0;
        int evnum = 0;
        string message2 = "";

        public override void Evacuate(string passedvalues)
        {
            Byte[] evac = new Byte[] { 61, 0, 1, 0, 0, 0, 0 };

            Byte[] evacnew = DefineControl(evac);

            // Byte[] evactest = new Byte[] { kAdvancedStart, 128, 0, 0, 4, 61, 7, 1, 0, 0, 0, 0, 240, 225, 100, kAdvanedEnd };  VB6 Example Send String

            serialsend(evacnew);

            p1 = 15; p2 = 1;
            p3 = 0; p4 = 70;

            p2 = p2 + this.Offset;
            message2 = "Evacuate";

            evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
            send_response_amx_and_serial(evnum, "", message2);
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

            p1 = 15; p2 = 1;
            p3 = 0; p4 = 70;

            p2 = p2 + this.Offset;
            message2 = "Alarms Silenced";

            evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
            send_response_amx_and_serial(evnum, "", message2);
        }

        public override void Alert(string passedvalues)
        {
            Byte[] alert = new Byte[] { 61, 0, 2, 0, 0, 0, 0 };

            Byte[] alertnew = DefineControl(alert);

            serialsend(alertnew);

            p1 = 15; p2 = 1;
            p3 = 0; p4 = 70;

            p2 = p2 + this.Offset;
            message2 = "Alert";

            evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
            send_response_amx_and_serial(evnum, "", message2);
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

            p1 = 15; p2 = 1;
            p3 = 0; p4 = 70;

            p2 = p2 + this.Offset;
            message2 = "Alarms Reset";

            evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
            send_response_amx_and_serial(evnum, "", message2);
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

            Byte[] enabledevicenew = DefineControl(enabledevice);

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

            Byte[] disablezonenew = DefineControl(disablezone);

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

            Byte[] enablezonenew = DefineControl(enablezone);

            serialsend(enablezonenew);

            int inputtype = 15;
            string text = "";
            bool on = false;

            node = node + this.Offset;
            SendEvent("Advanced", NwmData.IsolationToAmx, inputtype, text, on, node, loop, device);
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

        private void send_response_amx_and_serial(int evnum, string message1, string message2, string message3 = "")
        {
            string friendlymessage = message2 + (message3.Length > 0 ? (" " + message3) : "");

            // Signal the event back to the main service, so that it can be logged
            this.NotifyClient(friendlymessage, false);

            CSAMXSingleton.CS.SendAlarmToAMX(evnum, message1, message2, message3);
            CSAMXSingleton.CS.FlushMessages();

        }

        public void SendToAdvanced(string messagePacket, bool useRetries, string psPanelNumber)
        {
            clsAdvancedControl avc = new clsAdvancedControl();
            int iSystem;
            int iPanel;

            try
            {
                iSystem = GetSystemNumber(Convert.ToInt32(psPanelNumber));

                // J.M 23/12/08 if global set panel number to 0
                if (bSendGlobalCommand)
                {
                    psPanelNumber = "0";
                }

                iPanel = GetRealPanelNumber(Convert.ToInt32(psPanelNumber));

                if (string.IsNullOrEmpty(psPanelNumber))
                {
                    System.Diagnostics.Debug.WriteLine("BLANK");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("STA-" + psPanelNumber);
                }

                // LogMyStuff("sendToAdvanced " + psPanelNumber + " Without Offset : " + iPanel, false);

                avc.ControlString = messagePacket;

                // Acknowledge time
                if (giNWMPanelTCP != 1)
                {
                    avc.AcknowledgeTimeout = 800;
                }
                else
                {
                    avc.AcknowledgeTimeout = 4800;
                }

                avc.AcknowledgeMessage = false;
                avc.PanelNumber = iPanel;
                avc.Retries = 1;
                avc.UseRetries = useRetries;
                avc.DefineControl();  // Create the object

                colADVControls[iSystem].Add(avc);

                // Bring forward the timer if there is no sending in progress
                if (colADVControls[iSystem].Count <= 1)
                {
                    // LogMyStuff("SendtoAdv " + psPanelNumber + " Without Offset : " + iPanel + " System : " + iSystem);
                    frmADVNetworkManager.tmrAdvancedSend[iSystem].Interval = 50;
                    frmADVNetworkManager.tmrAdvancedSend[iSystem].Enabled = true;
                }
                else if (!frmADVNetworkManager.comADV[0].PortOpen) // JM Port is closed so in Comm fault
                {
                    frmADVNetworkManager.tmrAdvancedSend[iSystem].Interval = 50;
                    frmADVNetworkManager.tmrAdvancedSend[iSystem].Enabled = true;
                }
            }
            catch (Exception ex)
            {
                ErrorBeep("SendToAdvanced Error: " + ex.Message);
                // Resume Next equivalent is just swallowing the error in VB6,
                // but in C# you might want to log and continue
            }
        }

    }
}