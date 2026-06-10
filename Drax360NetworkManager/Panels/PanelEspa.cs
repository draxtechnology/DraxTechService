using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using DraxTechnology.Data;
using Microsoft.EntityFrameworkCore;

namespace DraxTechnology.Panels
{
    internal class PanelEspa : AbstractPanel
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
        private EspaFramer _framer;
        private readonly List<byte> _buffer = new List<byte>();
        private readonly byte[] _terminator = { 0x0D, 0x0A, 0x0D, 0x0A };
        #endregion

        #region FakeString
        public override string FakeString
        {
            get
            {
                string msg = "Receiving : Poll   ESPA  interface   1<ENQ>" + (char)13 + (char)10;


                msg += "Receiving: EspaString < SOH > 1 < STX > 1 < US > 999 < RS > 2 < US > Fire Alarm ZONE 1 - MAINBUILDING #  A1003 -INPUTALARM # <RS>3<US>5<RS>4<US>3<RS>5<US>3<RS>6<US>1<ETX>+";
                msg += "Pager Address : 999";
                msg += "Pager Text    : | Fire Alarm ZONE 1 - MAINBUILDING #  A1003 -INPUT|";
                msg += "Pager Text    : | ALARM #                                         |";
                msg += "Pager Ctrl    : Beeps = 5, Type = 3, Trans = 3, Pri = 1, ";
                msg += "Checksum: OK";
                msg += "---------------- -: 2026 - 06 - 10 13:54:45.193";
                msg += "" + (char)13 + (char)10;
                msg += "" + (char)13 + (char)10;
                msg += "Receiving: EspaString < SOH > 1 < STX > 1 < US > 999 < RS > 2 < US > Fire Alarm Fault #     Autrocom    Serial ESPA -DRAX # Lossof          communication <RS>3<US>2<RS>4<US>3<RS>5<US>2<RS>6<US>3<ETX>B";
                msg += "Pager Address : 999";
                msg += "Pager Text    : | Fire Alarm Fault #     Autrocom    Serial ESPA |";
                msg += "Pager Text    : | -DRAX # Lossof          communication           |";
                msg += "Pager Ctrl    : Beeps = 2, Type = 3, Trans = 2, Pri = 3, ";
                msg += "Checksum: OK";
                msg += "---------------- -: 2026 - 06 - 10 13:54:46.019";
                msg += "" + (char)13 + (char)10;
                msg += "" + (char)13 + (char)10;
                msg += "Receiving: EspaString < SOH > 1 < STX > 1 < US > 999 < RS > 2 < US > Fire Alarm Fault #     A1005 -     AutroGuard  VAD #       Missing     addon board <RS>3<US>2<RS>4<US>3<RS>5<US>2<RS>6<US>3<ETX>L";
                msg += "Pager Address : 999";
                msg += "Pager Text    : | Fire Alarm Fault #     A1005 -     AutroGuard  |";
                msg += "Pager Text    : | VAD #       Missing     addon board             |";
                msg += "Pager Ctrl    : Beeps = 2, Type = 3, Trans = 2, Pri = 3, ";
                msg += "Checksum: OK";
                msg += "---------------- -: 2026 - 06 - 10 13:56:02.808";
                msg += "" + (char)13 + (char)10;
                msg += "" + (char)13 + (char)10;
                msg += "Receiving: EspaString < SOH > 1 < STX > 1 < US > 999 < RS > 2 < US > Fire Alarm Fault #     AutroGuard  CO_Sounder #Missing     addon board <RS>3<US>2<RS>4<US>3<RS>5<US>2<RS>6<US>3<ETX>P";
                msg += "Pager Address : 999";
                msg += "Pager Text    : | Fire Alarm Fault #     AutroGuard  CO_Sounder #|";
                msg += "Pager Text    : | Missing     addon board                         |";
                msg += "Pager Ctrl: Beeps = 2, Type = 3, Trans = 2, Pri = 3, ";
                msg += "Checksum: OK";
                msg += "---------------- -: 2026 - 06 - 10 15:14:23.953";
                msg += "" + (char)13 + (char)10;
                msg += "" + (char)13 + (char)10;
                msg += "Receiving: EspaString < SOH > 1 < STX > 1 < US > 999 < RS > 2 < US > Fire Alarm Fault #     A1006 -     AutroGuard  CO # Missingdetector <RS>3<US>2<RS>4<US>3<RS>5<US>2<RS>6<US>3<ETX>[0x10]";
                msg += "Pager Address : 999";
                msg += "Pager Text    : | Fire Alarm Fault #     A1006 -     AutroGuard  |";
                msg += "Pager Text    : | CO # Missingdetector                            |";
                msg += "Pager Ctrl    : Beeps = 2, Type = 3, Trans = 2, Pri = 3, ";
                msg += "Checksum: OK";
                msg += "---------------- -: 2026 - 06 - 10 15:14:24.863";

                return msg;
            }
        }
        #endregion

        #region Constructor
        public PanelEspa(string baselogfolder, string identifier)
            : base(baselogfolder, identifier, "Espa", "ESPA")
        {
            if (!string.IsNullOrEmpty(identifier))
            {
                heartbeat_timer = new Timer(
                    heartbeat_timer_callback, this.Identifier,
                    500, kheartbeatdelayseconds * 1000);

                this.Offset = base.GetSetting<int>(ksettingsetupsection, "giAmx1Offset");
                KSFUseLoop = base.GetSetting<int>(ksettingsetupsection, "UseLoop");

                string dbPath = Path.Combine(baselogfolder, "events.db");
                EspaEventsLegacyMigrator.EnsureMigrated(dbPath, msg => this.NotifyClient(msg));

                _eventsDb = new EspaEventsContext(dbPath);
                _eventsDb.Database.EnsureCreated();

                foreach (var ev in _eventsDb.Events.AsNoTracking().OrderBy(e => e.Id))
                {
                    _eventCache[ev.Name] = (ev.Node, ev.Loop, ev.Device);
                    _nextAssignment = (ev.Node, ev.Loop, ev.Device);
                }

                this.NotifyClient(
                    "ESPA Events DB: " + _eventCache.Count + " device(s) loaded into cache, " +
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
                base.NotifyClient("ESPA running in FAKE mode — serial port not opened.", false);
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

            _framer = new EspaFramer(bytes =>
            {
                if (serialport != null && serialport.IsOpen)
                    serialport.Write(bytes, 0, bytes.Length);
            });

            _framer.FrameReceived += OnEspaFrame;
            _framer.Log += msg => base.NotifyClient(msg, false);

            base.NotifyClient("ESPA framer started — waiting for panel polls.", false);
        }
        #endregion

        #region SerialPort_DataReceived
        public override void SerialPort_Datareceived(object sender, SerialDataReceivedEventArgs e)
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

            if (_framer != null)
            {
                int offset = 0;
                while (offset < read)
                {
                    byte[] chunk = incoming.Skip(offset).Take(read - offset).ToArray();
                    int consumed = _framer.Feed(chunk);
                    base.NotifyClient($"Framer consumed {consumed} of {chunk.Length} bytes", false);

                    if (consumed == 0)
                    {
                        lock (_buffer) { _buffer.AddRange(chunk); ExtractMessages(); }
                        break;
                    }
                    offset += consumed;
                }
                return;
            }

            lock (_buffer) { _buffer.AddRange(incoming.Take(read)); ExtractMessages(); }
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

        private bool processmessage(string result)
        {
            string[] lines = result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string lineLower = Regex.Replace(line.ToLower(), @"\s+", " ").Trim();
                if (!lineLower.Contains("pager text")) continue;
                processEventLine(line);
            }
            return true;
        }

        // Decodes a single pager-text line and routes the event to AMX.
        // Works for both fake-mode log lines (containing "Pager Text : | ... <RS>...")
        // and real-frame DisplayText ("Fire Alarm ZONE 1 ... # device #").
        private void processEventLine(string line)
        {
            bool on = true;
            int tIpType = 0;
            int p1 = 0;
            int evnum = 0;
            string gsLine1 = "";
            string gsLine2 = "";

            gsTextField = "";
            gsTextField2 = "";
            gsDeviceText = "";

            string lineLower = Regex.Replace(line.ToLower(), @"\s+", " ").Trim();

            if (lineLower.Contains("fire alarm fault"))
            {
                tIpType = 8;
                gsTextField = line.Substring(line.ToLower().IndexOf("fire alarm fault"));
                int rsIdx = gsTextField.ToLower().IndexOf("<rs>");
                if (rsIdx >= 0) gsTextField = gsTextField.Substring(0, rsIdx);

                gsDeviceText = gsTextField.Substring(gsTextField.IndexOf("#") + 1).Trim();
                gsDeviceText = gsDeviceText.Substring(0, gsDeviceText.IndexOf("#")).Trim();
                gsDeviceText = Regex.Replace(gsDeviceText, @" {2,}", " ").Trim();

                gsTextField = gsTextField.Replace("#", "").Trim();
                gsTextField = Regex.Replace(gsTextField, @" {2,}", " ").Trim();
                if (gsTextField.Length > 40)
                {
                    int iSplit = gsTextField.LastIndexOfAny(new[] { ' ', '-' }, 40);
                    if (iSplit > 0)
                    {
                        gsLine1 = gsTextField.Substring(0, iSplit).TrimEnd();
                        gsLine2 = gsTextField.Substring(iSplit).TrimStart(' ').Trim();
                    }
                    else
                    {
                        gsLine1 = gsTextField.Substring(0, 40);
                        gsLine2 = gsTextField.Substring(40);
                    }
                    gsTextField = gsLine1;
                    gsTextField2 = gsLine2;
                }
            }
            else if (lineLower.Contains("fire pre alarm"))
            {
                tIpType = 2;
                gsTextField = line.Substring(line.ToLower().IndexOf("fire pre alarm"));
                int rsIdx = gsTextField.ToLower().IndexOf("<rs>");
                if (rsIdx >= 0) gsTextField = gsTextField.Substring(0, rsIdx);

                gsDeviceText = gsTextField.Substring(gsTextField.IndexOf("#") + 1).Trim();
                gsDeviceText = gsDeviceText.Substring(0, gsDeviceText.IndexOf("#")).Trim();
                gsDeviceText = Regex.Replace(gsDeviceText, @" {2,}", " ").Trim();

                gsTextField = gsTextField.Replace("#", "").Trim();
                gsTextField = Regex.Replace(gsTextField, @" {2,}", " ").Trim();
                if (gsTextField.Length > 40)
                {
                    int iSplit = gsTextField.LastIndexOfAny(new[] { ' ', '-' }, 40);
                    if (iSplit > 0)
                    {
                        gsLine1 = gsTextField.Substring(0, iSplit).TrimEnd();
                        gsLine2 = gsTextField.Substring(iSplit).TrimStart(' ').Trim();
                    }
                    else
                    {
                        gsLine1 = gsTextField.Substring(0, 40);
                        gsLine2 = gsTextField.Substring(40);
                    }
                    gsTextField = gsLine1;
                    gsTextField2 = gsLine2;
                }
            }
            else if (lineLower.Contains("fire alarm"))
            {
                tIpType = 0;
                gsTextField = line.Substring(line.ToLower().IndexOf("fire alarm"));
                int rsIdx = gsTextField.ToLower().IndexOf("<rs>");
                if (rsIdx >= 0) gsTextField = gsTextField.Substring(0, rsIdx);

                gsDeviceText = gsTextField.Substring(gsTextField.IndexOf("#") + 1).Trim();
                gsDeviceText = gsDeviceText.Substring(0, gsDeviceText.IndexOf("#")).Trim();
                gsDeviceText = Regex.Replace(gsDeviceText, @" {2,}", " ").Trim();

                gsTextField = gsTextField.Replace("#", "").Trim();
                gsTextField = Regex.Replace(gsTextField, @" {2,}", " ").Trim();
                if (gsTextField.Length > 40)
                {
                    int iSplit = gsTextField.LastIndexOfAny(new[] { ' ', '-' }, 40);
                    if (iSplit > 0)
                    {
                        gsLine1 = gsTextField.Substring(0, iSplit).TrimEnd();
                        gsLine2 = gsTextField.Substring(iSplit).TrimStart(' ').Trim();
                    }
                    else
                    {
                        gsLine1 = gsTextField.Substring(0, 40);
                        gsLine2 = gsTextField.Substring(40);
                    }
                    gsTextField = gsLine1;
                    gsTextField2 = gsLine2;
                }
            }

            if (gsTextField.Length > 0)
            {
                try
                {
                    p1 = (int)(enmNotAlarmType)Enum.Parse(typeof(enmNotAlarmType), tIpType.ToString());
                }
                catch (Exception ex)
                {
                    this.NotifyClient("tIpType parse error: " + tIpType + " " + ex.Message, false);
                }

                var addr = AssignOrLookup(gsDeviceText, null, null);
                giLoopNumber = addr.Loop;
                giDeviceAddress = addr.Device;

                evnum = CSAMXSingleton.CS.MakeInputNumber(
                    addr.Node, addr.Loop, addr.Device, p1, on);

                base.NotifyClient("Send to AMX: Node=" + (addr.Node + this.Offset) +
                                  " Loop=" + addr.Loop + " Address=" + addr.Device);

                Thread.Sleep(500);

                if (gsTextField2.StartsWith("-"))
                    gsTextField2 = gsTextField2.Substring(1);

                send_response_amx_and_serial(evnum, gsTextField, "", gsTextField2);
            }
        }
        #endregion

        #region OnEspaFrame — real panel byte-protocol path
        private void OnEspaFrame(EspaRecord rec)
        {
            string text = rec?.DisplayText ?? "";
            if (string.IsNullOrWhiteSpace(text)) return;

            base.NotifyClient("ESPA Frame received: " + text, false);
            processEventLine(text);
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
                            this.NotifyClient("ESPA Events DB: SaveChanges failed for '" + devicetext +
                                "'; reused existing (" + found.Node + "," + found.Loop + "," + found.Device + ")");
                            return found;
                        }
                        this.NotifyClient("ESPA Events DB: SaveChanges failed for '" + devicetext + "': " + ex.Message);
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
