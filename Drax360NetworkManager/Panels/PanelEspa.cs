
using Microsoft.Data.Sqlite;
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

        // SQLite connection is opened once at construction and reused for the
        // lifetime of the panel. Previously every parsed event opened, ran
        // CREATE TABLE, and closed a fresh connection inside the per-line loop —
        // that was both expensive and used a relative path ("events.db") which,
        // for a service running as LocalService, resolved under C:\Windows\System32.
        private SqliteConnection _eventsDb;
        private readonly object _eventsDbLock = new object();

        // Bumped whenever the [Events] schema changes; EnsureSchema migrates
        // existing databases up to this version on startup.
        private const int kEventsSchemaVersion = 1;

        private const string kEventsCreateDdl =
            "CREATE TABLE [Events] (" +
            "    [Id]     INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "    [Node]   INTEGER NOT NULL, " +
            "    [Loop]   INTEGER NOT NULL, " +
            "    [Device] INTEGER NOT NULL, " +
            "    [Name]   TEXT    NOT NULL UNIQUE" +
            ");";

        // In-memory cache of devicetext -> (node, loop, device). Pre-populated
        // from the Events table at startup, then updated on each new INSERT.
        // Repeat events hit only the cache; first-sight events do a single INSERT.
        private readonly Dictionary<string, (int Node, int Loop, int Device)> _eventCache
            = new Dictionary<string, (int, int, int)>(StringComparer.Ordinal);
        private (int Node, int Loop, int Device) _nextAssignment = (1, 1, 0);

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
                _eventsDb = new SqliteConnection("Data Source=" + dbPath);
                _eventsDb.Open();

                EnsureSchema();

                using (var loadCmd = _eventsDb.CreateCommand())
                {
                    loadCmd.CommandText =
                        "SELECT [Name], [Node], [Loop], [Device] " +
                        "FROM [Events] " +
                        "ORDER BY [Id];";
                    using var reader = loadCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        string name = reader.GetString(0);
                        int node = reader.GetInt32(1);
                        int loop = reader.GetInt32(2);
                        int device = reader.GetInt32(3);
                        _eventCache[name] = (node, loop, device);
                        _nextAssignment = (node, loop, device);  // last row by id is the highest assignment
                    }
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
                        giNodeNumber = 0;
                        giLoopNumber = 0;
                        giDeviceAddress = 0;

                        // Strip last word from gsTextField to get device text
                        string devicetext = gsTextField.Replace("-", "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";

                        lock (_eventsDbLock)
                        {
                            if (_eventCache.TryGetValue(devicetext, out var cached))
                            {
                                giNodeNumber = cached.Node;
                                giLoopNumber = cached.Loop;
                                giDeviceAddress = cached.Device;
                            }
                            else
                            {
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
                                        {
                                            throw new Exception("Maximum node/loop/device limit reached");
                                        }
                                    }
                                }

                                giNodeNumber = next.Node;
                                giLoopNumber = next.Loop;
                                giDeviceAddress = next.Device;
                                CreateEventId(_eventsDb, giNodeNumber, giLoopNumber, giDeviceAddress, devicetext);
                                _eventCache[devicetext] = next;
                                _nextAssignment = next;
                            }
                        }

                        evnum = CSAMXSingleton.CS.MakeInputNumber(giNodeNumber, giLoopNumber, giDeviceAddress, p1, on);

                        base.NotifyClient("Send to AMX: Node = " + (giNodeNumber + this.Offset) + " Loop = " + giLoopNumber + " Address = " + giDeviceAddress);

                        send_response_amx_and_serial(evnum, gsTextField, "", gsDeviceText);
                    }
                }
            }
            return true;
        }


        // Bring the Events schema up to kEventsSchemaVersion. Runs at startup,
        // is idempotent, and migrates legacy databases (untyped node/loop/device
        // columns from the original DDL) up to the current INTEGER-typed schema.
        private void EnsureSchema()
        {
            int currentVersion = ExecScalarInt("PRAGMA user_version;");

            bool tableExists = ExecScalarInt(
                "SELECT count(*) FROM [sqlite_master] " +
                "WHERE [type] = 'table' AND [name] = 'Events';") > 0;

            this.NotifyClient(
                "ESPA Events DB: schema check (current v" + currentVersion +
                ", target v" + kEventsSchemaVersion +
                ", tableExists=" + tableExists + ")");

            if (!tableExists)
            {
                using var create = _eventsDb.CreateCommand();
                create.CommandText = kEventsCreateDdl;
                create.ExecuteNonQuery();
                this.NotifyClient("ESPA Events DB: created fresh schema v" + kEventsSchemaVersion);
            }
            else if (currentVersion < kEventsSchemaVersion)
            {
                this.NotifyClient(
                    "ESPA Events DB: migrating v" + currentVersion +
                    " -> v" + kEventsSchemaVersion);

                int legacyCount = ExecScalarInt("SELECT count(*) FROM [Events];");
                int nullNameCount = ExecScalarInt(
                    "SELECT count(*) FROM [Events] WHERE [Name] IS NULL;");

                // Legacy schema had untyped node/loop/device columns storing
                // values as TEXT. Rebuild as INTEGER NOT NULL via rename + copy
                // with CAST. Rows missing a Name (would violate NOT NULL) are
                // dropped — the old schema permitted them in theory.
                using (var tx = _eventsDb.BeginTransaction())
                {
                    using (var rename = _eventsDb.CreateCommand())
                    {
                        rename.Transaction = tx;
                        rename.CommandText = "ALTER TABLE [Events] RENAME TO [Events_legacy];";
                        rename.ExecuteNonQuery();
                    }
                    using (var create = _eventsDb.CreateCommand())
                    {
                        create.Transaction = tx;
                        create.CommandText = kEventsCreateDdl;
                        create.ExecuteNonQuery();
                    }
                    using (var copy = _eventsDb.CreateCommand())
                    {
                        copy.Transaction = tx;
                        copy.CommandText =
                            "INSERT INTO [Events] ([Id], [Node], [Loop], [Device], [Name]) " +
                            "SELECT [Id], " +
                            "       CAST([Node]   AS INTEGER), " +
                            "       CAST([Loop]   AS INTEGER), " +
                            "       CAST([Device] AS INTEGER), " +
                            "       [Name] " +
                            "FROM [Events_legacy] " +
                            "WHERE [Name] IS NOT NULL;";
                        copy.ExecuteNonQuery();
                    }
                    using (var drop = _eventsDb.CreateCommand())
                    {
                        drop.Transaction = tx;
                        drop.CommandText = "DROP TABLE [Events_legacy];";
                        drop.ExecuteNonQuery();
                    }

                    tx.Commit();
                }

                int kept = ExecScalarInt("SELECT count(*) FROM [Events];");
                this.NotifyClient(
                    "ESPA Events DB: migration complete (" +
                    kept + " kept, " + nullNameCount + " dropped, " +
                    (legacyCount - kept - nullNameCount) + " other-skipped)");
            }
            else
            {
                this.NotifyClient("ESPA Events DB: already at v" + currentVersion + ", no migration");
            }

            using (var setVer = _eventsDb.CreateCommand())
            {
                // PRAGMA does not accept parameters, but kEventsSchemaVersion is
                // a compile-time constant so there is no injection surface here.
                setVer.CommandText = "PRAGMA user_version = " + kEventsSchemaVersion + ";";
                setVer.ExecuteNonQuery();
            }
        }

        private int ExecScalarInt(string sql)
        {
            using var cmd = _eventsDb.CreateCommand();
            cmd.CommandText = sql;
            object result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
        }

        public bool CreateEventId(SqliteConnection conn, int node, int loop, int device, string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText =
                "INSERT OR IGNORE INTO [Events] ([Node], [Loop], [Device], [Name]) " +
                "VALUES ($node, $loop, $device, $name);";
            insertCmd.Parameters.Add("$node",   SqliteType.Integer).Value = node;
            insertCmd.Parameters.Add("$loop",   SqliteType.Integer).Value = loop;
            insertCmd.Parameters.Add("$device", SqliteType.Integer).Value = device;
            insertCmd.Parameters.Add("$name",   SqliteType.Text).Value    = name;
            insertCmd.ExecuteNonQuery();
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

        private readonly List<byte> _buffer = new List<byte>();
        private readonly byte[] _terminator = { 0x0D, 0x0A, 0x0D, 0x0A }; // \r\n\r\n

        public override void SerialPort_Datareceived(object sender, SerialDataReceivedEventArgs e)
        {
            Thread.Sleep(500); // wait for more data
            int bytesToRead = serialport.BytesToRead;
            if (bytesToRead <= 0) return;

            byte[] incoming = new byte[bytesToRead];
            int read = serialport.Read(incoming, 0, bytesToRead);
            if (read <= 0) return;

            lock (_buffer)
            {
                _buffer.AddRange(incoming);
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
                    // Now deal with specific message types
                    if (_buffer.Count >= 4 && _buffer[3].ToString() == "68")
                    {
                        int DeviceAnalogueValue = _buffer[7];
                        int deviceNode = _buffer[2];
                        int DeviceLoop = giAnalogRequestLoop + 1;
                        base.NotifyClient("Analogue Node Received: " + deviceNode, false);
                        base.NotifyClient("Analogue Address Received: " + _buffer[6], false);
                        base.NotifyClient("Analogue Value Received: " + DeviceAnalogueValue, false);
                        //string sLavFileName = GetAnalogStoreName(deviceNode, DeviceLoop);
                    }
                    else
                    {
                        if (_buffer.Count >= 5 && _buffer[4].ToString() == "68")
                        {
                            int DeviceAnalogueValue = _buffer[8];
                            int deviceNode = _buffer[2];
                            int DeviceLoop = giAnalogRequestLoop + 1;
                            base.NotifyClient("Analogue Node Received: " + _buffer[3], false);
                            base.NotifyClient("Analogue Address Received: " + _buffer[7], false);
                            base.NotifyClient("Analogue Value Received: " + DeviceAnalogueValue, false);
                            //string sLavFileName = GetAnalogStoreName(deviceNode, DeviceLoop);
                        }
                        else
                        {
                            return;  // no complete message yet
                        }
                    }
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