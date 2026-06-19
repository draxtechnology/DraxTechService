using System;
using System.Collections.Concurrent;
using System.IO;
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
        // Receive-side accumulator for partial AMX frames straddling TCP reads.
        // See ReceiveDataAsync for the framing logic.
        private readonly System.Text.StringBuilder _rxAccum = new System.Text.StringBuilder();
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

        // Accumulator cap for the receive framing buffer (see ReceiveDataAsync).
        // 64 KB — far above any expected frame size, defensive only.
        private const int kRxAccumMaxBytes = 65536;

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
                    if (msg.StartsWith("NWM:") || msg.StartsWith("GEN:") || msg.StartsWith("AUT:"))
                    {
                        DraxService drax = new DraxService();
                        drax.sendreturncmd("", msg);
                    }
                    else if (msg.StartsWith("MAK:"))
                    {
                        // AMX echoes the original NTX: send back as "MAK:NTX:<path>".
                        // Strip BOTH prefixes — the previous code stripped only
                        // "MAK:" (4 chars), leaving "NTX:" wedged into the filename
                        // so File.Exists/Delete silently missed. The version before
                        // that used Substring(9) which over-corrected and lopped
                        // the drive letter off the path. Mike's 10 .GEN files left
                        // on disk after a clean test were this bug.
                        // No need to strip frame-separator hyphens here — the
                        // receive loop already splits chunks on '-' before
                        // dispatching, so msg is clean. Stripping hyphens with
                        // Replace("-", "") would also corrupt any AMX path
                        // containing an internal hyphen (e.g. C:\AMX1\Temp\my-
                        // folder\1.GEN), so it's been removed (2026-05-23).
                        string filename = msg.Substring(4).Trim();
                        if (filename.StartsWith("NTX:"))
                        {
                            filename = filename.Substring(4).Trim();
                        }
                        if (File.Exists(filename))
                        {
                            CSAMXSingleton.CS.ScheduleDelete(filename);
                        }
                        _makAck.Set();  // releases the sender thread's WaitForMak
                    }
                    else if (msg.StartsWith("MTX:"))
                    {
                        // Manual Controls from AMX. The path points at a 224-byte
                        // .MTN file in NVM struct format. Decode and dispatch to
                        // the active panel(s) via DraxService; then preserve the
                        // existing echo-back so AMX's handshake is unaffected.
                        // (Receive loop already strips frame-separator '-' before
                        // dispatch, so no extra hyphen cleanup needed here —
                        // doing so would corrupt paths containing internal '-'.)
                        string filename = msg.Substring(4).Trim();

                        DraxService.OnManualControlFile?.Invoke(filename);

                        // Schedule deletion now — AMX does not send a MAK for the
                        // MTX echo-back, so the file would otherwise be left on disk.
                        CSAMXSingleton.CS.ScheduleDelete(filename);

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

            // Bulk graphic-control case. AMX may concatenate multiple "CTRL|"
            // blocks back-to-back inside one frame (David's Friday concern via
            // Mike: 256-device bulk dispatches). Split on the "CTRL|" token
            // and run each block through the dispatch independently so each
            // becomes its own panel-writer queue entry — no SQL detour, panel
            // sets the drain pace. Single CTRL falls through to the original
            // path.
            //
            // Bulk format per Mike's note (assumed, not yet confirmed on the
            // wire): "CTRL|a|b|...|CTRL|a|b|...|CTRL|a|b|...|"
            int firstCtrl = msg.IndexOf("CTRL|");
            if (firstCtrl >= 0 && msg.IndexOf("CTRL|", firstCtrl + 5) >= 0)
            {
                string[] blocks = msg.Split(
                    new[] { "CTRL|" }, StringSplitOptions.RemoveEmptyEntries);
                NotifyClient($"Bulk CTRL frame: {blocks.Length} blocks");
                foreach (string block in blocks)
                {
                    string[] parts = ("CTRL|" + block).Split(kpipedelim);
                    if (parts.Length > 8)
                    {
                        DraxService.OnAmxPipeCommand?.Invoke(parts);
                        Thread.Sleep(500);  // give the panel writer a moment to pick up the file before we send the echo-back
                    }
                }
                return;
            }

            // Single CTRL or other pipe-delimited graphic command.
            string[] partsSingle = msg.Split(kpipedelim);
            if (partsSingle.Length <= 8)
                return;
            DraxService.OnAmxPipeCommand?.Invoke(partsSingle);
            Thread.Sleep(500);  // give the panel writer a moment to pick up the file before we send the echo-back
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
                //this.NotifyClient("Sent AMX Heartbeat ?");
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

        // Backoff between WriteToStream retries — earlier the loop spun three
        // attempts in microseconds with no pause and no reconnect, then dropped
        // the message. A short pause gives the Run() reconnect loop a chance
        // to re-establish before we burn the next attempt.
        private const int kSendRetryBackoffMs = 300;

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

                if (attempt < maxAttempts)
                {
                    // Pause before the next attempt so the reconnect loop in Run()
                    // has time to re-establish the stream after a transient drop.
                    Thread.Sleep(kSendRetryBackoffMs);
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

                    // AMX delimits replies with '-' but TCP doesn't guarantee
                    // message boundaries — a single read can span multiple
                    // replies AND a single reply can straddle two reads. Mike's
                    // earlier session left 3 .GEN files behind (file numbers
                    // 1, 5, 8 — gap pattern) after the prefix-split fix took
                    // most of them; the survivors looked like MAK acks that
                    // had been chopped across chunk boundaries so neither half
                    // matched the prefix dispatch.
                    //
                    // Accumulate incoming bytes; emit only what's before the
                    // last '-' (one or more complete frames), keeping any
                    // trailing partial for the next read.
                    _rxAccum.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                    // Defensive cap: if AMX (or a wedged connection) never sends
                    // a frame separator, the accumulator would grow without
                    // bound. 64 KB is far larger than any expected frame; if we
                    // ever cross it, dump and continue rather than OOM the
                    // service. Not an expected condition.
                    if (_rxAccum.Length > kRxAccumMaxBytes)
                    {
                        NotifyClient($"AMX receive accumulator exceeded {kRxAccumMaxBytes} bytes without a separator — dropping {_rxAccum.Length} buffered bytes");
                        _rxAccum.Clear();
                        continue;
                    }

                    int lastSep = -1;
                    for (int i = _rxAccum.Length - 1; i >= 0; i--)
                    {
                        if (_rxAccum[i] == '-') { lastSep = i; break; }
                    }
                    if (lastSep < 0)
                    {
                        // No separator yet — partial frame, wait for more.
                        continue;
                    }
                    string complete = _rxAccum.ToString(0, lastSep);
                    string remainder = _rxAccum.ToString(lastSep + 1,
                                                        _rxAccum.Length - lastSep - 1);
                    _rxAccum.Clear();
                    _rxAccum.Append(remainder);

                    foreach (string segment in complete.Split(
                        new[] { '-' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        // Strip BOM — AMX occasionally injects U+FEFF inside MAK frames
                        // (sometimes wedged between MAK: and NTX:, sometimes as a standalone
                        // "MAK:<BOM>" ghost frame). .Trim() doesn't treat U+FEFF as whitespace.
                        string msg = segment.Trim().Replace("\uFEFF", "");
                        if (msg.Length > 0)
                            isMessageReceive?.Invoke(msg);
                    }
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
