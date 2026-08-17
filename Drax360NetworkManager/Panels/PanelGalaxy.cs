using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DraxTechnology.Panels
{
    // Honeywell Galaxy intruder panel (Dimension GD-48 family) over the
    // Galaxy Ethernet module. The wire format is the Galaxy RS232-module
    // protocol carried over TCP: <header><block code><data><parity>, with
    // SIA-format event codes (BA, TA, OP, CL...) inside the event blocks.
    // Ported from Phil Spooner's Galaxy_NWM (SupportGalaxy.bas /
    // frmGalaxyNetworkManager.frm) with the serial-only machinery (CTS
    // presence sensing, RTS handshaking, framing-error counters) replaced
    // by socket state + the shared comms monitor, and two legacy defects
    // deliberately fixed rather than ported:
    //   - ACKs are sent with the ack-request bit CLEAR. The VB requested an
    //     ack for its own acks and the panel answered with literal garbage
    //     bytes (11 81 3D) that then had to be special-cased out.
    //   - The multi-block event window is 5s measured in milliseconds. The
    //     VB used DateDiff seconds on a 2s window (real tolerance 1-2s) and
    //     its logs show event blocks routinely falling outside it.
    internal class PanelGalaxy : AbstractPanel
    {
        #region Protocol constants
        // Block codes (SupportGalaxy.bas:121-155). A block is the first byte
        // after the header; the rest of the payload is ASCII data.
        private const char kBlkAck = '8';        // data received OK (both directions)
        private const char kBlkReject = '9';     // data received bad (both directions)
        private const char kBlkPasscode = '?';   // logon/logoff/passcode to panel
        private const char kBlkConfig = '@';     // config response - carries SIA level
        private const char kBlkAccount = '#';    // account (site id) event block
        private const char kBlkAlarmData = 'N';  // alarm event data block
        private const char kBlkOtherData = 'O';  // non-alarm event data block
        private const char kBlkAscii = 'A';      // ASCII event-text block (SIA level 3+)
        private const char kBlkControl = 'C';    // control block (CSA/CSB/COR/CEV)
        private const char kBlkExtended = 'X';   // extended control block (XEV/XZS)

        // Command verbs (block code + 2-char command).
        private const string kCmdSet = "CSA";    // set/unset/reset groups: CSA<g>*<action>
        private const string kCmdOmit = "CSB";   // omit zones: CSB<zone>*<1|0>
        private const string kCmdEvents = "XEV"; // event update: XEV<n>*1
        private const string kCmdLevel = "@AL";  // SIA level in the config response

        // CSA actions (SupportGalaxy.bas GlxCmdSetGroup syntax comment).
        private const int kSetActionSystemReset = 3;

        private const int kMinFrameLen = 3;
        // Header length field is 6 bits (0-63) + header/block/parity.
        private const int kMaxFrameLen = 66;

        private const int kResponseTimeoutMs = 2000;   // VB RESPONSE_TIMEOUT
        private const int kCommandRetries = 3;         // VB GLX_CMD_SEND_RETRIES
        private const int kLogonIntervalMs = 3000;     // VB SEND_LOGON_INTERVAL - doubles as keep-alive
        private const int kEventBlockWindowMs = 5000;  // widened from the VB's failing 2s window
        #endregion

        private string gsIPAddress;
        private string gsIPPort;
        private string _passcode = "543210";   // VB GLX_PASSCODE default; override via GalaxyMan.ini
        // Galaxy Account No. (panel menu 56.2.3, 1..100) - the AMX node is
        // SiteID + offset, matching the legacy GalaxyMan.ini SiteID key. The
        // panel transmits NOTHING while its account number is zero.
        private int _siteId = 1;
        private readonly int _amx1Offset;
        private readonly string _txLogPath;

        private TcpClient _client;
        private NetworkStream _stream;
        private readonly object _connLock = new object();
        private readonly List<byte> _assembly = new List<byte>();
        private CancellationTokenSource _readerCts;
        private Task _readerTask;
        private Task _pumpTask;

        // Single-outstanding command queue with timeout + retries, the VB
        // command state machine (frmGalaxyNetworkManager.frm:1417-1554)
        // without the cooperative-multitasking scheduler around it.
        private sealed class GalaxyCommand
        {
            public byte[] Frame;
            public string Payload;       // block code + data, for response matching
            public bool ExpectsResponse; // acks don't get answered
            public int RetriesLeft = kCommandRetries;
        }
        private readonly Queue<GalaxyCommand> _cmdQueue = new Queue<GalaxyCommand>();
        private readonly object _cmdLock = new object();
        private GalaxyCommand _inFlight;
        private DateTime _inFlightSentAt = DateTime.MinValue;

        // Multi-block event assembly: account (#) then data (N/O), plus an
        // ASCII (A) block at SIA level 3+. SIA level 0-2 panels send no
        // ASCII block, so the data block alone completes an event.
        private string _evAccount;
        private string _evData;
        private bool _evDataIsAlarm;
        private string _evAscii;
        private DateTime _evLastBlockAt = DateTime.MinValue;

        private int _siaLevel = -1;   // from @AL<n>; queried not negotiated

        public override string FakeString => throw new NotImplementedException();
        public override string PanelVersion => "1.0.0.0";

        public PanelGalaxy(string baselogfolder, string identifier)
            : base(baselogfolder, identifier, "GalaxyMan", "GALAXY")
        {
            if (string.IsNullOrEmpty(identifier)) return;

            try
            {
                gsIPAddress = base.GetSetting<string>(ksettingsetupsection, "PanelIPAddress");
                int port = base.GetSetting<int>(ksettingsetupsection, "IPPort");
                if (port > 0) gsIPPort = port.ToString();

                string passcode = base.GetSetting<string>(ksettingsetupsection, "Passcode");
                if (!string.IsNullOrWhiteSpace(passcode)) _passcode = passcode.Trim();

                int siteId = base.GetSetting<int>(ksettingsetupsection, "SiteID");
                if (siteId > 0) _siteId = siteId;

                _amx1Offset = base.GetSetting<int>(ksettingsetupsection, "giAmx1Offset");
                this.Offset = _amx1Offset;

                if (!string.IsNullOrEmpty(baselogfolder))
                {
                    _txLogPath = Path.Combine(baselogfolder, "GALAXY_transport.log");
                }

                NotifyClient($"PanelGalaxy: {gsIPAddress}:{gsIPPort} offset={_amx1Offset}");
            }
            catch (Exception ex)
            {
                NotifyClient($"PanelGalaxy settings load failed: {ex.Message}");
            }

            // The 3s logon doubles as the keep-alive (VB GlxTsk_Logon): the
            // panel answers every one with its @AL config block, so a healthy
            // link is never quiet and the comms monitor has a steady signal.
            heartbeat_timer = new System.Threading.Timer(
                heartbeat_timer_callback, this.Identifier, kLogonIntervalMs, kLogonIntervalMs);
        }

        protected override void heartbeat_timer_callback(object sender)
        {
            base.heartbeat_timer_callback(sender);
            EnqueueCommand(kBlkPasscode + _passcode + "*1", expectsResponse: true);
            CheckCommsMonitor();
        }

        // Comms fault after 3 missed x 10s: the keep-alive runs at 3s, so 30s
        // of silence is ten unanswered logons - the link is genuinely down.
        // This replaces the VB's CTS-holding panel-presence sensing (there is
        // no CTS over TCP); the fault reports against the panel's own node.
        protected override int HeartbeatIntervalSeconds => 10;
        protected override int CommsFaultNode => _siteId + this.Offset;

        public override void StartUp(int fakemode)
        {
            if (fakemode > 0)
            {
                NotifyClient("GALAXY FakeMode - reader not started");
                return;
            }
            if (string.IsNullOrEmpty(gsIPAddress) || string.IsNullOrEmpty(gsIPPort))
            {
                NotifyClient("GALAXY StartUp aborted - PanelIPAddress/IPPort missing from GalaxyMan.ini");
                return;
            }

            _readerCts?.Cancel();
            _readerCts = new CancellationTokenSource();
            var token = _readerCts.Token;
            _readerTask = Task.Run(() => ReaderLoop(token));
            _pumpTask = Task.Run(() => PumpLoop(token));
        }

        #region Framing
        // Frame: <header><block code><data...><parity>
        //   header bits 0-5: payload length (block code + data) minus 1... the
        //   VB builds it as (payload length - 1), i.e. data bytes excluding
        //   header/block/parity; bit 6: ack requested; bit 7: always set so a
        //   zero-length no-ack frame can't produce a NUL header.
        //   parity: 0xFF XOR'd with every byte from header to last data byte.
        private static byte[] BuildFrame(string payload, bool ackRequest)
        {
            var bytes = new byte[payload.Length + 2];
            int header = 0x80 | ((payload.Length - 1) & 0x3F);
            if (ackRequest) header |= 0x40;
            bytes[0] = (byte)header;
            for (int i = 0; i < payload.Length; i++) bytes[i + 1] = (byte)payload[i];
            int parity = 0xFF;
            for (int i = 0; i < bytes.Length - 1; i++) parity ^= bytes[i];
            bytes[bytes.Length - 1] = (byte)parity;
            return bytes;
        }

        // Drains complete frames out of the assembly buffer. Length comes
        // from the header (self-delimiting - no gap timing needed), parity
        // is verified, and the payload (block code + data) is handed on with
        // the header's ack-request flag.
        private void DrainFrames()
        {
            while (_assembly.Count >= kMinFrameLen)
            {
                int header = _assembly[0];
                int frameLen = (header & 0x3F) + 3;
                if (frameLen < kMinFrameLen || frameLen > kMaxFrameLen)
                {
                    NotifyClient($"GALAXY bad frame length {frameLen} - flushing buffer");
                    _assembly.Clear();
                    return;
                }
                if (_assembly.Count < frameLen) return;   // wait for more bytes

                int parity = 0xFF;
                for (int i = 0; i < frameLen - 1; i++) parity ^= _assembly[i];
                if (_assembly[frameLen - 1] != (byte)parity)
                {
                    NotifyClient("GALAXY parity mismatch - rejecting frame");
                    _assembly.RemoveRange(0, frameLen);
                    EnqueueAckFrame(kBlkReject);
                    continue;
                }

                bool ackRequested = (header & 0x40) != 0;
                var payload = new char[frameLen - 2];
                for (int i = 0; i < payload.Length; i++) payload[i] = (char)_assembly[i + 1];
                _assembly.RemoveRange(0, frameLen);
                ProcessBlock(new string(payload), ackRequested);
            }
        }
        #endregion

        #region Transport
        private void ReaderLoop(CancellationToken token)
        {
            byte[] buffer = new byte[4096];
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_client == null || !_client.Connected)
                    {
                        NotifyClient($"GALAXY connecting to {gsIPAddress}:{gsIPPort}");
                        var c = new TcpClient();
                        c.Connect(gsIPAddress, Convert.ToInt32(gsIPPort));
                        lock (_connLock)
                        {
                            _client = c;
                            _stream = c.GetStream();
                        }
                        _assembly.Clear();
                        NotifyClient("GALAXY connected");
                        // Logon straight away (VB sends it on port open,
                        // "mainly to get SIA level"); the timer repeats it.
                        EnqueueCommand(kBlkPasscode + _passcode + "*1", expectsResponse: true);
                    }

                    int bytesRead = _stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        NotifyClient("GALAXY peer closed - reconnecting");
                        CloseConnection();
                        try { Task.Delay(2000, token).Wait(token); } catch { }
                        continue;
                    }

                    lastDataReceived = DateTime.Now;
                    NoteCommsRestored();
                    LogTransport("<", buffer, bytesRead);
                    for (int i = 0; i < bytesRead; i++) _assembly.Add(buffer[i]);
                    DrainFrames();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested) break;
                    NotifyClient($"GALAXY reader: {ex.Message}");
                    CloseConnection();
                    try { Task.Delay(2000, token).Wait(token); } catch { break; }
                }
            }
            NotifyClient("GALAXY reader stopped");
        }

        private void CloseConnection()
        {
            lock (_connLock)
            {
                try { _stream?.Close(); } catch { }
                try { _client?.Close(); } catch { }
                _stream = null;
                _client = null;
            }
            lock (_cmdLock)
            {
                _inFlight = null;   // a dead link answers nothing
            }
        }

        private bool WriteRaw(byte[] frame)
        {
            lock (_connLock)
            {
                if (_stream == null || _client == null || !_client.Connected)
                {
                    return false;
                }
                try
                {
                    _stream.Write(frame, 0, frame.Length);
                    _stream.Flush();
                    LogTransport(">", frame, frame.Length);
                    return true;
                }
                catch (Exception ex)
                {
                    NotifyClient($"GALAXY write failed: {ex.Message}");
                    try { _stream?.Close(); } catch { }
                    try { _client?.Close(); } catch { }
                    _stream = null;
                    _client = null;
                    return false;
                }
            }
        }

        private void LogTransport(string direction, byte[] data, int count)
        {
            if (string.IsNullOrEmpty(_txLogPath)) return;
            try
            {
                var sb = new StringBuilder();
                for (int i = 0; i < count; i++) sb.Append(data[i].ToString("X2")).Append(' ');
                File.AppendAllText(_txLogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {direction}: {sb.ToString().TrimEnd()}{Environment.NewLine}");
            }
            catch { /* logging is best-effort */ }
        }
        #endregion

        #region Command queue
        // ACK/NAK frames jump the queue and never wait for a reply. The
        // ack-request bit stays CLEAR - see the class comment.
        private void EnqueueAckFrame(char blockCode)
        {
            var cmd = new GalaxyCommand
            {
                Frame = BuildFrame(blockCode.ToString(), ackRequest: false),
                Payload = blockCode.ToString(),
                ExpectsResponse = false,
            };
            lock (_cmdLock)
            {
                // Head-insert, mirroring the VB's high-priority queue slot.
                var rest = _cmdQueue.ToArray();
                _cmdQueue.Clear();
                _cmdQueue.Enqueue(cmd);
                foreach (var c in rest) _cmdQueue.Enqueue(c);
            }
        }

        private void EnqueueCommand(string payload, bool expectsResponse)
        {
            var cmd = new GalaxyCommand
            {
                Frame = BuildFrame(payload, ackRequest: true),
                Payload = payload,
                ExpectsResponse = expectsResponse,
            };
            lock (_cmdLock)
            {
                // Duplicate suppression as per the VB: an identical command
                // already queued (not yet sent) is dropped - the 3s logon
                // would otherwise pile up behind a dead link.
                foreach (var q in _cmdQueue)
                {
                    if (q.Payload == payload) return;
                }
                _cmdQueue.Enqueue(cmd);
            }
        }

        // One outstanding command at a time: send, wait for its response (or
        // the 2s timeout), retry up to 3 times on timeout or a '9' reject.
        private void PumpLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                GalaxyCommand toSend = null;
                lock (_cmdLock)
                {
                    if (_inFlight != null)
                    {
                        double waitedMs = (DateTime.Now - _inFlightSentAt).TotalMilliseconds;
                        if (waitedMs > kResponseTimeoutMs)
                        {
                            if (_inFlight.RetriesLeft > 0)
                            {
                                toSend = _inFlight;   // resend
                            }
                            else
                            {
                                NotifyClient($"GALAXY command {_inFlight.Payload} gave up after retries");
                                _inFlight = null;
                            }
                        }
                    }
                    if (toSend == null && _inFlight == null && _cmdQueue.Count > 0)
                    {
                        toSend = _cmdQueue.Dequeue();
                    }
                }

                if (toSend == null)
                {
                    try { Task.Delay(50, token).Wait(token); } catch { break; }
                    continue;
                }

                if (!WriteRaw(toSend.Frame))
                {
                    // Not connected - drop acks, requeue nothing: the logon
                    // timer repopulates the queue once the link returns.
                    lock (_cmdLock) { if (_inFlight == toSend) _inFlight = null; }
                    try { Task.Delay(200, token).Wait(token); } catch { break; }
                    continue;
                }

                if (toSend.ExpectsResponse)
                {
                    lock (_cmdLock)
                    {
                        toSend.RetriesLeft--;
                        _inFlight = toSend;
                        _inFlightSentAt = DateTime.Now;
                    }
                }
                else
                {
                    lock (_cmdLock) { if (_inFlight == toSend) _inFlight = null; }
                }
            }
            NotifyClient("GALAXY pump stopped");
        }

        // Response matching per the VB (SupportGalaxy.bas:1790-1939): '8'
        // completes, '9' leaves the command for a retried send, '@' answers a
        // passcode command, and a C/X block whose 3-char verb matches the
        // command's completes it (XEV may legitimately answer as CEV).
        private void ResolveInFlight(string payload)
        {
            lock (_cmdLock)
            {
                if (_inFlight == null) return;
                char block = payload[0];
                if (block == kBlkAck)
                {
                    _inFlight = null;
                }
                else if (block == kBlkReject)
                {
                    // Leave in flight; the timeout path resends while
                    // retries remain.
                    _inFlightSentAt = DateTime.MinValue;
                }
                else if (block == kBlkConfig && _inFlight.Payload[0] == kBlkPasscode)
                {
                    _inFlight = null;
                }
                else if ((block == kBlkControl || block == kBlkExtended)
                    && _inFlight.Payload.Length >= 3 && payload.Length >= 3
                    && _inFlight.Payload.Substring(1, 2) == payload.Substring(1, 2))
                {
                    _inFlight = null;
                }
            }
        }
        #endregion

        #region Block processing
        private void ProcessBlock(string payload, bool ackRequested)
        {
            if (payload.Length == 0) return;
            CountMessage();

            if (ackRequested && payload[0] != kBlkAck && payload[0] != kBlkReject)
            {
                EnqueueAckFrame(kBlkAck);
            }

            ResolveInFlight(payload);

            char block = payload[0];
            string data = payload.Length > 1 ? payload.Substring(1) : "";
            switch (block)
            {
                case kBlkConfig:
                    // @AL<n>B<m> - SIA level. Level 0-2: no ASCII text blocks;
                    // level 3+: ASCII block follows each event.
                    int pos = payload.IndexOf(kCmdLevel, StringComparison.Ordinal);
                    if (pos >= 0 && payload.Length > pos + kCmdLevel.Length
                        && int.TryParse(payload[pos + kCmdLevel.Length].ToString(), out int level))
                    {
                        if (level != _siaLevel)
                        {
                            _siaLevel = level;
                            NotifyClient($"GALAXY panel SIA level {level}"
                                + (level < 3 ? " - no event text blocks, codes only" : ""));
                        }
                    }
                    break;

                case kBlkAccount:
                    NoteEventBlock(account: data);
                    break;

                case kBlkAlarmData:
                    NoteEventBlock(data: data, isAlarm: true);
                    break;

                case kBlkOtherData:
                    NoteEventBlock(data: data, isAlarm: false);
                    break;

                case kBlkAscii:
                    NoteEventBlock(ascii: data);
                    break;

                case kBlkAck:
                case kBlkReject:
                case kBlkControl:
                case kBlkExtended:
                    // Handled by ResolveInFlight; nothing further to do here.
                    break;

                default:
                    NotifyClient($"GALAXY unknown block '{block}' data '{data}'", false);
                    break;
            }
        }

        // Assembles the account/data/ascii block group into one event. At SIA
        // level 0-2 the data block completes the event immediately (there is
        // no ASCII block to wait for); at level 3+ the event is held for its
        // text until the window closes.
        private void NoteEventBlock(string account = null, string data = null,
            bool isAlarm = false, string ascii = null)
        {
            double sinceLastMs = (DateTime.Now - _evLastBlockAt).TotalMilliseconds;
            if (sinceLastMs > kEventBlockWindowMs)
            {
                // Stale partial event - a data block on its own is still
                // decodable, so flush rather than drop (the VB dropped, and
                // its logs show real events lost to exactly this).
                FlushPendingEvent();
            }
            _evLastBlockAt = DateTime.Now;

            if (account != null) _evAccount = account;
            if (ascii != null) _evAscii = ascii;
            if (data != null)
            {
                // A second data block before the first flushed = new event.
                if (_evData != null) FlushPendingEvent();
                _evData = data;
                _evDataIsAlarm = isAlarm;
                _evLastBlockAt = DateTime.Now;
            }

            if (_evData != null && (_siaLevel < 3 || _evAscii != null))
            {
                FlushPendingEvent();
            }
        }

        private void FlushPendingEvent()
        {
            if (_evData == null)
            {
                _evAccount = null;
                _evAscii = null;
                return;
            }
            string dataBlock = _evData;
            bool isAlarm = _evDataIsAlarm;
            string ascii = _evAscii;
            _evData = null;
            _evAscii = null;

            try
            {
                DecodeSiaEvent(dataBlock, isAlarm, ascii);
            }
            catch (Exception ex)
            {
                NotifyClient($"GALAXY event decode error on '{dataBlock}': {ex.Message}");
            }
        }
        #endregion

        #region Event decode -> AMX
        // AMX identity contract (VB SupportNWM.bas WriteEventToTransferFile,
        // confirmed against GALAXY.nod and GalaxyAMX1InputType.doc):
        //   node      = SiteID + giAmx1Offset
        //   loop      = ALWAYS 0 - the raw 4-digit Galaxy zone address (1001..)
        //               overflows out of the 8-bit input field into the loop
        //               byte by design (zone 1001 -> loop 3, device 233), and
        //               the control path reverses exactly that
        //   input     = zone address + 0 | module + 10000 | panel status IP 1-15
        //   inputtype = ZIT enum below for zones/modules; 15 for panel status
        //
        // Zone input types (SupportGalaxy.bas:281-291 + GALAXY.nod, both agree;
        // ignore the stale comment block in GalaxyEvent.cls:350-359):
        //   0 Burglar, 1 Fire, 2 Holdup, 3 Panic, 4 Omit, 5 Tamper,
        //   6 Access, 7 Relay, 8 Trouble, 15 panel/housekeeping.
        private const int kZitBurglar = 0;
        private const int kZitFire = 1;
        private const int kZitHoldup = 2;
        private const int kZitPanic = 3;
        private const int kZitOmit = 4;
        private const int kZitTamper = 5;
        private const int kZitAccess = 6;
        private const int kZitRelay = 7;
        private const int kZitTrouble = 8;
        private const int kPanelInputType = 15;
        private const int kModuleOffset = 10000;

        // Panel status IP numbers (enmPanelStatusIP / GALAXY.nod [HKInputs]).
        private const int kPsiPanelSet = 2;
        private const int kPsiFailToSet = 3;
        private const int kPsiRecentSet = 4;
        private const int kPsiSetExtend = 5;
        private const int kPsiLateOpen = 6;
        private const int kPsiAcFail = 7;
        private const int kPsiEngineerMode = 8;
        private const int kPsiPowerUp = 9;
        private const int kPsiBatteryLow = 10;
        private const int kPsiTelCommFail = 11;
        private const int kPsiTamper = 12;
        private const int kPsiByPass = 13;
        private const int kPsiUnByPass = 14;
        private const int kPsiDuress = 15;

        // Off-latching (GalaxyEvent.cls:2048 + GalaxyAMX1InputType.doc s4):
        // an alarm only clears on AMX when Restored AND Cancelled AND Reset
        // are all true; codes that don't require cancel/reset auto-satisfy
        // those flags. Reset class 'S' = SYS reset, 'P' = PA reset (panic +
        // holdup), 'T' = tamper reset, matched against the OR event's ASCII
        // text ("SYS R"/"PA R"/"TAMP R"). Deviation from the VB, deliberate:
        // the legacy retrigger pulse pairs (off-then-on with a stage word per
        // latching stage) are not reproduced - they were history-log dressing
        // and the pulses' extra-info was blank anyway (VB BUG, cls:2066-2075).
        private sealed class ActiveAlarm
        {
            public int BaseEvnum;       // identity with the on-bit clear
            public string Text;
            public bool NeedsCancel;
            public char ResetClass;     // 'S', 'P', 'T' or ' ' (none)
            public bool Restored;
            public bool Cancelled;
            public bool Reset;
        }
        private readonly Dictionary<int, ActiveAlarm> _activeAlarms = new Dictionary<int, ActiveAlarm>();
        private readonly object _alarmsLock = new object();

        // Data block grammar (SupportGalaxy.bas:2423-2428):
        //   minimal (SIA level 0): "BA03"
        //   full: "tihh:mm/rixx/iduuu/pimmm/EVzzzz" - last token is the
        //   2-char event code + address; ti=time, ri=group, id=user,
        //   pi=module/peripheral.
        // Source typing (GalaxyAMX1InputType.doc s2): id present = User
        // (treated as a Panel event, user surfaced as text only - the VB
        // deliberately removed per-user AMX identity); pi without id =
        // Module (input = module + 10000); address > 0 = Zone; else Panel.
        private void DecodeSiaEvent(string dataBlock, bool isAlarm, string asciiText)
        {
            if (dataBlock.Length < 2) return;

            string[] segments = dataBlock.Split('/');
            string last = segments[segments.Length - 1];
            if (last.Length < 2) return;
            string code = last.Substring(0, 2).ToUpperInvariant();
            int.TryParse(last.Substring(2), out int address);

            int user = 0, module = -1, group = -1;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                string seg = segments[i];
                if (seg.StartsWith("id", StringComparison.OrdinalIgnoreCase)) int.TryParse(seg.Substring(2), out user);
                else if (seg.StartsWith("pi", StringComparison.OrdinalIgnoreCase)) { int m; if (int.TryParse(seg.Substring(2), out m)) module = m; }
                else if (seg.StartsWith("ri", StringComparison.OrdinalIgnoreCase)) { int g; if (int.TryParse(seg.Substring(2), out g)) group = g; }
            }

            bool userSourced = user > 0;
            bool moduleSourced = !userSourced && module >= 0;
            bool zoneSourced = !userSourced && !moduleSourced && address > 0;

            // Extra info to AMX text2, VB AssembleEventExtraInfo intent (its
            // Zn field was lost to a concat bug; emit the documented form).
            string extra = "";
            if (group >= 0) extra += "Gr" + group + ",";
            if (module >= 0) extra += "Pr" + module + ",";
            if (user > 0) extra += "Us" + user;
            extra = extra.TrimEnd(',');

            string text = string.IsNullOrEmpty(asciiText) ? "" : asciiText.Trim();

            switch (code)
            {
                // ---- Cancel / reset verbs -------------------------------
                case "BC":   // Burglar Cancel - satisfies the cancel flag
                    ApplyCancel();
                    return;
                case "OR":   // Reset - flavour from the ASCII text
                    if (text.IndexOf("SYS R", StringComparison.OrdinalIgnoreCase) >= 0) ApplyReset('S');
                    else if (text.IndexOf("PA R", StringComparison.OrdinalIgnoreCase) >= 0) ApplyReset('P');
                    else if (text.IndexOf("TAMP R", StringComparison.OrdinalIgnoreCase) >= 0) ApplyReset('T');
                    else NotifyClient($"GALAXY OR reset with unrecognised text '{text}' - discarded (VB parity)", false);
                    return;

                // ---- Set / unset: one panel status point, IT 15 ----------
                // CA/CG/CL all collapse to Set ON; OG/OK/OP to Set OFF
                // (part-set vs full-set lives in the text only, by design).
                case "CA": case "CG": case "CL":
                    SendPanelStatus(kPsiPanelSet, true, Fallback(text, "Panel Set"), extra); return;
                case "OG": case "OK": case "OP":
                    SendPanelStatus(kPsiPanelSet, false, Fallback(text, "Panel Unset"), extra); return;
                // Arming faults - one-shot status points.
                case "CI": SendPanelStatus(kPsiFailToSet, true, Fallback(text, "Fail To Set"), extra, oneShot: true); return;
                case "CR": SendPanelStatus(kPsiRecentSet, true, Fallback(text, "Recent Set"), extra, oneShot: true); return;
                case "CE": SendPanelStatus(kPsiSetExtend, true, Fallback(text, "Set Extend"), extra, oneShot: true); return;
                case "CT": SendPanelStatus(kPsiLateOpen, true, Fallback(text, "Late To Open"), extra, oneShot: true); return;

                // ---- Alarms with latching -------------------------------
                case "BA": ZoneAlarm(true, address, kZitBurglar, Fallback(text, "Intruder"), extra, needsCancel: true, resetClass: 'S'); return;
                case "BR": ZoneAlarm(false, address, kZitBurglar, Fallback(text, "Intruder Restored"), extra, needsCancel: true, resetClass: 'S'); return;
                case "FA": ZoneAlarm(true, address, kZitFire, Fallback(text, "Fire"), extra, needsCancel: true, resetClass: ' '); return;
                case "FR": ZoneAlarm(false, address, kZitFire, Fallback(text, "Fire Restored"), extra, needsCancel: true, resetClass: ' '); return;
                case "PA": ZoneAlarm(true, address, kZitPanic, Fallback(text, "Panic"), extra, needsCancel: true, resetClass: 'P'); return;
                case "PR": ZoneAlarm(false, address, kZitPanic, Fallback(text, "Panic Restored"), extra, needsCancel: true, resetClass: 'P'); return;
                case "HA":
                    if (userSourced)
                    {
                        // User-sourced holdup = DURESS: panel status 15, STICKY -
                        // no clearing event exists; a privileged AMX user clears
                        // it manually. (The VB emitted IT2/addr15 here - its
                        // documented Duress point per the .nod + doc s6 wins.)
                        SendPanelStatus(kPsiDuress, true, Fallback(text, "Duress"), extra);
                        return;
                    }
                    ZoneAlarm(true, address, kZitHoldup, Fallback(text, "Holdup"), extra, needsCancel: false, resetClass: 'P');
                    return;
                case "HR": ZoneAlarm(false, address, kZitHoldup, Fallback(text, "Holdup Restored"), extra, needsCancel: false, resetClass: 'P'); return;
                case "TA":
                    if (zoneSourced || moduleSourced)
                        ZoneAlarm(true, ModuleOrZone(address, module, moduleSourced), kZitTamper, Fallback(text, "Tamper"), extra, needsCancel: true, resetClass: 'T');
                    else
                        SendPanelStatus(kPsiTamper, true, Fallback(text, "Panel Tamper"), extra);
                    return;
                case "TR":
                    if (zoneSourced || moduleSourced)
                        ZoneAlarm(false, ModuleOrZone(address, module, moduleSourced), kZitTamper, Fallback(text, "Tamper Restored"), extra, needsCancel: true, resetClass: 'T');
                    else
                        SendPanelStatus(kPsiTamper, false, Fallback(text, "Panel Tamper Restored"), extra);
                    return;

                // ---- Troubles: straight on/off, no latching -------------
                case "BT": SendZone(true, address, kZitTrouble, Fallback(text, "Intruder Trouble"), extra); return;
                case "BJ": SendZone(false, address, kZitTrouble, Fallback(text, "Intruder Trouble Restored"), extra); return;
                case "FT": SendZone(true, address, kZitTrouble, Fallback(text, "Fire Trouble"), extra); return;
                case "FJ": SendZone(false, address, kZitTrouble, Fallback(text, "Fire Trouble Restored"), extra); return;
                case "HT": SendZone(true, address, kZitTrouble, Fallback(text, "Holdup Trouble"), extra); return;
                case "HJ": SendZone(false, address, kZitTrouble, Fallback(text, "Holdup Trouble Restored"), extra); return;
                case "PT": SendZone(true, address, kZitTrouble, Fallback(text, "Panic Trouble"), extra); return;
                case "PJ": SendZone(false, address, kZitTrouble, Fallback(text, "Panic Trouble Restored"), extra); return;
                case "LT": SendZone(true, address, kZitTrouble, Fallback(text, "Line Trouble"), extra); return;
                case "LR": SendZone(false, address, kZitTrouble, Fallback(text, "Line Restored"), extra); return;

                // ---- Omits: IT 4 on the zone; user-keyed = status one-shot -
                case "BB": case "FB": case "HB": case "PB":
                    if (zoneSourced) SendZone(true, address, kZitOmit, Fallback(text, "Zone Omitted"), extra);
                    else SendPanelStatus(kPsiByPass, true, Fallback(text, "Zone Omitted"), extra, oneShot: true);
                    return;
                case "BU": case "FU": case "HU": case "PU":
                    if (zoneSourced) SendZone(false, address, kZitOmit, Fallback(text, "Zone Reinstated"), extra);
                    else SendPanelStatus(kPsiUnByPass, true, Fallback(text, "Zone Reinstated"), extra, oneShot: true);
                    return;

                // ---- Access (modules): one-shot ON, IT 6 ----------------
                case "DD": AccessEvent(module, address, Fallback(text, "Access Denied"), extra); return;
                case "DF": AccessEvent(module, address, Fallback(text, "Door Forced"), extra); return;
                case "DG": AccessEvent(module, address, Fallback(text, "Access Granted"), extra); return;
                case "DK": AccessEvent(module, address, Fallback(text, "Access Lockout"), extra); return;
                case "DT": AccessEvent(module, address, Fallback(text, "Door Propped"), extra); return;

                // ---- Relay / engineer / power / comms / battery ---------
                case "RO": SendZone(true, address, kZitRelay, Fallback(text, "Relay Open"), extra); return;
                case "RC": SendZone(false, address, kZitRelay, Fallback(text, "Relay Closed"), extra); return;
                case "LB": SendPanelStatus(kPsiEngineerMode, true, Fallback(text, "Engineer Mode"), extra); return;
                case "LX": SendPanelStatus(kPsiEngineerMode, false, Fallback(text, "Engineer Mode End"), extra); return;
                case "RR": SendPanelStatus(kPsiPowerUp, true, Fallback(text, "Power Up"), extra, oneShot: true); return;
                case "AT":
                    if (zoneSourced || moduleSourced) SendZone(true, ModuleOrZone(address, module, moduleSourced), kZitTrouble, Fallback(text, "AC Fail"), extra);
                    else SendPanelStatus(kPsiAcFail, true, Fallback(text, "AC Fail"), extra);
                    return;
                case "AR":
                    if (zoneSourced || moduleSourced) SendZone(false, ModuleOrZone(address, module, moduleSourced), kZitTrouble, Fallback(text, "AC Restored"), extra);
                    else SendPanelStatus(kPsiAcFail, false, Fallback(text, "AC Restored"), extra);
                    return;
                case "YC": SendPanelStatus(kPsiTelCommFail, true, Fallback(text, "Comms Trouble"), extra); return;
                case "YK": SendPanelStatus(kPsiTelCommFail, false, Fallback(text, "Comms Restored"), extra); return;
                case "YT": SendPanelStatus(kPsiBatteryLow, true, Fallback(text, "Battery Low"), extra); return;
                case "YR": SendPanelStatus(kPsiBatteryLow, false, Fallback(text, "Battery Restored"), extra); return;

                // ---- Not permitted (VB parity): tests, program, time ----
                // JA (code tamper) stays dropped too - dead code in the VB
                // meant it never reached AMX, so no site graphic expects it.
                case "BV": case "BX": case "FX": case "JA": case "JT":
                case "RD": case "RS": case "RP": case "RX": case "TS": case "TE":
                    NotifyClient($"GALAXY event {code} not permitted (VB parity) - dropped", false);
                    return;

                default:
                    NotifyClient($"GALAXY unmapped SIA code {code} (address {address}) - dropped", false);
                    return;
            }
        }

        private static string Fallback(string text, string dflt)
            => string.IsNullOrEmpty(text) ? dflt : text;

        private static int ModuleOrZone(int address, int module, bool moduleSourced)
            => moduleSourced ? module + kModuleOffset : address;

        private int AmxNode => _siteId + _amx1Offset;

        private void SendZone(bool on, int input, int inputType, string text, string extra)
        {
            int evnum = CSAMXSingleton.CS.MakeInputNumber(AmxNode, 0, input, inputType, on);
            NotifyClient($"GALAXY -> AMX zone input {input} type {inputType} {(on ? "ON" : "OFF")}: {text}");
            send_response_amx(evnum, text, extra, "");
        }

        private void SendPanelStatus(int statusIp, bool on, string text, string extra, bool oneShot = false)
        {
            int evnum = CSAMXSingleton.CS.MakeInputNumber(AmxNode, 0, statusIp, kPanelInputType, on);
            NotifyClient($"GALAXY -> AMX panel status {statusIp} {(on ? "ON" : "OFF")}: {text}");
            send_response_amx(evnum, text, extra, "");
            if (oneShot)
            {
                // Attribute 13 = momentary: AMX clears the point on accept
                // (legacy NwmForceEvmAttribute, SupportNWM.bas:278-335).
                CSAMXSingleton.CS.ForceEvmAttribute(evnum, 13, 1);
                CSAMXSingleton.CS.FlushMessages();
            }
        }

        private void AccessEvent(int module, int address, string text, string extra)
        {
            int input = module >= 0 ? module + kModuleOffset : address + kModuleOffset;
            int evnum = CSAMXSingleton.CS.MakeInputNumber(AmxNode, 0, input, kZitAccess, true);
            NotifyClient($"GALAXY -> AMX access module {input}: {text}");
            send_response_amx(evnum, text, extra, "");
            CSAMXSingleton.CS.ForceEvmAttribute(evnum, 13, 1);
            CSAMXSingleton.CS.FlushMessages();
        }

        // Latched alarm on/restore. The ON goes to AMX immediately and books
        // the identity; the OFF is held until restore + (cancel) + (reset)
        // have all arrived, per the VB three-flag rule.
        private void ZoneAlarm(bool on, int input, int inputType, string text, string extra,
            bool needsCancel, char resetClass)
        {
            int baseEvnum = CSAMXSingleton.CS.MakeInputNumber(AmxNode, 0, input, inputType, false);
            if (on)
            {
                int evnum = CSAMXSingleton.CS.MakeInputNumber(AmxNode, 0, input, inputType, true);
                NotifyClient($"GALAXY -> AMX alarm input {input} type {inputType} ON: {text}");
                send_response_amx(evnum, text, extra, "");
                lock (_alarmsLock)
                {
                    _activeAlarms[baseEvnum] = new ActiveAlarm
                    {
                        BaseEvnum = baseEvnum,
                        Text = text,
                        NeedsCancel = needsCancel,
                        ResetClass = resetClass,
                        Cancelled = !needsCancel,
                        Reset = resetClass == ' ',
                    };
                }
                return;
            }

            lock (_alarmsLock)
            {
                if (_activeAlarms.TryGetValue(baseEvnum, out var alarm))
                {
                    alarm.Restored = true;
                    alarm.Text = text;
                    TrySendOff_NoLock(alarm);
                    return;
                }
            }
            // Restore for something never seen active (service restarted
            // mid-alarm) - pass it through so AMX can clear its row.
            NotifyClient($"GALAXY restore for untracked input {input} - sent through");
            send_response_amx(baseEvnum, text, extra, "");
        }

        private void ApplyCancel()
        {
            lock (_alarmsLock)
            {
                foreach (var alarm in _activeAlarms.Values)
                {
                    if (alarm.NeedsCancel) alarm.Cancelled = true;
                }
                FlushSatisfied_NoLock();
            }
        }

        private void ApplyReset(char resetClass)
        {
            lock (_alarmsLock)
            {
                foreach (var alarm in _activeAlarms.Values)
                {
                    if (alarm.ResetClass == resetClass) alarm.Reset = true;
                }
                FlushSatisfied_NoLock();
            }
        }

        private void FlushSatisfied_NoLock()
        {
            List<ActiveAlarm> done = null;
            foreach (var alarm in _activeAlarms.Values)
            {
                if (alarm.Restored && alarm.Cancelled && alarm.Reset)
                {
                    (done ??= new List<ActiveAlarm>()).Add(alarm);
                }
            }
            if (done == null) return;
            foreach (var alarm in done)
            {
                _activeAlarms.Remove(alarm.BaseEvnum);
                send_response_amx(alarm.BaseEvnum, alarm.Text, "", "");
            }
        }

        private void TrySendOff_NoLock(ActiveAlarm alarm)
        {
            if (alarm.Restored && alarm.Cancelled && alarm.Reset)
            {
                _activeAlarms.Remove(alarm.BaseEvnum);
                NotifyClient($"GALAXY alarm cleared (restored+cancelled+reset): {alarm.Text}");
                send_response_amx(alarm.BaseEvnum, alarm.Text, "", "");
            }
        }
        #endregion

        #region Controls from AMX
        // Galaxy production controls (the VB's reachable set, confirmed by
        // GALAXY.nod [Main] and DTD074 s4): zone omit / un-omit and system
        // reset - nothing else. Set/unset of groups exists on the wire
        // (CSA<g>*<action>) but the legacy NWM never exposed it to AMX -
        // hold until the intruder-semantics decision lands with Mike/James.
        public override void Reset(string passedValues)
        {
            EnqueueCommand(kCmdSet + "*" + kSetActionSystemReset, expectsResponse: true);
        }

        // The Galaxy zone address travels to AMX split across the loop and
        // input bytes of the packed event number (zone 1001 -> loop 3,
        // device 233), so every legacy control path reconstructed it as
        // loop*256 + device (frmGalaxyNetworkManager.frm:1045-1047) - never
        // from a zone field. Accept both shapes: a slot already carrying a
        // full 4-digit address wins; otherwise rebuild from loop/device.
        private static int GalaxyZoneFrom(int loop, int zone, int device)
        {
            if (zone >= 1001) return zone;
            if (device >= 1001) return device;
            return (loop * 256) + device;
        }

        public override void DisableZone(string passedValues)
        {
            ParsePassedValues(passedValues, out _, out int loop, out int zone, out int device);
            SendZoneOmit(GalaxyZoneFrom(loop, zone, device), omit: true);
        }

        public override void EnableZone(string passedValues)
        {
            ParsePassedValues(passedValues, out _, out int loop, out int zone, out int device);
            SendZoneOmit(GalaxyZoneFrom(loop, zone, device), omit: false);
        }

        // The legacy AMX isolate/de-isolate device path drove the same zone
        // omit - Galaxy devices ARE zones (4-digit l0rz addresses).
        public override void DisableDevice(string passedValues)
        {
            ParsePassedValues(passedValues, out _, out int loop, out int zone, out int device);
            SendZoneOmit(GalaxyZoneFrom(loop, zone, device), omit: true);
        }

        public override void EnableDevice(string passedValues)
        {
            ParsePassedValues(passedValues, out _, out int loop, out int zone, out int device);
            SendZoneOmit(GalaxyZoneFrom(loop, zone, device), omit: false);
        }

        private void SendZoneOmit(int galaxyZone, bool omit)
        {
            if (galaxyZone < 1001)
            {
                NotifyClient($"GALAXY omit ignored - '{galaxyZone}' is not a Galaxy zone address (1001..4158)");
                return;
            }
            NotifyClient($"GALAXY {(omit ? "omit" : "un-omit")} zone {galaxyZone}");
            EnqueueCommand(kCmdOmit + galaxyZone + "*" + (omit ? 1 : 0), expectsResponse: true);
        }

        // No Galaxy equivalents - an intruder panel has no evacuate/alert/
        // silence sounder semantics on this interface. Log-and-ignore so a
        // broadcast control from AMX can never throw out of the dispatcher.
        public override void Evacuate(string passedValues) => NotifyClient("GALAXY: Evacuate not supported", false);
        public override void EvacuateNetwork(string passedValues) => NotifyClient("GALAXY: EvacuateNetwork not supported", false);
        public override void Alert(string passedValues) => NotifyClient("GALAXY: Alert not supported", false);
        public override void Silence(string passedValues) => NotifyClient("GALAXY: Silence not supported", false);
        public override void MuteBuzzers(string passedValues) => NotifyClient("GALAXY: MuteBuzzers not supported", false);
        public override void Analogue(string passedValues) => NotifyClient("GALAXY: Analogue not supported", false);
        #endregion
    }
}
