
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Drax360Service.Panels
{
    internal class PanelGent : AbstractPanel
    {
        #region constants
        const byte kzerobyte = 0x00;
        const byte kackbyte = 0x06;
        const byte kheartbeatdelayseconds = 60;
        const int kchunksize = 59; 
        #endregion

        public override string FakeString
        {
            get
            {
                // two messages are sent, so we return the same message twice
                string msg = "";
                msg+="\0\0\0\0X\u0002@\0\0\0\0\u0002/\v\u0017\u0006\u0019\0\0\0\0\0\u0003\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\u0001\u000f";
                //msg+= "\0\0\0\0X\u0002@\0\0\0\0\u0002/\v\u0017\u0006\u0019\0\0\0\0\0\u0003\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\u0001\u000f";

                // added this extra part to the message to make it longer
                //msg += "12345";
                return msg;
            }

            //get =>

              //   "\0\0\0\0X\u0002@\0\0\0\0\u0002/\v\u0017\u0006\u0019\0\0\0\0\0\u0003\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\u0001\u000f";
        }

        public PanelGent(string baselogfolder, string identifier) : base(baselogfolder,identifier, "GenMan","GEN")
        {
            if (!String.IsNullOrEmpty(identifier))
            {
                heartbeat_timer = new System.Threading.Timer(heartbeat_timer_callback, this.Identifier, 500, kheartbeatdelayseconds * 1000);
                this.Offset = base.GetSetting<int>(ksettingsetupsection, "giAmx1Offset");
            }
        }

        public override void Parse(byte[] buffer)
        {
            // Strip all leading 00-06-00-06 sequences
            while (buffer.Length > 3 &&
                   buffer[0] == 0x00 && buffer[1] == 0x06 &&
                   buffer[2] == 0x00 && buffer[3] == 0x06)
            {
                byte[] bufferNew = new byte[buffer.Length - 4];
                Array.Copy(buffer, 4, bufferNew, 0, buffer.Length - 4);
                buffer = bufferNew;

                this.NotifyClient("Stripped 00-06-00-06 from beginning", false);
            }

            // Always add whatever remains
            this.buffer.AddRange(buffer);

            // Chunk from rolling buffer
            var chunks = Elements.Chunker(this.buffer.ToArray(), kchunksize);

            bool clean = true;
            foreach (var chunk in chunks)
            {
                if (!processmessage(chunk))
                {
                    clean = false;
                }
            }
            if (clean)
            {
                // Remove only the processed bytes, keep leftovers
                int processedBytes = chunks.Count * kchunksize;
                if (processedBytes > 0)
                {
                    this.buffer.RemoveRange(0, processedBytes);
                }
            }
            else 
            {
                this.buffer.Clear();
            }
        }
        
        private bool processmessage(byte[] chunk)
        {
            string hex = BitConverter.ToString(chunk);
            this.NotifyClient("Received: " + hex, false);

            // test checksum
            int piMSB = chunk[chunk.Length - 2];
            int piLSB = chunk[chunk.Length - 1];
            int oiMSB, oiLSB;
            if (!gentchecksumvalidation(piMSB, piLSB, chunk, out oiMSB, out oiLSB))
            {
                this.NotifyClient("Checksum Error: " + " piMSB: " + piMSB + " piLSB: " + piLSB + " oiMSB: " + oiMSB + " oiLSB: " + oiLSB, false);

                string sEventCode1 = "";
                int sPanelNumber1 = 0;
                for (int i = 0; i < 11; i++)
                {
                    byte b = chunk[i];
                    int intb = Convert.ToInt32(b);
                    switch (i)
                    {
                        case 0:
                            sEventCode1 = intb.ToString();
                            break;
                        case 1:
                            sEventCode1 += intb.ToString();
                            break;
                        case 10:
                            sPanelNumber1 = intb;
                            break;
                    }
                }
                int evnum1 = CSAMXSingleton.CS.MakeInputNumber(sPanelNumber1, 0, 13, 15);
                string message1 = "Chksum Fail: Evt Code : " + sEventCode1;
                CSAMXSingleton.CS.SendAlarmToAMX(evnum1, message1, "", "");
                CSAMXSingleton.CS.FlushMessages();
                return false;
            }

            string sEventCode = "";
            int AddressNumber = 0;
            int sChannelNumber = 0;
            string sSectorBitArray = "";
            int sZoneNumber = 0;
            int sLoopNumber = 0;
            int sPanelNumber = 0;
            int sDomainNumber = 0;
            int sMasterSector = 0;
            string sTime = "";
            string sEventParam = "";
            string gsTextField = "";
            for (int i = 0; i < 25; i++)
            {
                byte b = chunk[i];
                int intb = Convert.ToInt32(b);
                switch (i)
                {
                    case 0:
                        sEventCode = intb.ToString();
                        break;
                    case 1:
                        sEventCode += intb.ToString();
                        break;
                    case 2:
                        AddressNumber = intb;
                        break;
                    case 3:
                        sChannelNumber = intb;
                        break;
                    case 4:
                    case 5:
                    case 6:
                    case 7:
                        sSectorBitArray += intb.ToString();
                        break;
                    case 8:
                        sZoneNumber += intb;
                        break;
                    case 9:
                        sLoopNumber = intb;
                        break;
                    case 10:
                        sPanelNumber = intb;
                        break;
                    case 11:
                    case 12:
                    case 13:
                    case 14:
                    case 15:
                        sTime += intb.ToString();
                        break;
                    case 16:
                        sZoneNumber += intb;
                        break;
                    case 17:
                        sDomainNumber = intb;
                        break;
                    case 18:
                        sMasterSector = intb;
                        break;
                    case 19:
                    case 20:
                    case 21:
                    case 22:
                    case 23:
                    case 24:
                        sEventParam += intb.ToString();
                        break;
                    default:
                        break;
                }
            }

            for (int i = 25; i < 57; i++)
            {
                byte b = chunk[i];
                if (b == 0) continue;
                gsTextField += b.ToString();
            }


            int sMSB = 0;
            int sLSB = 0;

            if (sEventCode.Length > 2)
            {
                sMSB = Convert.ToInt32(sEventCode.Substring(0, 2));
                sLSB = Convert.ToInt32(sEventCode.Substring(2, 1));
            }
            else
            {
                sMSB = Convert.ToInt32(sEventCode.Substring(0, 1));
                sLSB = Convert.ToInt32(sEventCode.Substring(1, 1));
            }

            int p1 = 0;
            int p2 = sPanelNumber;
            int p3 = sLoopNumber;
            int p4 = AddressNumber;
            string message2 = "";
            string message3 = "";
            bool on = true;

            int evnum = 0;

            switch (sMSB)
            {
                case 0:
                    switch (sLSB)
                    {
                        case 0:  // Handshake
                            if (Convert.ToInt32(sEventParam.Substring(2, 2)) > 0)
                            {
                                int giNoOfFaults = Convert.ToInt32(sEventParam.Substring(2, 2));
                                message2 = giNoOfFaults.ToString() + " Panel(s) in Fault Condition";

                                p1 = 15; p2 = 1;
                                p3 = 0; p4 = 55;

                                p2 = p2 + this.Offset;
                                
                                evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
                                send_response_amx_and_serial(evnum, "", message2);
                            }
                            if (Convert.ToInt32(sEventParam.Substring(4, 2)) > 0)
                            {
                                int giNoOfDisable = Convert.ToInt32(sEventParam.Substring(4, 2));
                                message2 = giNoOfDisable.ToString() + " Panel(s) in Disablement";

                                p1 = 15; p2 = 1;
                                p3 = 0; p4 = 56;

                                p2 = p2 + this.Offset;

                                evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
                                send_response_amx_and_serial(evnum, "", message2, message3);
                            }
                            break;

                        case 1:
                            message2 = "Reset";
                            p1 = 15; p2 = sPanelNumber;
                            p3 = 0; p4 = 9;

                            p2 = p2 + this.Offset;

                            evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
                            send_response_amx_and_serial(evnum, "", message2);
                            break;

                        case 2:
                            message2 = "Faults Cleared";
                            p1 = 8; p2 = sPanelNumber;
                            p3 = 0; p4 = 21;

                            p2 = p2 + this.Offset;

                            evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
                            send_response_amx_and_serial(evnum, "", message2);
                            break;

                        case 3:
                            if (sLoopNumber == 0)
                            {
                                message2 = "Zone Enable";
                                p1 = 15;
                                on = false;
                            }
                            else
                            {
                                message2 = "Device Enable";
                                p1 = 4;
                                on = false;
                                
                            }
                            p2 = sPanelNumber;
                            p3 = sLoopNumber; p4 = 53;

                            p2 = p2 + this.Offset;

                            evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1, on);
                            send_response_amx_disable(evnum, "", message2, message3);
                            send_response_amx_and_serial(evnum, "", message2);
                            break;

                        case 4:
                            message2 = "Alarms Silenced";
                            p1 = 15; p2 = sPanelNumber;
                            p3 = 0; p4 = 10;

                            p2 = p2 + this.Offset;

                            evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
                            send_response_amx_and_serial(evnum, "", message2);
                            break;

                        case 5:
                            message2 = "Alarms Sounded";
                            p1 = 15; p2 = sPanelNumber;
                            p3 = 0; p4 = 1;

                            p2 = p2 + this.Offset;

                            evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
                            send_response_amx_and_serial(evnum, "", message2);
                            break;

                        default:
                            this.NotifyClient("Unknown sMSB: " + sMSB + " sLSB: " + sLSB, false);
                            break;
                    }
                    break;

                case 1:
                    if (sLSB == 8)
                    {
                        message2 = "Cancel Buzzer";
                        p1 = 4; p2 = sPanelNumber;
                        p3 = 0; p4 = 1;

                        p2 = p2 + this.Offset;

                        evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
                        send_response_amx_and_serial(evnum, "", message2);
                    }
                    else
                    {
                        this.NotifyClient("Unknown sMSB: " + sMSB + " sLSB: " + sLSB, false);
                    }
                    break;

                case 2:
                    switch (sLSB)
                    {
                        case 1:
                            message2 = "Supervisory On";
                            p1 = 15; p2 = sPanelNumber;
                            p3 = 0; p4 = 12;

                            p2 = p2 + this.Offset;

                            evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
                            send_response_amx_and_serial(evnum, "", message2);
                            break;

                        case 2:
                            message2 = "Supervisory Off";
                            p1 = 15; p2 = sPanelNumber;
                            p3 = 0; p4 = 12;

                            p2 = p2 + this.Offset;

                            evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1, false);
                            send_response_amx_and_serial(evnum, "", message2);
                            break;

                        default:
                            this.NotifyClient("Unknown sMSB: " + sMSB + " sLSB: " + sLSB, false);
                            break;
                    }
                    break;

                case 4:
                    message2 = "System Fault";
                    p1 = 15; p2 = sPanelNumber;
                    p3 = 0; p4 = 55;

                    p2 = p2 + this.Offset;

                    evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
                    send_response_amx_and_serial(evnum, "", message2);
                    break;

                case 5:
                    message2 = "Fault"; // Out Station Loop Fault
                    p1 = 8; p2 = sPanelNumber;
                    p3 = sLoopNumber; p4 = AddressNumber;

                    p2 = p2 + this.Offset;

                    evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
                    send_response_amx_and_serial(evnum, "", message2);
                    break;

                case 7:
                    if (AddressNumber == 0)
                    {
                        message2 = "Zone Disablement";
                        p1 = 15;

                        switch (sLoopNumber)
                        {
                            case 0: p4 = 53; break;
                            case 1: p4 = 37; break;
                            case 2: p4 = 38; break;
                            case 3: p4 = 39; break;
                            case 4: p4 = 40; break;
                            case 5: p4 = 41; break;
                            case 6: p4 = 42; break;
                            case 7: p4 = 43; break;
                            case 8: p4 = 44; break;
                            case 9: p4 = 45; break;
                            case 10: p4 = 46; break;
                            case 11: p4 = 47; break;
                            case 12: p4 = 48; break;
                            case 13: p4 = 49; break;
                            case 14: p4 = 50; break;
                            case 15: p4 = 51; break;
                            case 16: p4 = 52; break;
                        }
                    }
                    else
                    {
                        message2 = "Device Disablement";
                        p1 = 4;
                        p2 = sPanelNumber;
                        p3 = sLoopNumber;
                        p4 = AddressNumber;
                        p2 = p2 + this.Offset;
                        evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
                        send_response_amx_disable(evnum, "", message2, message3);
                    }

                    p2 = sPanelNumber;
                    p3 = sLoopNumber;

                    p2 = p2 + this.Offset;

                    evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
                    send_response_amx_and_serial(evnum, "", message2, message3);
                    break;

                case 9:
                    message2 = "Fire";
                    p1 = 15; p2 = sPanelNumber;
                    p3 = sLoopNumber; p4 = 54;

                    p2 = p2 + this.Offset;

                    evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
                    send_response_amx_and_serial(evnum, "", message2);
                    break;

                case 10:
                    message2 = "Super Fire";
                    p1 = 15; p2 = sPanelNumber;
                    p3 = sLoopNumber; p4 = 54;

                    p2 = p2 + this.Offset;

                    evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
                    send_response_amx_and_serial(evnum, "", message2);
                    break;

                case 18:
                    message2 = "Cancel Buzzer";
                    p1 = 15; p2 = sPanelNumber;
                    p3 = sLoopNumber; p4 = 12;

                    p2 = p2 + this.Offset;

                    evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1);
                    send_response_amx_and_serial(evnum, "", message2);
                    break;

                default:
                    this.NotifyClient("Unknown sMSB: " + sMSB + " sLSB: " + sLSB, false);
                    break;
            }

            return true;
        }

        private void send_response_amx_and_serial(int evnum, string message1, string message2, string message3 = "")
        {
            string friendlymessage = message2 + (message3.Length > 0 ? (" " + message3) : "");

            // Signal the event back to the main service, so that it can be logged
            this.NotifyClient(friendlymessage, false);

            CSAMXSingleton.CS.SendAlarmToAMX(evnum, message1, message2, message3);
            CSAMXSingleton.CS.FlushMessages();

            serialsend(new byte[] { kzerobyte, kackbyte, kzerobyte, kackbyte });
            byte[] bytesToLog = new byte[] { kzerobyte, kackbyte, kzerobyte, kackbyte };
            string hex = BitConverter.ToString(bytesToLog); // "00-06-00-06"
            this.NotifyClient("ACK Sent: " + hex, false);
        }
        private void send_response_amx_disable(int evnum, string message1, string message2, string message3 = "")
        {
            string friendlymessage = message2 + (message3.Length > 0 ? (" " + message3) : "");

            // Signal the event back to the main service, so that it can be logged
            this.NotifyClient(friendlymessage, false);

            CSAMXSingleton.CS.SendAlarmToAMX_disable(evnum, message1, message2, message3);
            CSAMXSingleton.CS.FlushMessages();
        }
        private bool gentchecksumvalidation(int piMSB, int piLSB, byte[] paryMessage, out int oiMSB, out int oiLSB)
        {
            int iMsgCheckSum = 0;
            for (int i = 0; i < paryMessage.Length - 2; i++)
            {
                iMsgCheckSum += paryMessage[i];
            }

            int iMSB = Convert.ToInt32(iMsgCheckSum / 256);
            int iLSB = iMsgCheckSum % 256;

            oiMSB = iMSB;
            oiLSB = iLSB;

            return piMSB == iMSB && piLSB == iLSB;
        }

        private void CalculateCheckSum(string[] paryMessage, out int piMSB, out int piLSB)
        {
            piMSB = 0;
            piLSB = 0;

            try
            {
                int iMsgCheckSum = 0;

                for (int i = 0; i < paryMessage.Length - 2; i++)
                {
                    if (!string.IsNullOrEmpty(paryMessage[i]) && (int)paryMessage[i][0] != 0)
                    {
                        iMsgCheckSum += (int)paryMessage[i][0];
                    }
                }

                piMSB = iMsgCheckSum / 256;
                piLSB = iMsgCheckSum % 256;
            }
            catch (Exception)
            {
                this.NotifyClient("Checksumvalidation Error:",false);
                this.NotifyClient("piMSB: " + piMSB, false);
                this.NotifyClient("piLSB: " + piLSB, false);
            }
        }

        protected override void heartbeat_timer_callback(object sender)
        {
            base.heartbeat_timer_callback(sender);
            serialsend(new byte[] { kzerobyte, kackbyte, kzerobyte, kackbyte });
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
            base.NotifyClient("Attempting Open "+ serialport.PortName,false);
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
                base.NotifyClient("Failed To Open " + serialport.PortName + " " + e.ToString(), false);
            }

            if (serialport.IsOpen)
            {
                serialport.DiscardInBuffer();
                serialport.DiscardOutBuffer();
            }
        }
        public override void Evacuate(string passedvalues)
        {
            send_message(ActionType.kEVACTUATE, NwmData.AlarmToAmx, passedvalues);
        }
        public override void Alert(string passedvalues)
        {
        }
        public override void EvacuateNetwork(string passedvalues)
        {
            send_message(ActionType.kEVACTUATENETWORK, NwmData.AlarmToAmx, passedvalues);
        }
        public override void Silence(string passedvalues)
        {
            send_message(ActionType.kSILENCE, NwmData.AlarmToAmx, passedvalues);
        }
        public override void MuteBuzzers(string passedvalues)
        {
            send_message(ActionType.kMUTEBUZZERS, NwmData.AlarmToAmx, passedvalues);
        }
        public override void Reset(string passedvalues)
        {
            send_message(ActionType.kRESET, NwmData.AlarmToAmx, passedvalues);
        }
        public override void DisableDevice(string passedvalues)
        {
            send_message(ActionType.kDISABLEDEVICE, NwmData.IsolationToAmx, passedvalues);
        }
        public override void EnableDevice(string passedvalues)
        {
            send_message(ActionType.kENABLEDEVICE, NwmData.IsolationToAmx, passedvalues);
        }
        public override void DisableZone(string passedvalues)
        {
            send_message(ActionType.kDISABLEZONE, NwmData.IsolationToAmx, passedvalues);
        }
        public override void EnableZone(string passedvalues)
        {
            send_message(ActionType.kENABLEZONE, NwmData.IsolationToAmx, passedvalues);
        }
        public override void Analogue(string passedvalues)
        {
            throw new NotImplementedException();
        }
        private void send_message(ActionType action, NwmData type, string passedvalues)
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
 
            byte[] gbaryDataToTX = new byte[59];

            string text = action.ToString();

            for (int i = 1; i <= 9; i++)
            {
                gbaryDataToTX[i] = 0;
            }
            if (action == ActionType.kEVACTUATENETWORK)
            {
                gbaryDataToTX[0] = 32;
                text = "Alarms Sounded";
                inputtype = 15;
                loop = 0;
                device = 1;
                node = 0;
            }

            if (action == ActionType.kEVACTUATE)
            {
                gbaryDataToTX[0] = 32;
                text = "Alarms Sounded";
                inputtype = 15;
                loop = 0;
                device = 1;
            }

            if (action == ActionType.kRESET)
            {
                gbaryDataToTX[0] = 20;
                text = "Reset";
                inputtype = 15;
                loop = 0;
                device = 9;
            }

            if (action == ActionType.kSILENCE)
            {
                gbaryDataToTX[0] = 16;
                text = "Alarms Silenced";
                inputtype = 15;
                loop = 0;
                device = 10;
            }

            if (action == ActionType.kMUTEBUZZERS)
            {
                gbaryDataToTX[0] = 18;
            }

            if (action == ActionType.kDISABLEDEVICE)
            {
                gbaryDataToTX[0] = 128;
                gbaryDataToTX[1] = 4;
                gbaryDataToTX[2] = (byte)device;
                gbaryDataToTX[8] = (byte)zone;
                gbaryDataToTX[9] = (byte)loop;
                text = "Disable Device";
                inputtype = 4;
            }

            if (action == ActionType.kENABLEDEVICE)
            {
                gbaryDataToTX[0] = 128;
                gbaryDataToTX[1] = 1;
                gbaryDataToTX[2] = (byte)device;
                gbaryDataToTX[8] = (byte)zone;
                gbaryDataToTX[9] = (byte)loop;
                text = "Enable Device";
                inputtype = 4;
                on = false;
            }

            if (action == ActionType.kDISABLEZONE)
            {
                gbaryDataToTX[0] = 144;
                gbaryDataToTX[1] = 4;
                gbaryDataToTX[8] = (byte)zone;
                gbaryDataToTX[9] = (byte)loop;
                text = "Zone Disablement";
                inputtype = 15;

                switch (loop)
                {
                    case 0:
                        device = 53;
                        break;
                    case 1:
                        device = 37;
                        break;
                    case 2:
                        device = 38;
                        break;
                    case 3:
                        device = 39;
                        break;
                    case 4:
                        device = 40;
                        break;
                    case 5:
                        device = 41;
                        break;
                    case 6:
                        device = 42;
                        break;
                    case 7:
                        device = 43;
                        break;
                    case 8:
                        device = 44;
                        break;
                    case 9:
                        device = 45;
                        break;
                    case 10:
                        device = 46;
                        break;
                    case 11:
                        device = 47;
                        break;
                    case 12:
                        device = 48;
                        break;
                    case 13:
                        device = 49;
                        break;
                    case 14:
                        device = 50;
                        break;
                    case 15:
                        device = 51;
                        break;
                    case 16:
                        device = 52;
                        break;
                }
            }

            if (action == ActionType.kENABLEZONE)
            {
                gbaryDataToTX[0] = 144;
                gbaryDataToTX[1] = 1;
                gbaryDataToTX[8] = (byte)zone;
                gbaryDataToTX[9] = (byte)loop;
                text = "Enable Zone";
                inputtype = 15;
                on = false;
                switch (loop)
                {
                    case 0:
                        device = 53;
                        break;
                    case 1:
                        device = 37;
                        break;
                    case 2:
                        device = 38;
                        break;
                    case 3:
                        device = 39;
                        break;
                    case 4:
                        device = 40;
                        break;
                    case 5:
                        device = 41;
                        break;
                    case 6:
                        device = 42;
                        break;
                    case 7:
                        device = 43;
                        break;
                    case 8:
                        device = 44;
                        break;
                    case 9:
                        device = 45;
                        break;
                    case 10:
                        device = 46;
                        break;
                    case 11:
                        device = 47;
                        break;
                    case 12:
                        device = 48;
                        break;
                    case 13:
                        device = 49;
                        break;
                    case 14:
                        device = 50;
                        break;
                    case 15:
                        device = 51;
                        break;
                    case 16:
                        device = 52;
                        break;
                }
            }
            gbaryDataToTX[10] = (byte)node;
            gbaryDataToTX[11] = (byte)sSecond;
            gbaryDataToTX[12] = (byte)sMinute;
            gbaryDataToTX[13] = (byte)sHour;
            gbaryDataToTX[14] = (byte)sDayWeek;
            gbaryDataToTX[15] = (byte)sMonth;
            gbaryDataToTX[16] = (byte)sYear;
            gbaryDataToTX[17] = (byte)giDomainNumber;

            for (int i = 18; i <= 56; i++)
            {
                gbaryDataToTX[i] = 0;
            }

            // Convert byte array to string[] of single-character strings (if needed for checksum function)
            string[] checksumInput = gbaryDataToTX.Select(b => ((char)b).ToString()).ToArray();

            CalculateCheckSum(checksumInput, out int iMSB, out int iLSB);

            // Add checksum bytes
            gbaryDataToTX[57] = (byte)iMSB;
            gbaryDataToTX[58] = (byte)iLSB;

            serialsend(gbaryDataToTX);

            node = node + this.Offset;
            SendEvent("Gent", type, inputtype, text, on, node, loop, device);
        }


        public override void SerialPort_Datareceived(object sender, SerialDataReceivedEventArgs e)
        {
            const int kchunksize = 59;  // packet size
            const int maxWaitMs = 500;  // how long to wait for remaining bytes
            const int pollDelayMs = 10; // how often to check

            lastDataReceived = DateTime.Now;
            int waited = 0;

            while (serialport.BytesToRead < kchunksize && waited < maxWaitMs)
            {
                System.Threading.Thread.Sleep(pollDelayMs);
                waited += pollDelayMs;
            }

            int bytestoread = serialport.BytesToRead;
            if (bytestoread == 0) return;

            byte[] readbytes = new byte[bytestoread];
            int numberread = serialport.Read(readbytes, 0, bytestoread);
            if (numberread == 0) return;

            // add check for 0606 at the beginning of the message  ??????

            Parse(readbytes);
        }
        /*
        // This method is not used in the current code, but it can be useful for converting byte arrays to escaped strings
        string ConvertByteArrayToEscapedString(byte[] bytes)
        {
            var sb = new StringBuilder();
            sb.Append('"');
            foreach (byte b in bytes)
            {
                if (b == 0x00)
                {
                    sb.Append(@"\0");
                }
                else if (b >= 0x01 && b <= 0x1F)
                {
                    sb.Append(@"\u" + b.ToString("X4"));
                }
                else if (b == 0x5C) // backslash '\'
                {
                    sb.Append(@"\\");
                }
                else if (b == 0x22) // double quote '"'
                {
                    sb.Append("\\\"");
                }
                else
                {
                    sb.Append((char)b);
                }
            }
            sb.Append('"');
            return sb.ToString();
        }*/
    }

}