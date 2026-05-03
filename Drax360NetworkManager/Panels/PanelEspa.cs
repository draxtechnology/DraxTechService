
using DraxTechnology.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Timers;
using System.Xml.Linq;
using static DraxTechnology.AMXTransfer;
using static DraxTechnology.Panels.PanelTaktis;

namespace DraxTechnology.Panels
{
    internal class PanelEspa : AbstractPanel
    {
        #region constants

        const int MAXINPUTSTRINGS = 5;
        const byte kheartbeatdelayseconds = 1;

        #endregion

        public string[] Ip = new string[MAXINPUTSTRINGS];
        public string[] UserMessages = new string[16];
        public int[] UserTypes = new int[16];
        public int giZoneNumber = 0;
        public int giDeviceSubAddress = 0;
        public string gsTextField = "";
        public string gsDeviceText = "";
        public string gsZoneText = "";
        public int giDeviceAddress = 0;
        public int giLoopNumber = 0;
        public bool LocalInputUnit = false;
        public int KSFUseLoop = 0;
        public int index = 0;
        public int giAnalogRequestLoop = 0;

        // EF Core DbContext for the ESPA events store. Opened once at
        // construction and reused for the lifetime of the panel.
        // EspaEventsLegacyMigrator runs first to bring any pre-EF database up
        // to the current schema before the context takes over.
        private EspaEventsContext _eventsDb;
        private readonly object _eventsDbLock = new object();

        // In-memory cache of devicetext -> (node, loop, device). Pre-populated
        // from the Events table at startup, then updated on each new INSERT.
        // Repeat events hit only the cache; first-sight events do a single INSERT.
        // The cache is what makes lookups effectively instant — the database
        // is only touched on first-sight devices and at process start.
        private readonly Dictionary<string, (int Node, int Loop, int Device)> _eventCache
            = new Dictionary<string, (int, int, int)>(StringComparer.Ordinal);
        private (int Node, int Loop, int Device) _nextAssignment = (1, 1, 0);

        // ESPA 4.4.4 byte-level framer. Constructed once the serial port is open
        // (so the framer can write ACK/NAK back through it). Stays null in
        // FakeMode and falls through to the legacy log-scrape path.
        private EspaFramer _framer;

        public override string FakeString
        {
            get
            {

                string msg = "Receiving : Poll   ESPA  interface   1<ENQ>" + (char)13 + (char)10;
                msg += "Receiving : Select pager transmitter 2<ENQ>" + (char)13 + (char)10;
                msg += "SENDING   : <ACK>" + (char)13 + (char)10;
                msg += "Receiving : EspaString <SOH>1<STX>1<US>999<RS>2<US>Fire Alarm  Fault -     AutroMaster Switchboard Loss of     communication <RS>3<US>2<RS>4<US>3<RS>5<US>2<RS>6<US>3<ETX>" + (char)13 + (char)10;
                msg += "   Pager Address : 999" + (char)13 + (char)10;
                msg += "   Pager Text    : |Fire Alarm  Fault -     AutroMaster Switchboard |" + (char)13 + (char)10;
                msg += "   Pager Text    : |Loss of     communication                       |" + (char)13 + (char)10;
                msg += "   Pager Ctrl    : Beeps=2, Type=3, Trans=2, Pri=3, " + (char)13 + (char)10;
                msg += "   Checksum      : OK" + (char)13 + (char)10;
                msg += "-----------------: 2026-04-16 12:24:23.750 " + (char)13 + (char)10;
                msg += "SENDING   : <ACK>" + (char)13 + (char)10;
                msg += "Receiving : <EOT>" + (char)13 + (char)10;
                msg += "" + (char)13 + (char)10;
                msg += "" + (char)13 + (char)10;
                msg += "Receiving : Select pager transmitter 2<ENQ>" + (char)13 + (char)10;
                msg += "SENDING   : <ACK>" + (char)13 + (char)10;
                msg += "Receiving : EspaString <SOH>1<STX>1<US>999<RS>2<US>Fire Alarm -ZONE 1 -MAINBUILDING    A1003 -INPUTALARM <RS>3<US>5<RS>4<US>3<RS>5<US>3<RS>6<US>1<ETX>&" + (char)13 + (char)10;
                msg += "   Pager Address : 999" + (char)13 + (char)10;
                msg += "   Pager Text    : |Fire Alarm -ZONE 1 -MAINBUILDING    A1003 -INPUT|" + (char)13 + (char)10;
                msg += "   Pager Text    : |ALARM                                           |" + (char)13 + (char)10;
                msg += "   Pager Ctrl    : Beeps=5, Type=3, Trans=3, Pri=1, " + (char)13 + (char)10;
                msg += "   Checksum      : OK" + (char)13 + (char)10;
                msg += "-----------------: 2026-04-16 12:36:07.798 " + (char)13 + (char)10;
                msg += "SENDING   : <ACK>" + (char)13 + (char)10;
                msg += "" + (char)13 + (char)10;
                msg += "" + (char)13 + (char)10;
                msg += "-----------------: 2026-04-16 13:04:10.512 " + (char)13 + (char)10;
                msg += "   Pager Address : 999" + (char)13 + (char)10;
                msg += "   Pager Text    : |Fire Alarm  Fault -     AutroGuard  CO_Sounder  |" + (char)13 + (char)10;
                msg += "   Pager Text    : |Missing     addon board                         |" + (char)13 + (char)10;
                msg += "   Pager Ctrl    : Beeps=2, Type=3, Trans=2, Pri=3, " + (char)13 + (char)10;
                msg += "-----------------: 2026-04-16 13:04:11.319 " + (char)13 + (char)10;
                msg += "   Pager Address : 999" + (char)13 + (char)10;
                msg += "   Pager Text    : |Fire Alarm  Fault -ZONE 1 -MAIN     BUILDING    |" + (char)13 + (char)10;
                msg += "   Pager Text    : |Faulty      point(s) in zone                    |" + (char)13 + (char)10;
                msg += "   Pager Ctrl    : Beeps=2, Type=3, Trans=2, Pri=3, " + (char)13 + (char)10;
                msg += "-----------------: 2026-04-16 13:04:12.120 " + (char)13 + (char)10;
                msg += "" + (char)13 + (char)10;
                msg += "" + (char)13 + (char)10;
                msg += "" + (char)13 + (char)10;
                msg += "Receiving : <EOT>" + (char)13 + (char)10;
                msg += "   Pager Address : 999" + (char)13 + (char)10;
                msg += "   Pager Text    : |Fire Alarm -ZONE 1 -MAINBUILDING    A1005 -     |" + (char)13 + (char)10;
                msg += "   Pager Text    : |AutroGuard  SD                                  |" + (char)13 + (char)10;
                msg += "   Pager Ctrl    : Beeps=5, Type=3, Trans=3, Pri=1, " + (char)13 + (char)10;
                msg += "-----------------: 2026-04-16 13:34:58.152 " + (char)13 + (char)10;
                msg += "   Pager Address : 999" + (char)13 + (char)10;
                msg += "   Pager Text    : |Fire Alarm -ZONE 1 -MAINBUILDING    A1005 -     |" + (char)13 + (char)10;
                msg += "   Pager Text    : |AutroGuard  SD                                  |" + (char)13 + (char)10;
                msg += "   Pager Ctrl    : Beeps=5, Type=3, Trans=3, Pri=1, " + (char)13 + (char)10;
                msg += "-----------------: 2026-04-16 13:35:06.781 " + (char)13 + (char)10;

                return msg;
            }
        }

        public PanelEspa(string baselogfolder, string identifier) : base(baselogfolder, identifier, "KsfMan", "ESPA")
        {
            if (!String.IsNullOrEmpty(identifier))
            {
                heartbeat_timer = new System.Threading.Timer(heartbeat_timer_callback, this.Identifier, 500, kheartbeatdelayseconds * 1000);
                this.Offset = base.GetSetting<int>(ksettingsetupsection, "giAmx1Offset");
                KSFUseLoop = base.GetSetting<int>(ksettingsetupsection, "UseLoop");

                string dbPath = Path.Combine(baselogfolder, "events.db");

                // Bring any legacy schema up to the current version before EF
                // attaches. EnsureCreated() afterwards is a no-op if the table
                // already exists, and creates it from the entity model if not.
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

        public override void Parse(byte[] buffer)
        {
            base.Parse(buffer);
            int bufferlength = buffer.Length;
            string result = Encoding.UTF8.GetString(buffer);

            if (bufferlength > 0)
            {
                processmessage(result);
            }
        }
        private bool processmessage(string result)
        {
            gsDeviceText = "";
            int giNodeNumber = 1;
            bool on = true;
            int tIpType = 0;
            int p1 = 0;
            int evnum = 0;
            string gAlarmType = "";

            string[] lines = result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < lines.Length; i++)
            {

                string line = lines[i];
                string lineLower = Regex.Replace(line.ToLower(), @"\s+", " ").Trim();
                gsDeviceText = "";

                if (lineLower.Contains("pager text"))
                {
                    if (lineLower.Contains("fire alarm fault"))
                    {
                        tIpType = 8;
                        gsTextField = line.Substring(19);   //Remove the text Pager Text
                        gsTextField = gsTextField.Replace("-", " - ");
                        gsTextField = Regex.Replace(gsTextField, @"\s+", " ").Trim();
                        gsTextField = gsTextField.Replace("|", "");
                        gsTextField = gsTextField.Substring(19).Trim();
                        if (gsTextField.EndsWith("-"))
                        {
                            gsTextField = gsTextField.Substring(0, gsTextField.Length - 1).Trim();
                        }

                        string nextLine = (i + 1 < lines.Length) ? lines[i + 1] : null;
                        if (nextLine != null)
                        {
                            gsDeviceText = Regex.Replace(nextLine, @"\s+", " ").Substring(14).Trim();
                            gsDeviceText = gsDeviceText.Replace("|", "");
                        }
                    }
                    else if (lineLower.Contains("fire pre alarm"))
                    {
                        tIpType = 2;
                        gsTextField = line.Substring(19);
                        gsTextField = gsTextField.Replace("-", " - ");
                        gsTextField = Regex.Replace(gsTextField, @"\s+", " ").Trim();
                        gsTextField = gsTextField.Replace("|", "");
                        gsTextField = gsTextField.Substring(16).Trim();
                        if (gsTextField.EndsWith("-"))
                        {
                            gsTextField = gsTextField.Substring(0, gsTextField.Length - 1).Trim();
                        }
                    }
                    else if (lineLower.Contains("fire alarm"))
                    {
                        tIpType = 0;
                        gsTextField = line.Substring(19);
                        gsTextField = gsTextField.Replace("-", " - ");
                        gsTextField = Regex.Replace(gsTextField, @"\s+", " ").Trim();
                        gsTextField = gsTextField.Replace("|", "");
                        gsTextField = gsTextField.Substring(12).Trim();
                        if (gsTextField.ToLower().EndsWith("input"))
                        {
                            gsTextField = gsTextField.Substring(0, gsTextField.Length - 5).Trim();
                        }
                        if (gsTextField.EndsWith("-"))
                        {
                            gsTextField = gsTextField.Substring(0, gsTextField.Length - 1).Trim();
                        }

                        string nextLine = (i + 1 < lines.Length) ? lines[i + 1] : null;
                        if (nextLine != null)
                        {
                            gsDeviceText = Regex.Replace(nextLine, @"\s+", " ").Substring(14).Trim();
                            gsDeviceText = gsDeviceText.Replace("|", "");
                        }
                    }

                    if (gsTextField.Length > 0)
                    {
                        try
                        {
                            enmNotAlarmType enumValue = (enmNotAlarmType)Enum.Parse(typeof(enmNotAlarmType), tIpType.ToString());
                            p1 = (int)(enumValue);
                        }
                        catch (Exception ex)
                        {
                            this.NotifyClient("gAlarmType " + gAlarmType + " " + ex.Message, false);
                        }
                        // Strip last word from gsTextField to get device text
                        string devicetext = gsTextField.Replace("-", "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";

                        var addr = AssignOrLookup(devicetext, null, null);
                        giNodeNumber = addr.Node;
                        giLoopNumber = addr.Loop;
                        giDeviceAddress = addr.Device;

                        evnum = CSAMXSingleton.CS.MakeInputNumber(giNodeNumber, giLoopNumber, giDeviceAddress, p1, on);

                        base.NotifyClient("Send to AMX: Node = " + (giNodeNumber + this.Offset) + " Loop = " + giLoopNumber + " Address = " + giDeviceAddress);

                        send_response_amx_and_serial(evnum, gsTextField, "", gsDeviceText);
                    }
                }
            }
            return true;
        }


        private void send_response_amx_and_serial(int evnum, string message1, string message2, string message3 = "")
        {
            string friendlymessage = message2 + (message3.Length > 0 ? (" " + message3) : "");

            // Signal the event back to the main service, so that it can be logged
            this.NotifyClient(friendlymessage, false);

            CSAMXSingleton.CS.SendAlarmToAMX(evnum, message1, message2, message3);
            CSAMXSingleton.CS.FlushMessages();
        }

        protected override void heartbeat_timer_callback(object sender)
        {
            //base.heartbeat_timer_callback(sender);

            // send_message(ActionType.KHandShake, NwmData.AlarmToAmx, "0,0,0,0");
        }

        public override void StartUp(int fakemode)
        {
            int setttingbaudrate = base.GetSetting<int>(ksettingsyncrosection, "BaudRate");
            string settingparity = base.GetSetting<string>(ksettingsyncrosection, "Parity");
            int settingdatabits = base.GetSetting<int>(ksettingsyncrosection, "DataBits");
            int settingstopbits = base.GetSetting<int>(ksettingsyncrosection, "StopBits");

            if (fakemode > 0)
            {
                return;
            }

            // we are a real serial port 
            serialport = new SerialPort(this.Identifier);
            serialport.BaudRate = setttingbaudrate;

            Parity parity = Parity.None;
            string friendlyparity = settingparity.Substring(0, 1).ToUpper();
            if (friendlyparity == "E")
                parity = Parity.Even;
            if (friendlyparity == "O")
                parity = Parity.Odd;

            serialport.Parity = parity;

            serialport.DataBits = settingdatabits;
            serialport.StopBits = (StopBits)settingstopbits;
            serialport.Handshake = Handshake.None;
            serialport.DataReceived += SerialPort_Datareceived;
            if (serialport.IsOpen)
            {
                serialport.Close();
            }
            base.NotifyClient("Attempting Open " + serialport.PortName, false);
            serialport.Encoding = System.Text.Encoding.ASCII;
            serialport.DtrEnable = true;

            serialport.ReadBufferSize = 8000;

            serialport.ReadTimeout = 500;
            serialport.ParityReplace = (byte)0;
            serialport.ReceivedBytesThreshold = 8;
            try
            {
                serialport.Open();
            }
            catch (Exception e)
            {
                base.NotifyClient("Failed To Open " + serialport.PortName + " " + e.ToString(), false);
            }

            if (serialport.IsOpen)
            {
                serialport.DiscardInBuffer();
                serialport.DiscardOutBuffer();
            }

            _framer = new EspaFramer(bytes =>
            {
                if (serialport != null && serialport.IsOpen)
                    serialport.Write(bytes, 0, bytes.Length);
            });
            _framer.FrameReceived += OnEspaFrame;
            _framer.Log += msg => base.NotifyClient(msg, false);
        }
        public override void Evacuate(string passedvalues)
        {
        }
        public override void Alert(string passedvalues)
        {
        }
        public override void EvacuateNetwork(string passedvalues)
        {
        }
        public override void Silence(string passedvalues)
        {
        }
        public override void MuteBuzzers(string passedvalues)
        {
        }
        public override void Reset(string passedvalues)
        {
        }
        public override void DisableDevice(string passedvalues)
        {
        }
        public override void EnableDevice(string passedvalues)
        {
        }
        public override void DisableZone(string passedvalues)
        {
        }
        public override void EnableZone(string passedvalues)
        {
        }
        public override void Analogue(string passedvalues)
        {
        }

        /// <summary>
        /// Called by the framer when a clean ESPA 4.4.4 frame has been received
        /// (BCC validated, ACK already sent). Classifies the display text into
        /// an event category, extracts zone and device address where the panel
        /// supplies them, and emits one AMX event.
        /// </summary>
        private void OnEspaFrame(EspaRecord rec)
        {
            string text = rec?.DisplayText ?? "";
            if (string.IsNullOrWhiteSpace(text)) return;

            string lower = Regex.Replace(text.ToLowerInvariant(), @"\s+", " ").Trim();

            int p1 = (int)enmNotAlarmType.NOTStatusEvent;
            bool on = true;
            string category = "ESPA Event";

            // Order matters: more specific phrases (cleared / fault / pre-alarm)
            // before the generic "fire alarm" / "alarm" catch-alls.
            if (lower.Contains("disablement cleared") || lower.Contains("isolation cleared") ||
                lower.Contains("disablement reset"))
            {
                p1 = (int)enmNotAlarmType.NOTIsolate; on = false; category = "Disablement Cleared";
            }
            else if (lower.Contains("disablement") || lower.Contains("isolation") ||
                     lower.Contains("disabled") || lower.Contains("isolated"))
            {
                p1 = (int)enmNotAlarmType.NOTIsolate; category = "Disablement";
            }
            else if (lower.Contains("fault cleared") || lower.Contains("fault reset") ||
                     lower.Contains("fault restored"))
            {
                p1 = (int)enmNotAlarmType.NOTFault; on = false; category = "Fault Cleared";
            }
            else if (lower.Contains("fire alarm fault") || lower.Contains("fault"))
            {
                p1 = (int)enmNotAlarmType.NOTFault; category = "Fault";
            }
            else if (lower.Contains("fire pre alarm") || lower.Contains("pre-alarm") ||
                     lower.Contains("prealarm"))
            {
                p1 = (int)enmNotAlarmType.NOTPreAlarm; category = "Pre-Alarm";
            }
            else if (lower.Contains("test mode") || lower.Contains("walk test"))
            {
                p1 = (int)enmNotAlarmType.NOTTestModeFire; category = "Test Mode";
            }
            else if (lower.Contains("fire alarm cleared") || lower.Contains("fire reset") ||
                     lower.Contains("alarm cleared"))
            {
                p1 = (int)enmNotAlarmType.NOTFire; on = false; category = "Fire Reset";
            }
            else if (lower.Contains("fire alarm") || lower.Contains("alarm"))
            {
                p1 = (int)enmNotAlarmType.NOTFire; category = "Fire";
            }

            // Examples of useful tokens in the panel's display text:
            //   "Fire Alarm -ZONE 1 -MAINBUILDING    A1003 -INPUTALARM" → zone 1, addr 1003
            //   "Fire Alarm  Fault -ZONE 1 -MAIN BUILDING"              → zone 1, no addr
            //   "Fire Alarm  Fault -     AutroMaster Switchboard ..."   → no zone, system event
            int? zoneHint = null, devHint = null;
            var zm = Regex.Match(text, @"\bZONE\s+(\d+)\b", RegexOptions.IgnoreCase);
            if (zm.Success && int.TryParse(zm.Groups[1].Value, out int zone)) zoneHint = zone;

            var dm = Regex.Match(text, @"\b[A-Z](\d{3,4})\b");
            if (dm.Success && int.TryParse(dm.Groups[1].Value, out int dev)) devHint = dev;

            string devicetext = text.Replace("-", "").Trim()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault() ?? "";

            var addr = AssignOrLookup(devicetext, zoneHint, devHint);

            int evnum = CSAMXSingleton.CS.MakeInputNumber(addr.Node, addr.Loop, addr.Device, p1, on);

            base.NotifyClient(
                "ESPA " + category +
                " (zone=" + (zoneHint?.ToString() ?? "-") +
                " dev=" + (devHint?.ToString() ?? "-") +
                "): Send to AMX Node=" + (addr.Node + this.Offset) +
                " Loop=" + addr.Loop + " Address=" + addr.Device);

            send_response_amx_and_serial(evnum, text.Trim(), category, devicetext);
        }

        /// <summary>
        /// Resolve a (node, loop, device) triple for an event. If the panel
        /// supplied explicit zone/address hints (parsed from the display text)
        /// those are used directly and not cached — the panel is the source of
        /// truth. Otherwise fall back to the legacy auto-assigner keyed on
        /// devicetext, which writes new entries to the EF Core events store.
        /// </summary>
        private (int Node, int Loop, int Device) AssignOrLookup(
            string devicetext, int? hintLoop, int? hintDevice)
        {
            if (hintLoop.HasValue && hintDevice.HasValue)
                return (1, hintLoop.Value, hintDevice.Value);

            lock (_eventsDbLock)
            {
                if (!string.IsNullOrEmpty(devicetext) &&
                    _eventCache.TryGetValue(devicetext, out var cached))
                {
                    return cached;
                }

                var next = _nextAssignment;
                next.Device++;
                if (next.Device > 254)
                {
                    next.Device = 1;
                    next.Loop++;
                    if (next.Loop > 254)
                    {
                        next.Loop = 1;
                        next.Node++;
                        if (next.Node > 254)
                            throw new Exception("Maximum node/loop/device limit reached");
                    }
                }

                if (!string.IsNullOrEmpty(devicetext))
                {
                    var entity = new EspaEvent
                    {
                        Node = next.Node,
                        Loop = next.Loop,
                        Device = next.Device,
                        Name = devicetext
                    };
                    _eventsDb.Events.Add(entity);
                    try
                    {
                        _eventsDb.SaveChanges();
                    }
                    catch (Exception ex)
                    {
                        // If the insert fails (e.g. unique-index collision on Name
                        // because a stale row exists from before the cache was
                        // populated), don't strand _nextAssignment on the failed
                        // slot — the next event with the same name would retry the
                        // same Node/Loop/Device and throw on a loop. Detach the
                        // tracked entity, look up the existing row, cache it and
                        // return that.
                        _eventsDb.Entry(entity).State = EntityState.Detached;
                        var existing = _eventsDb.Events.AsNoTracking()
                            .FirstOrDefault(e => e.Name == devicetext);
                        if (existing != null)
                        {
                            var found = (existing.Node, existing.Loop, existing.Device);
                            _eventCache[devicetext] = found;
                            this.NotifyClient(
                                "ESPA Events DB: SaveChanges failed for '" + devicetext +
                                "'; reused existing (" + found.Node + "," + found.Loop + "," + found.Device + ")");
                            return found;
                        }
                        this.NotifyClient(
                            "ESPA Events DB: SaveChanges failed for '" + devicetext + "': " + ex.Message);
                        throw;
                    }
                    _eventCache[devicetext] = next;
                }
                _nextAssignment = next;
                return next;
            }
        }

        private readonly List<byte> _buffer = new List<byte>();
        private readonly byte[] _terminator = { 0x0D, 0x0A, 0x0D, 0x0A }; // \r\n\r\n

        public override void SerialPort_Datareceived(object sender, SerialDataReceivedEventArgs e)
        {
            int bytesToRead = serialport.BytesToRead;
            if (bytesToRead <= 0) return;

            byte[] incoming = new byte[bytesToRead];
            int read = serialport.Read(incoming, 0, bytesToRead);
            if (read <= 0) return;

            // Real ESPA 4.4.4 traffic from an Autronica panel speaks the byte
            // protocol (ENQ/SOH/STX/ETX/EOT/BCC). Hand bytes to the framer first;
            // it consumes everything from ENQ through EOT and replies with
            // ACK/NAK on the same serial port. Anything the framer doesn't
            // recognise (e.g. log lines from the upstream helper program used
            // in dev/test) falls through to the legacy \r\n\r\n scrape path.
            int consumed = 0;
            if (_framer != null)
                consumed = _framer.Feed(incoming);

            if (consumed >= read) return;

            byte[] tail = consumed == 0
                ? incoming
                : incoming.Skip(consumed).ToArray();

            lock (_buffer)
            {
                _buffer.AddRange(tail);
                ExtractMessages();
            }
        }

        private void ExtractMessages()
        {
            while (true)
            {
                int pos = FindPattern(_buffer, _terminator);
                if (pos == -1)
                {
                    // No complete \r\n\r\n frame yet. The legacy code had an
                    // analog-value branch here that read fixed offsets from
                    // _buffer when no terminator was found, then fell through
                    // and tried to take pos + _terminator.Length (= 3) bytes
                    // off the buffer to Parse — that path was broken. Drop it
                    // until we have a real spec for the analog reply format.
                    return;
                }

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
                {
                    if (buffer[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }
    }
}