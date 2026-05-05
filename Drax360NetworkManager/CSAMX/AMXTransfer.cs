using DraxTechnology.Panels;
using System;
using System.Collections.Concurrent;
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

namespace DraxTechnology
{
    internal class AMXTransfer
    {
        const char kpipedelim = '|';

        public event EventHandler OutsideEvents;

        private int _port = 3090;
        private string _address = "localhost";
        private TcpClient _tcpClient;
        public TcpClient TcpClient => _tcpClient;
        private bool _connected = false;
        public bool IsConnected { get; private set; }

        private NetworkStream _stream;
        private StreamWriter _writer;
        private System.Timers.Timer _heartbeatTimer;

        // Single-writer outbound queue: every SendMessage call enqueues here,
        // and a dedicated sender thread drains the queue. NetworkStream.Write
        // is not safe for concurrent writers, so funnelling all writes through
        // one thread prevents byte-level interleaving between the heartbeat
        // timer, inbound echo handler and panel parser callbacks.
        private readonly BlockingCollection<string> _outbound = new BlockingCollection<string>();
        private Thread _senderThread;
        private CancellationTokenSource _senderCts;

        // Set by Stop() so the Run reconnect loop and sender thread exit cleanly
        // when DraxService.OnStop runs. Without this the while(true) in Run kept
        // looping until the process died — fine for a normal service stop, but
        // it leaked _senderCts and _outbound on a programmatic Stop().
        private volatile bool _stopRequested;

        public event Action<string> isMessageReceive;

        private static AMXTransfer _instance;
        private static readonly object _lock = new object();

        // Reconnect backoff between attempts. The sender thread and outbound
        // queue persist across reconnects; messages enqueued while disconnected
        // get the existing 3-attempt retry inside WriteToStream and are then
        // discarded — the queue does not stack up unbounded.
        private const int kReconnectDelayMs = 5000;

        public async Task Run(string[] args)
        {
            while (!_stopRequested)
            {
                try
                {
                    await tcpconnect();
                }
                catch (Exception ex)
                {
                    NotifyClient("AMX connect error: " + ex.Message);
                }

                CleanupConnection();
                if (_stopRequested) break;

                NotifyClient("AMX disconnected; reconnecting in " + (kReconnectDelayMs / 1000) + "s");
                await Task.Delay(kReconnectDelayMs);
            }

            NotifyClient("AMX Run loop exited");
        }

        public void Stop()
        {
            _stopRequested = true;
            try { _outbound.CompleteAdding(); } catch { }
            try { _senderCts?.Cancel(); } catch { }
            CleanupConnection();
        }

        private void CleanupConnection()
        {
            _connected = false;
            IsConnected = false;

            try { _heartbeatTimer?.Stop(); _heartbeatTimer?.Dispose(); } catch { }
            _heartbeatTimer = null;

            try { _stream?.Dispose(); } catch { }
            _stream = null;

            try { _tcpClient?.Close(); } catch { }
            _tcpClient = null;

            // Drop any leftover ack signal so the next NTX send waits for a
            // fresh MAK from the new connection rather than racing on a stale set.
            _makAck.Reset();
        }

        // Add near the top of the class
        private static readonly ManualResetEventSlim _makAck = new ManualResetEventSlim(false);

        public static void WaitForMak()
        {
            _makAck.Wait();
        }

        public static void ResetMak()
        {
            _makAck.Reset();
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
                    NotifyClient("AMX Connection timeout");
                    return;
                }
                _connected = true;
                IsConnected = true;
                _stream = _tcpClient.GetStream();
                _writer = new StreamWriter(_stream, Encoding.UTF8) { AutoFlush = true };
                StartSender();
                StartHeartbeatTimer();

                // Log the startup

                int evnum = CSAMXSingleton.CS.MakeInputNumber(1, 1, 1, 1);
                string text = "c# Gent Started";
                CSAMXSingleton.CS.WriteData(NwmData.MessageForSystemHistoryToAmx, evnum, text, "", "");
                CSAMXSingleton.CS.FlushMessages();

                // Single subscription per connection. On reconnect we clear the
                // event first so handlers don't accumulate.
                isMessageReceive = null;
                isMessageReceive += msg =>
                {
                    NotifyClient("Received From AMX: " + msg);

                    // Prefix-tagged messages are mutually exclusive; the
                    // pipe-delimited graphic command is a separate message
                    // type. Use else-if so a "NWM:foo|bar" frame doesn't
                    // double-dispatch.
                    if (msg.StartsWith("NWM:") || msg.StartsWith("GEN:"))
                    {
                        DraxService drax = new DraxService();
                        drax.sendreturncmd("", msg);
                    }
                    else if (msg.StartsWith("MAK:"))
                    {
                        // "MAK:" is 4 chars — the previous Substring(9) lopped off
                        // 5 extra characters of the file path so the delete silently
                        // missed.
                        string filename = msg.Substring(4).Trim();
                        filename = filename.Replace("-", "").Trim();
                        if (System.IO.File.Exists(filename))
                        {
                            System.IO.File.Delete(filename);
                        }
                        _makAck.Set();  // releases the sender thread's WaitForMak
                    }
                    else if (msg.StartsWith("MTX:"))
                    {
                        // Manual Controls from AMX. The path points at a 224-byte
                        // .MTN file in NVM struct format. Decode and dispatch to
                        // the active panel(s) via DraxService; then preserve the
                        // existing echo-back so AMX's handshake is unaffected.
                        string filename = msg.Substring(4).Trim();
                        filename = filename.Replace("-", "").Trim();

                        DraxService.OnManualControlFile?.Invoke(filename);

                        if (AMXTransfer.Instance.IsConnected)
                        {
                            AMXTransfer.Instance.SendMessage(filename);
                        }
                    }
                    else if (msg.Contains("|"))
                    {
                        ProcessAmxTransfer(msg);
                    }
                };

                await ReceiveDataAsync();
            }
            catch (Exception ex)
            {
                _connected = false;
                NotifyClient("AMX Connection failed: " + ex.Message);
            }
        }

        private void ProcessAmxTransfer(string msg)
        {
            if (string.IsNullOrEmpty(msg) || !msg.Contains("|"))
                return;
            string[] parts = msg.Split(kpipedelim);
            if (parts.Length <= 8)
                return;

            // Hand off to the live DraxService instance (which owns abstractpanels).
            DraxService.OnAmxPipeCommand?.Invoke(parts);
        }

        public void NotifyClient(string message)
        {
            OutsideEvents?.Invoke(this, new CustomEventArgs(message, false));
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
                // Heartbeat fires every second — don't NotifyClient or it would
                // flood the event stream. The send itself is logged at debug
                // depth (Debug.WriteLine survives in attached debuggers but not
                // in service mode).
                SendMessage("?");
                this.NotifyClient("Sent AMX Heartbeat ?");

            }
        }
        public void SendMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (_outbound.IsAddingCompleted) return;
            _outbound.Add(message);
        }

        private void StartSender()
        {
            if (_senderThread != null && _senderThread.IsAlive) return;
            _senderCts = new CancellationTokenSource();
            _senderThread = new Thread(SenderLoop)
            {
                IsBackground = true,
                Name = "AMXSender"
            };
            _senderThread.Start();
        }

        private void SenderLoop()
        {
            try
            {
                foreach (var message in _outbound.GetConsumingEnumerable(_senderCts.Token))
                {
                    WriteToStream(message);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                NotifyClient("AMX sender thread terminated: " + ex.Message);
            }
        }

        // Wait this long for a MAK ack from AMX before giving up and moving on.
        // 5s is generous — typical ack is sub-second; bounded so a missing MAK
        // can't deadlock the sender.
        private static readonly TimeSpan kMakTimeout = TimeSpan.FromSeconds(5);

        private void WriteToStream(string message)
        {
            const int maxAttempts = 3;

            // Heartbeat is fire-and-forget; everything else (NTX:, MTX: echo,
            // file paths) waits for AMX to MAK before we send the next frame.
            // This is what the sleep was crudely approximating before.
            bool waitForAck = !string.Equals(message, "?", StringComparison.Ordinal);
            if (waitForAck) _makAck.Reset();

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

                            if (waitForAck && !_makAck.Wait(kMakTimeout))
                            {
                                NotifyClient("AMX MAK timeout for: " + message);
                            }
                            return;
                        }
                        NotifyClient("Stream is not writable.");
                    }
                    catch (Exception ex)
                    {
                        NotifyClient($"Send attempt {attempt} failed: {ex.Message}");
                        _connected = false;
                    }
                }

                if (!_connected && attempt < maxAttempts)
                {
                    NotifyClient("Not connected unable to send");
                }
            }
            NotifyClient("SendMessage failed after 3 attempts.");
        }

        public async Task ReceiveDataAsync()
        {
            try
            {
                if (_tcpClient == null || !_tcpClient.Connected)
                    return;

                var buffer = new byte[4096];
                var stream = _tcpClient.GetStream();

                while (_tcpClient.Connected)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

                    if (bytesRead == 0)
                    {
                        NotifyClient("AMX server closed connection");
                        break;
                    }

                    string chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                    if (chunk.Length > 0)
                        isMessageReceive?.Invoke(chunk);
                }
            }
            catch (Exception ex)
            {
                NotifyClient("Exception in ReceiveDataAsync: " + ex.Message);
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
