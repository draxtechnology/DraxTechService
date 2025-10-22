using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;

string gsIPAddress = "10.0.11.100";
int gsIPPort = 100;

using (var client = new TcpClient())
{
    client.Connect(gsIPAddress, gsIPPort);
    using (var stream = client.GetStream())
    {
        // send initial request TAKSendRequestActEvents
        string[] tosend = null;

        tosend = new string[8];
        for (int i = 0; i < 8 - 1; i++)
            tosend[i] = "0";
        tosend[3] = 8.ToString();
        tosend[7] = 79.ToString();


        byte[] data = convertstringarraytobytearray(tosend);

        stream.Write(data, 0, data.Length);
        stream.Flush();

        Thread.Sleep(1000); // wait for response

        // Read the initial response
        var responseBuffer = new byte[1024];
        int bytesRead = stream.Read(responseBuffer, 0, responseBuffer.Length);

        string response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);
        Console.WriteLine("Initial Received response: " + response);

        // ACK 
        tosend = new string[12];
        for (int i = 0; i < 12 - 1; i++)
            tosend[i] = "0";
        tosend[3] = 12.ToString();
        tosend[7] = 134.ToString();
        tosend[10] = 2.ToString();
        tosend[11] = 72.ToString();

        data = convertstringarraytobytearray(tosend);

        stream.Write(data, 0, data.Length);
        stream.Flush();

        Thread.Sleep(1000); // wait for response

        int counter = 73;

        while (stream.DataAvailable)
        {
            if (stream.DataAvailable)
            {
                bytesRead = stream.Read(responseBuffer, 0, responseBuffer.Length);
                response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);
                Console.WriteLine("Subsequent Received response: " + response);


                // ACK 
                tosend = new string[12];
                for (int i = 0; i < 12 - 1; i++)
                    tosend[i] = "0";
                tosend[3] = 12.ToString();
                tosend[7] = 134.ToString();
                tosend[10] = 2.ToString();
                tosend[11] = counter.ToString();

                data = convertstringarraytobytearray(tosend);

                stream.Write(data, 0, data.Length);
                stream.Flush();

                Thread.Sleep(1000); // wait for response

                counter++;
            }


        } 
    }

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