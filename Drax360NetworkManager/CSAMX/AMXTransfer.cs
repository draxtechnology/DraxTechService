using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace Drax360Service
{
    internal class AMXTransfer
    {
        private int _port = 3090;
        private string _address = "localhost";
        private TcpClient _tcpClient;
        public TcpClient TcpClient => _tcpClient;
        private bool _connected = false;
        public bool IsConnected { get; private set; }

        private NetworkStream _stream;
        private StreamWriter _writer;
        private System.Timers.Timer _heartbeatTimer;

        public event Action<string> isMessageReceive;

        private static AMXTransfer _instance;
        private static readonly object _lock = new object();
        public async Task Run(string[] args)
        {
            await tcpconnect();
        }
        private async Task tcpconnect()
        {
            _tcpClient = new TcpClient();
            var cancellationTokenSource = new CancellationTokenSource();
            _tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            _tcpClient.ReceiveTimeout = 3000;
            _tcpClient.SendTimeout = 3000;
            try
            {
                var connectTask = Task.Run(() => _tcpClient.ConnectAsync(_address, _port), cancellationTokenSource.Token);
                var timeoutTask = Task.Delay(5000); // 5-second timeout
                                                    // Wait for either the connection to succeed or the timeout to occur
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    cancellationTokenSource.Cancel();
                    Debug.WriteLine("Connection timeout.");
                    return;
                }
                _connected = true;
                IsConnected = true;
                _stream = _tcpClient.GetStream();
                _writer = new StreamWriter(_stream, Encoding.UTF8) { AutoFlush = true };
                StartHeartbeatTimer();

                // Log the startup

                int evnum = CSAMXSingleton.CS.MakeInputNumber(1, 1, 1, 1, true);
                string text = "c# Gent Started";
                CSAMXSingleton.CS.WriteData(NwmData.MessageForSystemHistoryToAmx, evnum, text, "", "", true);
                CSAMXSingleton.CS.FlushMessages();

                isMessageReceive += msg =>
                {
                    Console.WriteLine("Received From AMX: " + msg);
                    if (msg.StartsWith("NWM:") || msg.StartsWith("GEN:"))
                    {
                        DraxService drax = new DraxService();
                        drax.sendreturncmd("", msg);
                    }
                };
                
                await ReceiveDataAsync();
            }
            catch (Exception ex)
            {
                _connected = false;
                Debug.WriteLine("Connection failed: " + ex.Message);
            }
        }
        private void StartHeartbeatTimer()
        {
            _heartbeatTimer = new System.Timers.Timer(1000); // 1 second interval
            _heartbeatTimer.Elapsed += HeartbeatTimer_Elapsed;
            _heartbeatTimer.AutoReset = true; // keep firing every second
            _heartbeatTimer.Enabled = true;
        }

        private void HeartbeatTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (_tcpClient != null && _tcpClient.Connected)
            {
                SendMessage("?");  // Send your heartbeat query every second
                Console.WriteLine("Sent AMX Heartbeat ?");
            }
        }
        public void SendMessage(string message)
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

                if (!_connected && attempt < maxAttempts)
                {
                    Debug.WriteLine($"Not connected unable to sennd");
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

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception in ReceiveDataAsync: " + ex.Message);
            }
        }
        public static AMXTransfer Instance
        {
            get
            {
                lock (_lock)
                {
                    return _instance ??= new AMXTransfer();
                }
            }
        }
    }
}
