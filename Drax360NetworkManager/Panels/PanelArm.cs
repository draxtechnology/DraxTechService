using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using DraxTechnology.Data;
using Microsoft.EntityFrameworkCore;

namespace DraxTechnology.Panels
{
    internal class PanelArm : AbstractPanel
    {
        #region Constants
        private const int MAXINPUTSTRINGS = 5;
        private const byte kheartbeatdelayseconds = 1;
        #endregion

        #region Fields
        public string[] Ip = new string[MAXINPUTSTRINGS];
        public string[] UserMessages = new string[16];
        public int[] UserTypes = new int[16];
        public int giZoneNumber = 0;
        public int giDeviceSubAddress = 0;
        public string gsTextField = "";
        public string gsTextField2 = "";
        public string gsDeviceText = "";
        public string gsZoneText = "";
        public int giDeviceAddress = 0;
        public int giLoopNumber = 0;
        public bool LocalInputUnit = false;
        public int KSFUseLoop = 0;
        public int index = 0;
        public int giAnalogRequestLoop = 0;
        #endregion

        #region EF Core / event cache
        private EspaEventsContext _eventsDb;
        private readonly object _eventsDbLock = new object();
        private readonly Dictionary<string, (int Node, int Loop, int Device)> _eventCache
            = new Dictionary<string, (int, int, int)>(StringComparer.Ordinal);
        private (int Node, int Loop, int Device) _nextAssignment = (1, 1, 0);
        #endregion

        #region Serial / framer
        private readonly List<byte> _buffer = new List<byte>();
        // Wire format is plain ASCII lines ending LF CR (matches FakeString),
        // not ESPA CRLF-CRLF framing.
        private readonly byte[] _terminator = { 0x0A, 0x0D };
        #endregion

        #region FakeString
        public override string FakeString
        {
            get
            {
                string msg = "497 09:43 22/12 W54 \n\r";
                msg += "EMERGENCY ALM \n\r";
                msg += "498 09:43 22/12 W54 \n\r";
                msg += "EMERGENCY CLR \n\r";

                msg += "335 09:42 20/12 88776 \n\r";
                msg += "PATIENT CALL \n\r";
                msg += "336 09:42 20/12 88776 \n\r";
                msg += "PATIENT CLR \n\r";

                msg += "499 09:51 20/12 88776 \n\r";
                msg += "JACK REMOVED FLT \n\r";
                msg += "500 09:51 20/12 88776 \n\r";
                msg += "JACK REMOVED CLR \n\r";

                msg += "337 10:12 20/12 88776 PATIE CALL \n\r";
                msg += "PATIENT 32 \n\r";
                msg += "338 10:12 20/12 88776 \n\r";
                msg += "PATIENT CLR \n\r";

                return msg;
            }
        }
        public override string PanelVersion => "1.0.0.0";
        #endregion

        #region Constructor
        public PanelArm(string baselogfolder, string identifier)
            : base(baselogfolder, identifier, "Arm", "ARM")
        {
            if (!string.IsNullOrEmpty(identifier))
            {
                heartbeat_timer = new Timer(
                    heartbeat_timer_callback, this.Identifier,
                    500, kheartbeatdelayseconds * 1000);

                this.Offset = base.GetSetting<int>(ksettingsetupsection, "giAmx1Offset");
                KSFUseLoop = base.GetSetting<int>(ksettingsetupsection, "UseLoop");

                string dbPath = Path.Combine(baselogfolder, "data\\events.db");
                EspaEventsLegacyMigrator.EnsureMigrated(dbPath, msg => this.NotifyClient(msg));

                _eventsDb = new EspaEventsContext(dbPath);
                _eventsDb.Database.EnsureCreated();

                foreach (var ev in _eventsDb.Events.AsNoTracking().OrderBy(e => e.Id))
                {
                    _eventCache[ev.Name] = (ev.Node, ev.Loop, ev.Device);
                    _nextAssignment = (ev.Node, ev.Loop, ev.Device);
                }

                this.NotifyClient(
                    "ARM Events DB: " + _eventCache.Count + " device(s) loaded into cache, " +
                    "next assignment after (" + _nextAssignment.Node + "," +
                    _nextAssignment.Loop + "," + _nextAssignment.Device + ")");
            }
        }
        #endregion

        #region StartUp
        public override void StartUp(int fakemode)
        {
            int settingBaudRate = base.GetSetting<int>("SetUp", "BaudRate");
            string settingParity = base.GetSetting<string>("SetUp", "Parity");
            int settingDataBits = base.GetSetting<int>("SetUp", "DataBits");
            int settingStopBits = base.GetSetting<int>("SetUp", "StopBits");

            if (fakemode > 0)
            {
                base.NotifyClient("ARM running in FAKE mode — serial port not opened.", false);
                return;
            }

            serialport = new SerialPort(this.Identifier);
            serialport.BaudRate = settingBaudRate;

            Parity parity = Parity.None;
            if (!string.IsNullOrEmpty(settingParity))
            {
                string p = settingParity.Substring(0, 1).ToUpper();
                if (p == "E") parity = Parity.Even;
                if (p == "O") parity = Parity.Odd;
            }
            serialport.Parity = parity;
            serialport.DataBits = settingDataBits;
            serialport.StopBits = (StopBits)settingStopBits;
            serialport.Handshake = Handshake.None;
            serialport.DtrEnable = true;
            serialport.Encoding = Encoding.ASCII;
            serialport.ReadBufferSize = 8000;
            serialport.ReadTimeout = 500;
            serialport.ParityReplace = 0;
            serialport.ReceivedBytesThreshold = 1;

            serialport.DataReceived += SerialPort_Datareceived;

            if (serialport.IsOpen) serialport.Close();

            base.NotifyClient("Attempting open " + serialport.PortName +
                              " @ " + settingBaudRate + " baud", false);
            try
            {
                serialport.Open();
            }
            catch (Exception ex)
            {
                base.NotifyClient("Failed to open " + serialport.PortName +
                                  ": " + ex.Message, false);
                return;
            }

            if (serialport.IsOpen)
            {
                serialport.DiscardInBuffer();
                serialport.DiscardOutBuffer();
                base.NotifyClient("Serial port " + serialport.PortName + " open OK.", false);
            }

            base.NotifyClient("ARM serial reader started — waiting for panel data.", false);
        }
        #endregion

        #region SerialPort_DataReceived
        public override void SerialPort_Datareceived(object sender, SerialDataReceivedEventArgs e)
        {
            // Guard the whole handler: an exception escaping a SerialPort event
            // thread takes the service process down (the base handler is guarded;
            // this override must be too).
            try
            {
                lastDataReceived = DateTime.Now;

                int bytesToRead = serialport.BytesToRead;
                if (bytesToRead <= 0) return;

                byte[] incoming = new byte[bytesToRead];
                int read = serialport.Read(incoming, 0, bytesToRead);
                if (read <= 0) return;

                string hex = BitConverter.ToString(incoming, 0, read).Replace("-", " ");
                string asc = new string(incoming.Take(read)
                    .Select(b => b >= 0x20 && b < 0x7F ? (char)b : '.').ToArray());
                base.NotifyClient($"RX {read} bytes | HEX: {hex} | ASC: {asc}", false);

                lock (_buffer) { _buffer.AddRange(incoming.Take(read)); ExtractMessages(); }
            }
            catch (Exception ex)
            {
                base.NotifyClient("ARM receive error: " + ex.Message, false);
            }
        }

        // Device text sits between a pair of '#' characters in the pager line.
        // Pager text without the full pair must not throw on the receive thread —
        // fall back to empty device text instead.
        private static string ExtractHashDelimitedDeviceText(string text)
        {
            int first = text.IndexOf('#');
            if (first < 0) return "";
            string rest = text.Substring(first + 1);
            int second = rest.IndexOf('#');
            if (second < 0) return "";
            return Regex.Replace(rest.Substring(0, second), @" {2,}", " ").Trim();
        }

        private void ExtractMessages()
        {
            while (true)
            {
                int pos = FindPattern(_buffer, _terminator);
                if (pos == -1) return;
                int end = pos + _terminator.Length;
                byte[] message = _buffer.Take(end).ToArray();
                _buffer.RemoveRange(0, end);
                Parse(message);
            }
        }
        private int FindPattern(List<byte> buffer, byte[] pattern)
        {
            for (int i = 0; i <= buffer.Count - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                    if (buffer[i + j] != pattern[j]) { match = false; break; }
                if (match) return i;
            }
            return -1;
        }
        #endregion

        #region Parse / processmessage — legacy log-scrape / fake mode
        public override void Parse(byte[] buffer)
        {
            base.Parse(buffer);
            if (buffer.Length > 0)
                processmessage(Encoding.UTF8.GetString(buffer));
        }

        // Header line shape: "<event number> <hh:mm> <dd/MM> <zone/device>",
        // e.g. "497 09:43 22/12 W54". The text on the following line
        // ("EMERGENCY ALM", "PATIENT CALL", ...) belongs to that header.
        // This is the FakeString shape only — the real panel never sends it.
        private static readonly Regex HeaderLineRegex =
            new Regex(@"^\d+\s+\d{2}:\d{2}\s+\d{2}/\d{2}\s+\S+", RegexOptions.Compiled);

        // Real panel wire shape — one line per event, columns fixed-width:
        // "2   8    1 200 14:56 19/11 BED 1             EMERGENCY         ALM"
        // "2   0    1 202 14:57 19/11 BED 1                 PRESENCE"
        // (const, event-class code, const, sequence#, time, date, device, event type, optional status)
        private static readonly Regex RealLineRegex = new Regex(
            @"^\d+\s+\d+\s+\d+\s+\d+\s+\d{2}:\d{2}\s+\d{2}/\d{2}\s+(?<device>.+?)\s{2,}(?<eventtype>[A-Z][A-Z ]*?)(?:\s+(?<status>ALM|CLR))?\s*$",
            RegexOptions.Compiled);

        private bool processmessage(string result)
        {
            string[] lines = result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(l => l.Trim())
                                    .Where(l => l.Length > 0)
                                    .ToArray();

            for (int i = 0; i < lines.Length; i++)
            {
                Match realMatch = RealLineRegex.Match(lines[i]);
                if (realMatch.Success)
                {
                    string device = realMatch.Groups["device"].Value.Trim();
                    string eventType = realMatch.Groups["eventtype"].Value.Trim();
                    string status = realMatch.Groups["status"].Success ? realMatch.Groups["status"].Value : "";
                    processEventLine(eventType, status, device);
                    continue;
                }

                if (!HeaderLineRegex.IsMatch(lines[i])) continue;

                string headerLine = lines[i];
                string eventText = (i + 1 < lines.Length) ? lines[i + 1] : "";
                i++; // consume the paired text line so it isn't re-scanned as a header

                processEventLine(headerLine, eventText);
            }
            return true;
        }

        private void processEventLine(string headerLine, string eventText, string deviceTextOverride = null)
        {
            bool on = true;
            int p1 = 0;
            int evnum = 0;

            gsTextField = "";
            gsTextField2 = "";
            gsDeviceText = "";

            gsTextField = headerLine;
            gsTextField2 = eventText;

            if (gsTextField.Length > 0)
            {
                switch (true)
                {
                    case var _ when gsTextField.Contains("EMERGENCY"):  // ALARM level event log
                        p1 = 0;
                        if (gsTextField2.Contains("CLR")) on = false;
                        break;

                    case var _ when gsTextField.Contains("498"):  // ALARM level event log clear
                        p1 = 0;
                        on = false;
                        break;

                    case var _ when gsTextField.Contains("335"):  // CALL level event log
                        p1 = 1;
                        break;

                    case var _ when gsTextField.Contains("336"):  // CALL level event log clear
                        p1 = 1;
                        on = false;
                        break;

                    case var _ when gsTextField.Contains("337"):  // Resident keyfob IR call
                        p1 = 1;
                        break;

                    case var _ when gsTextField.Contains("338"):  // Resident keyfob IR call clear
                        p1 = 1;
                        on = false;
                        break;

                    case var _ when gsTextField.Contains("499"):  // FAULT level event log
                        p1 = 8;
                        break;

                    case var _ when gsTextField.Contains("500"):  // FAULT level event log clear
                        p1 = 8;
                        on = false;
                        break;
                }

                gsTextField = deviceTextOverride ?? gsTextField.Substring(15).Trim();
                gsDeviceText = gsTextField;

                var addr = AssignOrLookup(gsDeviceText, null, null);
                giLoopNumber = addr.Loop;
                giDeviceAddress = addr.Device;

                // Inbound events must carry the configured AMX offset: the
                // log line below always printed the offset node while the
                // event itself went out raw — on an offset site events landed
                // on the wrong AMX node with a log that looked correct.
                evnum = CSAMXSingleton.CS.MakeInputNumber(
                    addr.Node + this.Offset, addr.Loop, addr.Device, p1, on);

                base.NotifyClient("Send to AMX: Node=" + (addr.Node + this.Offset) + " Loop=" + addr.Loop + " Address=" + addr.Device);
                base.NotifyClient("Send to AMX: gsTextField=" + gsTextField);
                base.NotifyClient("Send to AMX: gsTextField2=" + gsTextField2);

                Thread.Sleep(500);

                if (gsTextField2.StartsWith("-"))
                    gsTextField2 = gsTextField2.Substring(1);

                send_response_amx_and_serial(evnum, gsTextField, "", gsTextField2);
            }
        }
        #endregion

        // Add to SQL Lite DB if not found, with optional hints to reuse existing loop/device for known zones/devices

        #region AssignOrLookup
        private (int Node, int Loop, int Device) AssignOrLookup(
            string devicetext, int? hintLoop, int? hintDevice)
        {
            if (hintLoop.HasValue && hintDevice.HasValue)
                return (1, hintLoop.Value, hintDevice.Value);

            lock (_eventsDbLock)
            {
                if (!string.IsNullOrEmpty(devicetext) &&
                    _eventCache.TryGetValue(devicetext, out var cached))
                    return cached;

                var next = _nextAssignment;
                next.Device++;
                if (next.Device > 254) { next.Device = 1; next.Loop++; }
                if (next.Loop > 254) { next.Loop = 1; next.Node++; }
                if (next.Node > 254)
                    throw new Exception("Maximum node/loop/device limit reached");

                if (!string.IsNullOrEmpty(devicetext))
                {
                    var entity = new EspaEvent
                    { Node = next.Node, Loop = next.Loop, Device = next.Device, Name = devicetext };
                    _eventsDb.Events.Add(entity);
                    try { _eventsDb.SaveChanges(); }
                    catch (Exception ex)
                    {
                        _eventsDb.Entry(entity).State = EntityState.Detached;
                        var existing = _eventsDb.Events.AsNoTracking()
                                                .FirstOrDefault(e => e.Name == devicetext);
                        if (existing != null)
                        {
                            var found = (existing.Node, existing.Loop, existing.Device);
                            _eventCache[devicetext] = found;
                            this.NotifyClient("ARM Events DB: SaveChanges failed for '" + devicetext +
                                "'; reused existing (" + found.Node + "," + found.Loop + "," + found.Device + ")");
                            return found;
                        }
                        this.NotifyClient("ARM Events DB: SaveChanges failed for '" + devicetext + "': " + ex.Message);
                        throw;
                    }
                    _eventCache[devicetext] = next;
                }

                _nextAssignment = next;
                return next;
            }
        }
        #endregion

        #region AMX response
        private void send_response_amx_and_serial(
            int evnum, string message1, string message2, string message3 = "")
        {
            string friendly = message2 + (message3.Length > 0 ? " " + message3 : "");
            this.NotifyClient(friendly, false);
            CSAMXSingleton.CS.SendAlarmToAMX(evnum, message1, message2, message3);
            CSAMXSingleton.CS.FlushMessages();
        }
        #endregion

        #region Heartbeat
        protected override void heartbeat_timer_callback(object sender) { }
        #endregion

        #region Abstract overrides
        public override void Evacuate(string p) { }
        public override void Alert(string p) { }
        public override void EvacuateNetwork(string p) { }
        public override void Silence(string p) { }
        public override void MuteBuzzers(string p) { }
        public override void Reset(string p) { }
        public override void DisableDevice(string p) { }
        public override void EnableDevice(string p) { }
        public override void DisableZone(string p) { }
        public override void EnableZone(string p) { }
        public override void Analogue(string p) { }
        #endregion
    }
}
