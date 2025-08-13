using System;
using System.IO;
using System.IO.Ports;
using System.Threading;

class Program
{
    static SerialPort serialPort;
    static string logFile = "c:\\temp\\serial_log.txt";

    static void Main()
    {
        serialPort = new SerialPort("COM3", 19200, Parity.Even, 8, StopBits.One);
        serialPort.Handshake = Handshake.None;
        serialPort.DtrEnable = true; // VB6 default
        serialPort.RtsEnable = true; // VB6 default

        try
        {
            serialPort.Open();
            Log("Program started, port open: " + serialPort.PortName);

            // Send back 0x06 0x06 (change here if you want 00-06-00-06)
            byte[] start = { 0x0, 0x06, 0x0, 0x06 };
            serialPort.Write(start, 0, start.Length);

            while (true)
            {
                if (serialPort.BytesToRead > 0)
                {
                    // Read all available bytes
                    byte[] buffer = new byte[serialPort.BytesToRead];
                    serialPort.Read(buffer, 0, buffer.Length);

                    string receivedHex = BitConverter.ToString(buffer);
                    Console.WriteLine(System.DateTime.Now.ToString() + " Received: " + receivedHex);
                    Log("Received: " + receivedHex);

                    // Send back 0x06 0x06 (change here if you want 00-06-00-06)
                    byte[] reply = { 0x0, 0x06, 0x0, 0x06 };
                    serialPort.Write(reply, 0, reply.Length);

                    string sentHex = BitConverter.ToString(reply);
                    Console.WriteLine(System.DateTime.Now.ToString() + " Sent: " + sentHex);
                    Log("Sent: " + sentHex);
                }

                Thread.Sleep(500); // Mimics VB6 timer interval
            }
        }
        catch (Exception ex)
        {
            Log("Error: " + ex.Message);
        }
    }

    private static void Log(string message)
    {
        string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}";
        File.AppendAllText(logFile, logEntry + Environment.NewLine);
    }
}
