using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Remoting.Messaging;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace Drax360Service
{
    public class Program
    {
        private static int _port = 3090;
        private static string _address = "localhost";
        private static TcpClient _tcpClient;
        private static bool _connected = true;

        private static NetworkStream _stream;
        private static StreamWriter _writer;
        private static StreamReader _reader;
        private static CancellationTokenSource _cts;
        //private DraxService draxService = new DraxService();


        public event Action<string> isMessageReceive;
        private static System.Timers.Timer _heartbeatTimer;
        public static bool IsConnected => _connected;
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main(string[] args)
        {
            if (Environment.UserInteractive)
            {

                DraxService service = new DraxService();
                service.Run(args);
                tcpconnect();
                waitcr();
                service.Stopit();
                service.Dispose();
            }
            else
            {


                ServiceBase[] ServicesToRun;
                ServicesToRun = new ServiceBase[]
                {
                new DraxService()
                };
                ServiceBase.Run(ServicesToRun);
            }
        }


        private static void waitcr()
        {
            string msg = "Waiting For CR";
            string line = "".PadRight(msg.Length, '-');
            Console.WriteLine(line);
            Console.WriteLine(msg);
            Console.WriteLine(line);
            Console.ReadLine();

        }

        private static async void tcpconnect()
        {
            _port = 3090;
            _address = "localhost";

            _tcpClient = new TcpClient();
            var cancellationTokenSource = new CancellationTokenSource();
            _tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            _tcpClient.ReceiveTimeout = 3000;  // 5 seconds
            _tcpClient.SendTimeout = 3000;     // 5 seconds
            try
            {
                var connectTask = Task.Run(() => _tcpClient.Connect(_address, _port), cancellationTokenSource.Token);
                var timeoutTask = Task.Delay(5000); // 5-second timeout
                                                    // Wait for either the connection to succeed or the timeout to occur
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    cancellationTokenSource.Cancel();
                    Debug.WriteLine("Connection timeout.");
                    return;
                }
                //await _tcpClient.ConnectAsync(_address, _port);
                _connected = true;
                _stream = _tcpClient.GetStream();
                _writer = new StreamWriter(_stream, Encoding.UTF8) { AutoFlush = true };
                _reader = new StreamReader(_stream, Encoding.UTF8);
                StartHeartbeatTimer();

                //Log the startup

                //int evnum = CSAMXSingleton.CS.MakeInputNumber(1, 1, 1, 1, true);
                //string text = "c# Gent Started";
                //CSAMXSingleton.CS.WriteData(NwmData.MessageForSystemHistoryToAmx, evnum, text, "", "", true);
                //CSAMXSingleton.CS.FlushMessages();

                var program = new Program();
                program.isMessageReceive += msg => Console.WriteLine("Received From AMX: " + msg);
                await program.ReceiveDataAsync();
            }
            catch (Exception ex)
            {
                _connected = false;
                Debug.WriteLine("Connection failed: " + ex.Message);
            }
        }

        private static void StartHeartbeatTimer()
        {
            _heartbeatTimer = new System.Timers.Timer(1000); // 1 second interval
            _heartbeatTimer.Elapsed += HeartbeatTimer_Elapsed;
            _heartbeatTimer.AutoReset = true; // keep firing every second
            _heartbeatTimer.Enabled = true;
        }

        private static void HeartbeatTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (_tcpClient != null && _tcpClient.Connected)
            {
                SendMessage("?");  // Send your heartbeat query every second
                Console.WriteLine("Sent AMX Heartbeat ?");
            }
        }

        public static void SendMessage(string message)
        {
            const int maxAttempts = 3;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (_connected && _tcpClient != null && _tcpClient.Connected && _stream != null)
                {
                    try
                    {
                        if (_stream.CanWrite)
                        {
                            byte[] data = Encoding.UTF8.GetBytes(message);
                            _stream.Write(data, 0, data.Length);
                            _stream.Flush();
                            return; // success, exit method
                        }
                        else
                        {
                            Debug.WriteLine("Stream is not writable.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Send attempt {attempt} failed: {ex.Message}");
                        _connected = false;
                    }
                }

                // Try reconnecting if not connected and not on final attempt
                if (!_connected && attempt < maxAttempts)
                {
                    Debug.WriteLine($"Attempting to reconnect... (Attempt {attempt + 1})");
                    tcpconnect();
                }
            }

            Debug.WriteLine("SendMessage failed after 3 attempts.");
        }

        public async Task ReceiveDataAsync()
        {
            try
            {
                if (_tcpClient == null || !_tcpClient.Connected)
                    return;

                var buffer = new byte[1024];
                var stream = _tcpClient.GetStream();

                while (_tcpClient.Connected)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

                    if (bytesRead == 0)
                    {
                        Console.WriteLine("Server closed connection");
                        break;
                    }

                    string chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    isMessageReceive?.Invoke(chunk.Trim());

                    string cmd = chunk.Substring(0, Math.Min(4, chunk.Length));
                    string par = chunk.Substring(Math.Min(4, chunk.Length));

                    switch (cmd)
                    {
                        case "NWM:":  //NWM = Commands recognised by any NWM 

                            switch (par)
                            {
                                case "TBSHOW":  //Look for command to show test box 
                                    ShowTestBox();
                                    break;
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception in ReceiveDataAsync: " + ex.Message);
            }
        }
        private void ShowTestBox()
        {
            SendMessage("TBSHOW");
        }
    }
}