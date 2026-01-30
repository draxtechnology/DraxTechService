
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace DraxTechnology.Panels
{
    internal class PanelRSM : AbstractPanel
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

                return msg;
            }
        }
       

        public PanelRSM(string baselogfolder, string identifier) : base(baselogfolder,identifier, "RSMMan","RSM")
        {
            if (!String.IsNullOrEmpty(identifier))
            {
                heartbeat_timer = new System.Threading.Timer(heartbeat_timer_callback, this.Identifier, 500, kheartbeatdelayseconds * 1000);
                this.Offset = base.GetSetting<int>(ksettingsetupsection, "giAmx1Offset");
            }
        }

        public byte[] Parse(byte[] buffer)
        {

            // just for cosmetics - display the raw hex data
            string hexData = BitConverter.ToString(buffer).Replace("-", " ");
            Console.WriteLine(hexData);

            string decodedData = decodedata(buffer);
            Console.WriteLine(decodedData);
            // Send ACK response
            string ackResponse = generateackresponse(decodedData);
            if (!string.IsNullOrEmpty(ackResponse))
            {
                byte[] ackBytes = scrambleandencodemessage(ackResponse);
                return ackBytes;
            }
            else
            {
                // No ACK generated
                return new byte[0];

            }
        }


        private byte[] scrambleandencodemessage(string message)
        {
            // Replace semicolons back from our display format
            message = message.Replace(";", new string((char)0x3B, 1));

            // Calculate checksum
            int checksum = 0;
            foreach (char c in message)
            {
                checksum += (int)c;
            }
            checksum = (checksum % 200) + 33;

            // Scramble the message (reverse of descramble)
            // First reverse the string
            char[] chars = message.ToCharArray();
            Array.Reverse(chars);
            string reversed = new string(chars);

            // Then apply the scramble formula
            List<byte> scrambled = new List<byte>();
            for (int n = 1; n <= reversed.Length; n++)
            {
                int charValue = (int)reversed[n - 1];
                int encoded = charValue + 3 + (n % 9) + ((n % 5) * 7);

                // Handle wrap-around
                while (encoded > 255)
                    encoded -= 256;

                scrambled.Add((byte)encoded);
            }

            // Add STX header, scrambled data, checksum, and ETX
            List<byte> fullMessage = new List<byte>();
            fullMessage.Add(0x02); // STX
            fullMessage.AddRange(scrambled);
            fullMessage.Add((byte)checksum);
            fullMessage.Add(0x03); // ETX

            return fullMessage.ToArray();
        }


        private string decodedata(byte[] data)
        {
            if (data.Length < 3)
                return Encoding.ASCII.GetString(data);

            // VB6 strips STX (0x02) and ETX before descrambling
            // Find and remove STX (0x02) and ETX (0x03) bytes
            List<byte> cleanedData = new List<byte>();
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] != 0x02 && data[i] != 0x03)
                {
                    cleanedData.Add(data[i]);
                }
            }

            if (cleanedData.Count < 2)
                return "";

            // Now the data is just the scrambled content + checksum (last byte)
            int dataLength = cleanedData.Count - 1; // Exclude checksum
            int checksumByte = cleanedData[cleanedData.Count - 1];

            // Descramble the string
            StringBuilder descrambled = new StringBuilder();
            for (int n = 1; n <= dataLength; n++)
            {
                int byteValue = cleanedData[n - 1]; // C# is 0-based, VB6 loop is 1-based
                int decoded = byteValue - 3 - (n % 9) - ((n % 5) * 7);

                // Handle wrap-around for negative values (VB6 byte range is 0-255)
                while (decoded < 0)
                    decoded += 256;
                while (decoded > 255)
                    decoded -= 256;

                descrambled.Append((char)decoded);
            }

            // Reverse the string
            char[] chars = descrambled.ToString().ToCharArray();
            Array.Reverse(chars);
            string reversed = new string(chars);

            // Calculate and confirm checksum
            int calculatedChecksum = 0;
            for (int n = 0; n < reversed.Length; n++)
            {
                calculatedChecksum += (int)reversed[n];
            }
            calculatedChecksum = (calculatedChecksum % 200) + 33;

            // Replace semicolons with Ç to match VB6 output
            string result = reversed.Replace(";", "Ç");

            // Checksum validation (optional display)
            if (calculatedChecksum != checksumByte)
            {
                result += $" [CHECKSUM ERROR: Expected {checksumByte}, Got {calculatedChecksum}]";
            }

            return result;
        }


        // Mike suggest we might want to return these values too
        //private string generateackresponse(string decodedMessage, out string messageType, out string messageID, out string moduleNumber)

        private string generateackresponse(string decodedMessage )
        {
            // Parse the decoded message to extract: MessageType, ModuleNumber, MessageID
            // Format: EVTÇ3159Ç1ÇB19810252D...
            // or: POLÇxxxx...

            // Remove checksum error text if present
            if (decodedMessage.Contains("[CHECKSUM ERROR"))
            {
                decodedMessage = decodedMessage.Substring(0, decodedMessage.IndexOf("[CHECKSUM ERROR")).Trim();
            }

            string[] parts = decodedMessage.Split('Ç');

            if (parts.Length < 3)
                return ""; // Not enough parts to generate ACK

            string messageType = parts[0];
            string messageID = parts[1];
            string moduleNumber = parts[2];

            // Generate ACK based on message type
            switch (messageType)
            {
                case "EVT":
                case "ZTX":
                case "ANA":
                case "SPX":
                    // ACK format: ACKÇModuleNumberÇMessageID
                    return $"ACK\u00C7{moduleNumber}\u00C7{messageID}";

                case "POL":
                    // PAK format: PAKÇModuleNumberÇMessageIDÇLicenseStatus
                    // For now, return license status 0 (valid)
                    return $"PAK\u00C7{moduleNumber}\u00C7{messageID}\u00C70";

                default:
                    // For unknown messages, send generic ACK
                    //return $"ACK;{moduleNumber};{messageID}";
                    return $"ACK\u00C7{moduleNumber}\u00C7{messageID}";
            }
        }




        private bool processmessage(byte[] chunk)
        {
            string hex = BitConverter.ToString(chunk);
            this.NotifyClient("Received: " + hex, false);

  
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
        }

        public override void StartUp(int fakemode)
        {     
            if (fakemode > 0)
            {
                return;
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
 
            node = node + this.Offset;
            string text = "";
            SendEvent("Gent", type, inputtype, text, on, node, loop, device);
        }
    }
}