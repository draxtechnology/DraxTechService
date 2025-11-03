using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using static System.Formats.Asn1.AsnWriter;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Globalization;

namespace Taktis_Receive
{
    internal class Runner
    {
        string gsIPAddress = "10.0.11.100";
        int gsIPPort = 100;

        public const string CMD_REQUEST_EVENT_LOG = "78";
        public const string CMD_REQUEST_ACTIVE_EVENTS = "79";
        public const string CMD_HEARTBEAT = "86";
        public const string CMD_EVENT_ACK = "134";
        public const string CMD_START_MONITORING = "231";
        public const string CMD_STOP_MONITORING = "244";
        TcpClient client;
        NetworkStream stream;

        private long[] glSerialNo = new long[4];

        public void Run()
        {
            //client = new TcpClient();

            if (client == null)
            {
                client = new TcpClient();

                client.Connect(gsIPAddress, gsIPPort);
                stream = client.GetStream();
            }

            write(sendinitialstring);
            Thread.Sleep(500); // wait for response
            readinitial();

            // send start monitoring

            Console.WriteLine("Start Monitoring");

            write(sendstartstring);

            write(sendstartmonitoringstring);
            readsusbsequent();

            while (true)
            {
                Console.WriteLine("Heartbeat");
                write(sendheartbeatstring);
                readsusbsequent();
            }
        }

        private void write(string[] towrite)
        {
            if (client==null || !client.Connected )
            {
                client = new TcpClient();
           
                client.Connect(gsIPAddress, gsIPPort);
                stream = client.GetStream();
           }

            byte[] data = convertstringarraytobytearray(towrite);

            try
            {
                stream.Write(data, 0, data.Length);
                stream.Flush();
                string logFilePath = @"C:\temp\c#log.txt";
                string hexString = BitConverter.ToString(data);
                string decimalString = string.Join(" ", data);
                File.AppendAllText(logFilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} Sent: {decimalString}{Environment.NewLine}");

            }
            catch (Exception)
            {}
        }

        private string[] sendstartstring
        {
            get
            {
                string[] tosend = new string[12];
                for (int i = 0; i < 12 - 1; i++)
                    tosend[i] = "0";
                tosend[3] = 12.ToString();
                tosend[7] = CMD_START_MONITORING;
                tosend[11] = "1";

                return tosend;
            }
        }

        private string[] sendstopstring
        {
            get
            {
                string[] tosend = new string[12];
                for (int i = 0; i < 12 - 1; i++)
                    tosend[i] = "0";
                tosend[3] = 12.ToString();
                tosend[7] = CMD_STOP_MONITORING;
                tosend[11] = "1";

                return tosend;
            }
        }

        private string[] sendackstring
        {
            get
            {
                string[] tosend = new string[12];
                for (int i = 0; i < 12; i++)
                    tosend[i] = "0";
                tosend[3] = 12.ToString();
                tosend[7] = CMD_EVENT_ACK;
                tosend[8] = glSerialNo[0].ToString();
                tosend[9] = glSerialNo[1].ToString();
                tosend[10] = glSerialNo[2].ToString();
                tosend[11] = glSerialNo[3].ToString();

                return tosend;
            }
        }
        private string[] sendinitialstring
        {
            get
            {
                string[] tosend = new string[8];
                for (int i = 0; i < 8; i++)
                    tosend[i] = "0";
                tosend[3] = 8.ToString();
                tosend[7] = CMD_REQUEST_ACTIVE_EVENTS;
                return tosend;

            }
        }

        private string[] sendheartbeatstring
        {
            get
            {
                string[] tosend = new string[12];
                for (int i = 0; i < 12; i++)
                    tosend[i] = "0";
                tosend[3] = 12.ToString();
                tosend[7] = CMD_HEARTBEAT;
                tosend[11] = "1";

                return tosend;
            }
        }

        private string[] sendstartmonitoringstring
        {
            get
            {
                string[] tosend = new string[12];
                for (int i = 0; i < 12; i++)
                    tosend[i] = "0";
                tosend[3] = 12.ToString();
                tosend[7] = CMD_REQUEST_EVENT_LOG;
                tosend[8] = glSerialNo[0].ToString();
                tosend[9] = glSerialNo[1].ToString();
                tosend[10] = glSerialNo[2].ToString();
                tosend[11] = glSerialNo[3].ToString();

                return tosend;
            }
        }

        private void readinitial()

        {
            int counter = 1;

            while (stream.DataAvailable)
            {
                byte[] responseBuffer = new byte[1024];
                int bytesRead = 0;

                try
                {
                    if (stream.DataAvailable)
                    {
                        bytesRead = stream.Read(responseBuffer, 0, responseBuffer.Length);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    break;
                }

                if (bytesRead == 0)
                {
                    continue;
                }

                string response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);
                Console.WriteLine("Initial Received Response: " + response + " read = " + bytesRead);

                // get serial number from response
                decodeserialnumberfromresponse(responseBuffer, bytesRead);

                write(sendackstring);
                Thread.Sleep(500); // wait for response

                counter++;
            }
            Console.WriteLine("Exited Initial read loop Connected = "+client.Connected);
        }

        private void readsusbsequent()

        {
            int counter = 1;

            while (client.Connected)
            {
                byte[] responseBuffer = new byte[1024];
                int bytesRead = 0;

                try
                {
                    if (stream.DataAvailable)
                    {
                        bytesRead = stream.Read(responseBuffer, 0, responseBuffer.Length);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    break;
                }

                if (bytesRead == 0)
                {
                    continue;
                }

                string response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);
                Console.WriteLine("Subsequent Received Response: " + response + " read = " + bytesRead);

                // get serial number from response
                decodeserialnumberfromresponse(responseBuffer, bytesRead);

                Thread.Sleep(1000); // wait for response
                write(sendackstring);

                counter++;
            }
            Console.WriteLine("Exited Subsuquent read loop Connected = " + client.Connected);
            //client.Close();
            //client.Dispose();
            //client = null;
        }

        private void decodeserialnumberfromresponse(byte[] aryHexMessage,int messagelength)
        {
            int start = 8;

            if (messagelength == 8)
            {
                return;
            }

            if (messagelength == 256)
            {
                start = 16;
            }

            string sMessageType = "";

            for (int i = start - 4; i <= start - 1; i++)
            {
                sMessageType += aryHexMessage[i].ToString("X2"); // format as 2-digit hex
            }

            glSerialNo[0] = aryHexMessage[start];
            start++;

            glSerialNo[1] = aryHexMessage[start];
            start++;

            glSerialNo[2] = aryHexMessage[start];
            start++;

            glSerialNo[3] = aryHexMessage[start];

        }
        byte[] convertstringarraytobytearray(string[] stringArray)
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
}