
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.IO;

namespace Drax360Service.Panels
{
    internal class PanelGent : AbstractPanel
    {
        #region constants
        const byte kzerobyte = 0x00;
        const byte kackbyte = 0x06;
        protected const byte kheartbeatdelayseconds = 60;
        #endregion

        public override string GetFileName { get => "GenMan"; }

        public override string FakeString
        {
            get =>

                 "\0\0\0\0X\u0002@\0\0\0\0\u0002/\v\u0017\u0006\u0019\0\0\0\0\0\u0003\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\u0001\u000f";
        }

        public string Zone = "";

        public PanelGent(string identifier) : base(identifier)
        {
            if (!String.IsNullOrEmpty(identifier))
            {
                heartbeat_timer = new System.Threading.Timer(heartbeat_timer_callback, this.Identifier, 500, kheartbeatdelayseconds * 1000);
            }
        }

        public override void Parse(byte[] buffer)
        {
            //base.Parse(buffer);
            this.buffer.AddRange(buffer);

            while (this.buffer.Count >= 59)
            {
                int bufferlength = this.buffer.Count;
                //if (bufferlength < 59) return; // bail if we have less bytes than is viable
                byte[] ourmessage = this.buffer.GetRange(0, 59).ToArray();
                //this.buffer.Clear();
                this.buffer.RemoveRange(0, 59);
                string strmsg = Encoding.UTF8.GetString(ourmessage);

                string filePath = "c:\\temp\\csharp_output_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".gen";
                File.WriteAllBytes(filePath, ourmessage);
                // test checksum
                int piMSB = ourmessage[ourmessage.Length - 2];
                int piLSB = ourmessage[ourmessage.Length - 1];
                if (!gentchecksumvalidation(piMSB, piLSB, ourmessage)) return;

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

                    byte b = ourmessage[i];
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

                if (ourmessage.Length > 25)
                {
                    for (int i = 25; i < 57; i++)
                    {
                        byte b = ourmessage[i];
                        if (b == 0) continue;
                        gsTextField += b.ToString();
                    }
                }

                string sMSB = "";
                string sLSB = "";

                if (sEventCode.Length > 2)
                {
                    sMSB = sEventCode.Substring(0, 2);
                    sLSB = sEventCode.Substring(2, 1);
                }

                else
                {
                    sMSB = sEventCode.Substring(0, 1);
                    sLSB = sEventCode.Substring(1, 1);
                }

                if (sMSB == "0")
                {
                    if (sLSB == "4")
                    {
                        Console.WriteLine("-------------------- Alarms Silenced ----------- ");

                        string part1 = "15";
                        string part2 = "1";
                        string part3 = "0";
                        string part4 = "10";

                        int p1 = int.Parse(part1);
                        int p2 = int.Parse(part2);
                        int p3 = int.Parse(part3);
                        int p4 = int.Parse(part4);

                        int evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1, true);
                        CSAMXSingleton.CS.SendAlarmToAMX(evnum, "", "Alarms Silenced");
                        CSAMXSingleton.CS.FlushMessages();

                        buffer = new byte[] { kzerobyte, kackbyte, kzerobyte, kackbyte };
                        sendserial(buffer);
                    }

                    if (sLSB == "5")
                    {
                        Console.WriteLine("-------------------- Panel In Evac Condition ----------- ");

                        string part1 = "15";
                        string part2 = "1";
                        string part3 = "0";
                        string part4 = "1";

                        int p1 = int.Parse(part1);
                        int p2 = int.Parse(part2);
                        int p3 = int.Parse(part3);
                        int p4 = int.Parse(part4);

                        int evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1, true);
                        CSAMXSingleton.CS.SendAlarmToAMX(evnum, "", "Alarms Sounded");
                        CSAMXSingleton.CS.FlushMessages();

                        buffer = new byte[] { kzerobyte, kackbyte, kzerobyte, kackbyte };
                        sendserial(buffer);
                    }

                    if (sLSB == "1")
                    {
                        Console.WriteLine("-------------------- Panel In Reset Condition ----------- ");

                        string part1 = "1";
                        string part2 = "1";
                        string part3 = "0";
                        string part4 = "1";

                        int p1 = int.Parse(part1);
                        int p2 = int.Parse(part2);
                        int p3 = int.Parse(part3);
                        int p4 = int.Parse(part4);

                        int evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1, true);
                        CSAMXSingleton.CS.SendAlarmToAMX(evnum, "", "Reset");
                        CSAMXSingleton.CS.FlushMessages();

                        buffer = new byte[] { kzerobyte, kackbyte, kzerobyte, kackbyte };
                        sendserial(buffer);
                    }

                    if (sLSB == "3")
                    {
                        Console.WriteLine("-------------------- Panel In Enable Condition ----------- ");

                        string part1 = "4";
                        string part2 = "1";
                        string part3 = "0";
                        string part4 = "1";

                        int p1 = int.Parse(part1);
                        int p2 = int.Parse(part2);
                        int p3 = int.Parse(part3);
                        int p4 = int.Parse(part4);

                        int evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1, true);
                        CSAMXSingleton.CS.SendAlarmToAMX(evnum, "", "Enable");
                        CSAMXSingleton.CS.FlushMessages();

                        buffer = new byte[] { kzerobyte, kackbyte, kzerobyte, kackbyte };
                        sendserial(buffer);
                    }
                }
                if (sMSB == "1")
                {
                    if (sLSB == "8")
                    {
                        Console.WriteLine("-------------------- Panel In Cancel Buzzer ----------- ");

                        string part1 = "4";
                        string part2 = "1";
                        string part3 = "0";
                        string part4 = "1";

                        int p1 = int.Parse(part1);
                        int p2 = int.Parse(part2);
                        int p3 = int.Parse(part3);
                        int p4 = int.Parse(part4);

                        int evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1, true);
                        CSAMXSingleton.CS.SendAlarmToAMX(evnum, "", "Cancel Buzzer");
                        CSAMXSingleton.CS.FlushMessages();

                        buffer = new byte[] { kzerobyte, kackbyte, kzerobyte, kackbyte };
                        sendserial(buffer);
                    }
                }
                if (sMSB == "4")
                {
                    int giNoOfFaults = Convert.ToInt32(sEventParam.Substring(2, 2));
                    Console.WriteLine("-------------------- Panel In " + giNoOfFaults.ToString() + " Fault Condition ----------- ");

                    string part1 = "8";
                    string part2 = "1";
                    string part3 = "0";
                    string part4 = "1";

                    int p1 = int.Parse(part1);
                    int p2 = int.Parse(part2);
                    int p3 = int.Parse(part3);
                    int p4 = int.Parse(part4);

                    int evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1, true);
                    CSAMXSingleton.CS.SendAlarmToAMX(evnum, "", giNoOfFaults.ToString() + " Fault(s) in Panel or Panels", "Number of Panels in Fault");
                    CSAMXSingleton.CS.FlushMessages();

                    buffer = new byte[] { kzerobyte, kackbyte, kzerobyte, kackbyte };
                    sendserial(buffer);
                }
                if (sMSB == "7")
                {
                    int giNoOfDisable = Convert.ToInt32(sEventParam.Substring(4, 2));
                    Console.WriteLine("-------------------- Panel In " + giNoOfDisable.ToString() + " Disable Condition ----------- ");

                    int p1 = 4;
                    int p2 = sPanelNumber;
                    int p3 = sLoopNumber;
                    int p4 = AddressNumber;

                    string part1 = "4";
                    string part2 = "1";
                    string part3 = "0";
                    string part4 = "1";

                    p1 = int.Parse(part1);
                    p2 = int.Parse(part2);
                    p3 = int.Parse(part3);
                    p4 = int.Parse(part4);

                    int evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1, true);
                    CSAMXSingleton.CS.SendAlarmToAMX(evnum, "", giNoOfDisable.ToString() + " Panel(s) in Disablement", "Number of Panels in Disablement");
                    CSAMXSingleton.CS.FlushMessages();

                    buffer = new byte[] { kzerobyte, kackbyte, kzerobyte, kackbyte };
                    sendserial(buffer);
                }
            }
        }

        private bool gentchecksumvalidation(int piMSB, int piLSB, byte[] paryMessage)
        {
            int iMsgCheckSum = 0;
            for (int i = 0; i < paryMessage.Length - 2; i++)
            {
                iMsgCheckSum += paryMessage[i];
            }

            int iMSB = Convert.ToInt32(iMsgCheckSum / 256);
            int iLSB = iMsgCheckSum % 256;

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
                // TODO : log error   
                // ln("Checksumvalidation " + warningmessage, EventLogEntryType.FailureAudit);
            }
        }

        protected override void heartbeat_timer_callback(object sender)
        {
            base.heartbeat_timer_callback(sender);
            byte[] buffer = new byte[] { kzerobyte, kackbyte, kzerobyte, kackbyte };
            sendserial(buffer);
        }

        public override void OnStartUp()
        {
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
        public virtual void send_message(ActionType action, NwmData type, string passedvalues)
        {
            string[] parts = passedvalues.Split(',');

            int node = 1, loop = 0, zone = 0, device = 0, giDomainNumber = 0, inputtype = 0;

            if (parts.Length > 0) int.TryParse(parts[0], out node);
            if (parts.Length > 1) int.TryParse(parts[1], out loop);
            if (parts.Length > 2) int.TryParse(parts[2], out zone);
            if (parts.Length > 3) int.TryParse(parts[3], out device);

            DateTime now = DateTime.Now;

            int iDayOfWeek = (int)now.DayOfWeek; // Sunday = 0, Monday = 1, etc.
            iDayOfWeek++;

            int sHour = now.Hour;
            int sMinute = now.Minute;
            int sSecond = now.Second;

            int sYear = int.Parse(now.ToString("yy"));   // Two-digit year
            int sMonth = int.Parse(now.ToString("MM"));  // Two-digit month
            int sDay = int.Parse(now.ToString("dd"));   // Two-digit day

            byte[] gbaryDataToTX = new byte[60];

            string text = action.ToString();

            for (int i = 1; i <= 9; i++)
            {
                gbaryDataToTX[i] = 0;
            }

            if (action == ActionType.kEVACTUATE)
            {
                gbaryDataToTX[0] = 32;
                text = "Alarms Sounded";
                inputtype = 248;
                node = 1;
                loop = 0;
                device = 1;
            }

            if (action == ActionType.kRESET)
            {
                gbaryDataToTX[0] = 20;
                text = "Alarms Reset";
                inputtype = 248;
                node = 1;
                loop = 0;
                device = 1;
            }

            if (action == ActionType.kSILENCE)
            {
                gbaryDataToTX[0] = 16;
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
            }

            if (action == ActionType.kDISABLEZONE)
            {
                gbaryDataToTX[0] = 144;
                gbaryDataToTX[1] = 4;
                gbaryDataToTX[8] = (byte)zone;
                gbaryDataToTX[9] = (byte)loop;
                text = "Disable Zone";
                inputtype = 4;
            }

            if (action == ActionType.kENABLEZONE)
            {
                gbaryDataToTX[0] = 144;
                gbaryDataToTX[1] = 1;
                gbaryDataToTX[8] = (byte)zone;
                gbaryDataToTX[9] = (byte)loop;
                text = "Enable Zone";
                inputtype = 4;
            }
            gbaryDataToTX[10] = (byte)node;
            gbaryDataToTX[11] = (byte)sSecond;
            gbaryDataToTX[12] = (byte)sMinute;
            gbaryDataToTX[13] = (byte)sHour;
            gbaryDataToTX[14] = (byte)iDayOfWeek;
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

            sendserial(gbaryDataToTX);
            SendEvent("Gent", type, inputtype, text, node, loop, device);
        }

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
        }
    }

}