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
        
        private long serialnumber = -1;
        private byte[] serialnumberbytes = new byte[4];

        public void Run()
        {
            //client = new TcpClient();



            // send initial request TAKSendRequestActEvents
            write(sendinitialstring);
            readsusbsequent();

            // todo go get the serial number from the initial response

            // send start monitoring

            Console.WriteLine("Start Monitoring");
            write(sendstartmonitoringstring);
            readsusbsequent();


            write(sendstartmonitoringstring);
            readsusbsequent();

            write(sendstartstring);
            while (true)
            {
                Console.WriteLine("Heartbeat");
                write(sendheartbeatstring);
                readsusbsequent();


                //write(sendstartstring);
                //readsusbsequent();
                //write(sendstopstring);

            }

        }

        private void write(string[] towrite)
        {
            if (client==null)
            {
                client = new TcpClient();

                client.Connect(gsIPAddress, gsIPPort);
                stream = client.GetStream();
            }

            //if (!client.Connected) return;
            byte[] data = convertstringarraytobytearray(towrite);

            try
            {
                stream.Write(data, 0, data.Length);
                stream.Flush();

            }
            catch (Exception)
            {

            }
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
                for (int i = 0; i < 12 - 1; i++)
                    tosend[i] = "0";
                tosend[3] = 12.ToString();
                tosend[7] = CMD_EVENT_ACK;
                tosend[8] =  serialnumberbytes[0].ToString();
                tosend[9] = serialnumberbytes[1].ToString();
                tosend[10] = serialnumberbytes[2].ToString();
                tosend[11] = serialnumberbytes[3].ToString();



                return tosend;
            }
        }
        private string[] sendinitialstring
        {
            get
            {
                string[] tosend = new string[8];
                for (int i = 0; i < 8 - 1; i++)
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
                for (int i = 0; i < 11 - 1; i++)
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
                for (int i = 0; i < 8 - 1; i++)
                    tosend[i] = "0";
                tosend[3] = 12.ToString();
                tosend[7] = CMD_REQUEST_EVENT_LOG;
                tosend[8] = serialnumberbytes[0].ToString();
                tosend[9] = serialnumberbytes[1].ToString();
                tosend[10] = serialnumberbytes[2].ToString();
                tosend[11] = serialnumberbytes[3].ToString();


                return tosend;

            }
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
                    bytesRead = stream.Read(responseBuffer, 0, responseBuffer.Length);
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
                Console.WriteLine("Subsequent Received response: " + response+ " read ="+bytesRead);
                //if (serialnumber==-1)
                //{
                    // get serial number from response
                    decodeserialnumberfromresponse(responseBuffer, bytesRead);
                //}

                Thread.Sleep(1000); // wait for response
                write(sendackstring);
                
                counter++;
            }
            Console.WriteLine("Exited read loop Connected = "+client.Connected);
            client.Close();
            client.Dispose();
            client = null;


        }

        private void decodeserialnumberfromresponse(byte[] aryHexMessage,int messagelength)
        {
            serialnumber = 0;
            int start = 16;
            long ourvalue = 0;

            if (messagelength == 8)
            {
                return;

            }

            if (messagelength==248)
            {
                start = 8;

            }

            int messagetype = aryHexMessage[7];
            if (messagetype == 133) return; // ack message no serial number

            serialnumber += Convert.ToInt64(aryHexMessage[start]) << 3;
            ourvalue = base10Tohexasint(aryHexMessage[start]);
            serialnumberbytes[0] = (byte)ourvalue;
            start++;
            
            serialnumber += Convert.ToInt64(aryHexMessage[start]) << 2;
            ourvalue = base10Tohexasint(aryHexMessage[start]);
            serialnumberbytes[1] = (byte)ourvalue;
            start++;

            serialnumber += Convert.ToInt64(aryHexMessage[start]) << 1;
            ourvalue = base10Tohexasint(aryHexMessage[start]);
            serialnumberbytes[2] = (byte)ourvalue;
            start++;

            serialnumber += Convert.ToInt64(aryHexMessage[start]);
            ourvalue = base10Tohexasint(aryHexMessage[start]);
            serialnumberbytes[3] = (byte)ourvalue;

               
        }

        private int base10Tohexasint(byte base10val)

        { 
            string stringhex = base10val.ToString("X");
            return int.Parse(stringhex);
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