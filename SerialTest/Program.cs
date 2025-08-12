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
        serialPort.DtrEnable = true;  
        serialPort.RtsEnable = false;

        serialPort.DataReceived += SerialPort_DataReceived;

        try
        {
            serialPort.Open();
            Console.WriteLine("Listening on " + serialPort.PortName + "...");
            Console.WriteLine("Press Ctrl+C to exit.");
            Log("Program started.");

            byte[] reply = { 0x00, 0x06, 0x00, 0x06 };
            serialPort.Write(reply, 0, reply.Length);
            serialPort.BaseStream.Flush();

            string sentHex = BitConverter.ToString(reply);
            Console.WriteLine("Sent: " + sentHex);
            Log("Sent: " + sentHex);

            while (true) Thread.Sleep(100);
        }
        catch (Exception ex)
        {
            Log("Error: " + ex.Message);
        }
    }

    private static void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            int bytes = serialPort.BytesToRead;
            byte[] buffer = new byte[bytes];
            serialPort.Read(buffer, 0, bytes);

            string receivedHex = BitConverter.ToString(buffer);
            Console.WriteLine("Received: " + receivedHex);
            Log("Received: " + receivedHex);

            byte[] reply = { 0x00, 0x06, 0x00, 0x06 };
            serialPort.Write(reply, 0, reply.Length);
            serialPort.BaseStream.Flush();

            string sentHex = BitConverter.ToString(reply);
            Console.WriteLine("Sent: " + sentHex);
            Log("Sent: " + sentHex);
        }
        catch (Exception ex)
        {
            Log("Send Error: " + ex.Message);
        }
    }

    private static void Log(string message)
    {
        string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}";
        File.AppendAllText(logFile, logEntry + Environment.NewLine);
    }
}
