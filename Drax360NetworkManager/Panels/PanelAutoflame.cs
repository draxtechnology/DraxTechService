#region usings
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentModbus;
#endregion

namespace DraxTechnology.Panels
{
    // Autoflame Mk8 DTI (Data Transfer Interface) boiler-house controller,
    // polled over Modbus — read-only. First site: Mount Vernon Hospital
    // boiler alarms (Chris / Mike, 2026-11-19). The register map is the
    // "Mk8 D.T.I. Set-Up Guide" (04.10.2018) section 9; the code tables are
    // in AutoflameCodes.
    //
    // Unlike every other driver the panel never speaks first: we are the
    // Modbus client, read the DTI's fault registers each scan and turn
    // changes into AMX events. Those go through CSAMX like any panel's, so
    // the MQTT mirror (MqttEnabled) publishes them with no extra code.
    //
    // Several DTIs (one per boiler house) run as [ConnectionN] sections in
    // AflMan.ini, one driver instance each, like Taktis multi-IP (Chris,
    // 2026-11-23). Without them the single DTI is configured in [SetUp].
    //
    // AMX addressing, one node per DTI:
    //   Node   = 1 + giAmx1Offset (single), PanelNumber + Offset (connection)
    //   Loop 0 = the DTI itself: device 1 error, device 2 warning
    //   Loop n = burner (MM) n, 1-10:
    //            device 1 lockout, 2 alarm, 3 warning, 4 MM error, 5 offline
    // A code change on a point clears the old text on AMX and raises the
    // new one; a code going to 0 clears it.
    //
    // Transport (AflMan.ini [SetUp] Transport): TCP (Ethernet, port 502) or
    // RTU (RS422/485 serial — the DTI's other Modbus port). Nothing is ever
    // written to the DTI, so setting the DTI's own option 20 to 1
    // (read-only) is safe and recommended.
    internal class PanelAutoflame : AbstractPanel
    {
        #region Register map (guide section 9.1; documented 3x/1x numbers minus 30001/10001)
        // DTI: 30001-30006 error status, error code, error device ID,
        // warning status, warning code, warning device ID.
        private const ushort kDtiBlock = 0;
        private const ushort kDtiBlockLen = 6;

        // MM error number 30113, +50 per MM.
        private const int kMmErrorBase = 112;
        // Lockout code 30830, +50 per MM. The guide prints MM 6 as 30180,
        // which is MM 2's "Min Required Setpoint" — a typo in the table;
        // every other MM follows the 50-step pattern, so MM 6 is 31080.
        private const int kLockoutBase = 829;
        private const int kMmStride = 50;
        // 32009 alarm status, 32010 warning status, 32011 (unused),
        // 32012 alarm code, 32013 warning code; +100 per MM.
        private const int kWlBlockBase = 2008;
        private const int kWlStride = 100;
        private const ushort kWlBlockLen = 5;
        // 1x 11793-11802: MM online (1) / offline (0), one bit per MM.
        private const int kOnlineBase = 1792;

        private const int kMaxMm = 10;
        #endregion

        #region AMX point numbers
        private const int kLoopDti = 0;
        private const int kDevDtiError = 1;
        private const int kDevDtiWarning = 2;

        private const int kDevLockout = 1;
        private const int kDevAlarm = 2;
        private const int kDevWarning = 3;
        private const int kDevMmError = 4;
        private const int kDevOffline = 5;
        #endregion

        #region Settings
        private string transport = "TCP";
        private string ipAddress = "";
        private int ipPort = 502;
        private string commPort = "COM1";
        private int baudRate = 9600;
        private string parity = "Even";
        private byte unitId = 1;
        private int scanMs = 2000;
        // Burners to watch. Empty = learn: any MM seen online is watched
        // from then on, so an unfitted MM slot never raises "offline".
        private readonly HashSet<int> burners = new HashSet<int>();
        private bool learnBurners;
        private readonly Dictionary<int, string> burnerNames = new Dictionary<int, string>();
        // AMX input type per class. Default fault (8) for everything: these
        // are plant alarms, never fire. Lockout/alarm are separate keys so a
        // site that wants a boiler lockout to stand out can raise it as a
        // non-fire alarm (1) instead.
        private int lockoutInputType = (int)enmNotAlarmType.NOTFault;
        private int alarmInputType = (int)enmNotAlarmType.NOTFault;
        private const int kFaultInputType = (int)enmNotAlarmType.NOTFault;
        // This DTI's AMX node: 1 + giAmx1Offset for the single [SetUp] DTI,
        // PanelNumber + Offset for a [ConnectionN] one.
        private int node;
        private string label = "Autoflame DTI";
        #endregion

        #region State
        private IDtiRegisters link;
        private CancellationTokenSource cts;
        private Task pollTask;
        private int fakemodeFlag;
        // Current code per (loop, device), 0 = clear. Kept across a comms
        // break so a fault that is still standing when the link returns is
        // not raised twice.
        private readonly Dictionary<(int Loop, int Device), (int Code, string Text1, string Text2)> active
            = new Dictionary<(int, int), (int, string, string)>();
        private string lastPollError = null;
        #endregion

        public override string FakeString => "";
        public override string PanelVersion => "1.0.0.0";

        // Polls every couple of seconds, so it can fault faster than the
        // 60s serial heartbeat: three missed 10s ticks = comms failure.
        protected override int HeartbeatIntervalSeconds => 10;
        // Each DTI faults on its own node, not on panel 1.
        protected override int CommsFaultNode => node;

        #region Constructor
        public PanelAutoflame(string baselogfolder, string identifier)
            : this(baselogfolder, identifier, null) { }

        public PanelAutoflame(string baselogfolder, string identifier, AutoflameConnectionSettings conn)
            : base(baselogfolder, identifier, "AflMan", "AUTOFLAME")
        {
            if (string.IsNullOrEmpty(identifier)) return;

            this.Offset = base.GetSetting<int>(ksettingsetupsection, "giAmx1Offset");
            node = 1 + this.Offset;

            transport = Setting("Transport", transport).ToUpperInvariant();
            ipAddress = Setting("IPAddress", ipAddress);
            ipPort = SettingInt("IPPort", ipPort);
            commPort = Setting("CommPort", commPort);
            baudRate = SettingInt("BaudRate", baudRate);
            parity = Setting("Parity", parity);
            unitId = (byte)Math.Clamp(SettingInt("ModbusID", unitId), 1, 254);
            scanMs = Math.Max(500, SettingInt("ScanMs", scanMs));
            lockoutInputType = SettingInt("LockoutInputType", lockoutInputType);
            alarmInputType = SettingInt("AlarmInputType", alarmInputType);

            foreach (int mm in ParseBurnerList(Setting("Burners", "")))
                burners.Add(mm);
            learnBurners = burners.Count == 0;

            // A connection overrides the shared [SetUp] values; scan rate,
            // input types and burner learning stay shared.
            string namesSection = "Names";
            if (conn != null)
            {
                transport = "TCP";
                ipAddress = conn.IPAddress;
                if (conn.Port > 0) ipPort = conn.Port;
                if (conn.ModbusID > 0) unitId = (byte)Math.Clamp(conn.ModbusID, 1, 254);
                this.Offset = conn.Offset;
                node = conn.PanelNumber + conn.Offset;
                label = "Autoflame DTI " + conn.Index;
                namesSection = "Connection" + conn.Index;
                burners.Clear();
                foreach (int mm in ParseBurnerList(conn.Burners ?? "")) burners.Add(mm);
                learnBurners = burners.Count == 0;
            }

            for (int mm = 1; mm <= kMaxMm; mm++)
            {
                string name = base.GetSetting<string>(namesSection, "MM" + mm);
                burnerNames[mm] = string.IsNullOrWhiteSpace(name) ? "Boiler " + mm : name.Trim();
            }

            heartbeat_timer = new Timer(heartbeat_timer_callback, this.Identifier,
                HeartbeatIntervalSeconds * 1000, HeartbeatIntervalSeconds * 1000);
        }

        private string Setting(string key, string fallback)
        {
            string v = base.GetSetting<string>(ksettingsetupsection, key);
            return string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
        }

        private int SettingInt(string key, int fallback)
        {
            return int.TryParse(base.GetSetting<string>(ksettingsetupsection, key), out int v) ? v : fallback;
        }

        // "1,2,4" or "1-4" or a mix; anything outside 1-10 is ignored.
        private static IEnumerable<int> ParseBurnerList(string text)
        {
            foreach (string part in text.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] range = part.Split('-');
                if (range.Length == 2 && int.TryParse(range[0], out int a) && int.TryParse(range[1], out int b))
                {
                    for (int i = Math.Max(1, a); i <= Math.Min(kMaxMm, b); i++) yield return i;
                }
                else if (int.TryParse(part, out int one) && one >= 1 && one <= kMaxMm)
                {
                    yield return one;
                }
            }
        }
        #endregion

        #region StartUp / Shutdown
        public override void StartUp(int fakemode)
        {
            fakemodeFlag = fakemode;
            if (fakemode > 0)
            {
                link = new FakeDtiRegisters();
                base.NotifyClient(label + " running in FAKE mode — scripted lockouts and alarms on boilers 1 and 2.", false);
            }
            else if (transport == "RTU")
            {
                link = new RtuDtiRegisters(commPort, baudRate, parity);
                base.NotifyClient(label + ": Modbus RTU on " + commPort + " @ " + baudRate + " " + parity +
                                  ", Modbus ID " + unitId, false);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(ipAddress))
                {
                    base.NotifyClient(label + ": no IPAddress in AflMan.ini — not polling.", false);
                    return;
                }
                link = new TcpDtiRegisters(ipAddress, ipPort);
                base.NotifyClient(label + ": Modbus TCP " + ipAddress + ":" + ipPort + ", Modbus ID " + unitId +
                                  ", AMX node " + node, false);
            }

            base.NotifyClient(label + ": watching " +
                (learnBurners ? "any boiler seen online" : "boilers " + string.Join(",", burners.OrderBy(b => b))) +
                ", scan every " + scanMs + " ms", false);

            cts = new CancellationTokenSource();
            pollTask = Task.Run(() => PollLoop(cts.Token));
        }

        public override void Shutdown()
        {
            try { cts?.Cancel(); } catch { }
            try { pollTask?.Wait(3000); } catch { }
            try { link?.Dispose(); } catch { }
            link = null;
            base.Shutdown();
        }

        public override bool LinkIsOpen()
        {
            return link != null && link.IsConnected;
        }
        #endregion

        #region Poll loop
        private async Task PollLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!link.IsConnected) link.Connect();
                    Scan();

                    lastDataReceived = DateTime.Now;
                    CountHeartbeat();
                    NoteCommsRestored();
                    if (lastPollError != null)
                    {
                        base.NotifyClient(label + ": polling again", false);
                        lastPollError = null;
                    }
                }
                catch (Exception ex)
                {
                    // Logged once per distinct error, not every scan. The
                    // comms monitor raises the AMX fault if it persists.
                    if (ex.Message != lastPollError)
                    {
                        base.NotifyClient(label + " poll failed: " + ex.Message, false);
                        lastPollError = ex.Message;
                    }
                    try { link.Disconnect(); } catch { }
                }

                try { await Task.Delay(scanMs, token); }
                catch (TaskCanceledException) { break; }
            }
        }

        private void Scan()
        {
            ushort[] dti = link.ReadInputRegisters(unitId, kDtiBlock, kDtiBlockLen);
            Update(kLoopDti, kDevDtiError, dti[0] != 0 ? Math.Max((int)dti[1], 1) : 0, kFaultInputType,
                   code => ("DTI Error", AutoflameCodes.DtiFault(code, dti[2])));
            Update(kLoopDti, kDevDtiWarning, dti[3] != 0 ? Math.Max((int)dti[4], 1) : 0, kFaultInputType,
                   code => ("DTI Warning", AutoflameCodes.DtiFault(code, dti[5])));

            bool[] online = link.ReadDiscreteInputs(unitId, kOnlineBase, kMaxMm);
            for (int mm = 1; mm <= kMaxMm; mm++)
            {
                if (learnBurners && online[mm - 1] && burners.Add(mm))
                    base.NotifyClient(label + ": " + burnerNames[mm] + " (MM " + mm + ") online — now watched", false);
            }

            foreach (int mm in burners.OrderBy(b => b))
            {
                string name = burnerNames[mm];
                int k = mm - 1;

                Update(mm, kDevOffline, online[k] ? 0 : 1, kFaultInputType,
                       _ => (name, "Burner Offline"));

                int lockout = link.ReadInputRegisters(unitId, (ushort)(kLockoutBase + kMmStride * k), 1)[0];
                if (lockout == AutoflameCodes.LockoutCleared) lockout = 0;
                Update(mm, kDevLockout, lockout, lockoutInputType,
                       code => (name + " Lockout", AutoflameCodes.Describe(AutoflameCodes.Lockouts, code, "Lockout")));

                int mmError = link.ReadInputRegisters(unitId, (ushort)(kMmErrorBase + kMmStride * k), 1)[0];
                Update(mm, kDevMmError, mmError, kFaultInputType,
                       code => (name + " MM Error", AutoflameCodes.Describe(AutoflameCodes.MmErrors, code, "Error")));

                ushort[] wl = link.ReadInputRegisters(unitId, (ushort)(kWlBlockBase + kWlStride * k), kWlBlockLen);
                int alarm = wl[0] != 0 ? Math.Max((int)wl[3], 1) : 0;
                int warning = wl[1] != 0 ? Math.Max((int)wl[4], 1) : 0;
                Update(mm, kDevAlarm, alarm, alarmInputType,
                       code => (name + " Alarm", AutoflameCodes.Describe(AutoflameCodes.Alarms, code, "Alarm")));
                Update(mm, kDevWarning, warning, kFaultInputType,
                       code => (name + " Warning", AutoflameCodes.Describe(AutoflameCodes.Alarms, code, "Warning")));
            }
        }
        #endregion

        #region State changes → AMX
        private void Update(int loop, int device, int code, int inputType, Func<int, (string Text1, string Text2)> describe)
        {
            active.TryGetValue((loop, device), out var was);
            if (was.Code == code) return;

            if (was.Code != 0)
            {
                int clr = CSAMXSingleton.CS.MakeInputNumber(node, loop, device, inputType, false);
                base.NotifyClient(label + " clear: " + was.Text1 + " - " + was.Text2 +
                                  "  (N" + node + " L" + loop + " D" + device + ")", false);
                CSAMXSingleton.CS.SendResetToAMX(clr, Clip(was.Text1), Clip(was.Text2), "");
            }

            if (code != 0)
            {
                var (text1, text2) = describe(code);
                int evnum = CSAMXSingleton.CS.MakeInputNumber(node, loop, device, inputType, true);
                base.NotifyClient(label + " raise: " + text1 + " - " + text2 + " (code " + code + ")" +
                                  "  (N" + node + " L" + loop + " D" + device + ")", false);
                CSAMXSingleton.CS.SendAlarmToAMX(evnum, Clip(text1), Clip(text2), "");
                active[(loop, device)] = (code, text1, text2);
                CountMessage();
            }
            else
            {
                active.Remove((loop, device));
            }

            CSAMXSingleton.CS.FlushMessages();
        }

        // AMX text lines are 40 characters.
        private static string Clip(string s) => s.Length <= 40 ? s : s.Substring(0, 40);
        #endregion

        #region Heartbeat
        protected override void heartbeat_timer_callback(object sender)
        {
            if (link != null) CheckCommsMonitor();
        }
        #endregion

        #region Abstract overrides — read-only, no controls
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

        #region Register sources
        // The poll loop reads through this so the fake-mode script and the two
        // Modbus transports share one decode path.
        private interface IDtiRegisters : IDisposable
        {
            bool IsConnected { get; }
            void Connect();
            void Disconnect();
            ushort[] ReadInputRegisters(byte unit, ushort start, ushort count);
            bool[] ReadDiscreteInputs(byte unit, int start, int count);
        }

        private abstract class ModbusDtiRegisters : IDtiRegisters
        {
            protected abstract ModbusClient Client { get; }
            public abstract bool IsConnected { get; }
            public abstract void Connect();
            public abstract void Disconnect();

            // The raw overload returns the big-endian wire bytes, so no
            // reliance on the library's endianness setting.
            public ushort[] ReadInputRegisters(byte unit, ushort start, ushort count)
            {
                Span<byte> raw = Client.ReadInputRegisters(unit, start, count);
                var values = new ushort[count];
                for (int i = 0; i < count; i++)
                    values[i] = (ushort)((raw[2 * i] << 8) | raw[2 * i + 1]);
                return values;
            }

            // Discrete inputs arrive packed, least significant bit first.
            public bool[] ReadDiscreteInputs(byte unit, int start, int count)
            {
                Span<byte> raw = Client.ReadDiscreteInputs(unit, start, count);
                var bits = new bool[count];
                for (int i = 0; i < count; i++)
                    bits[i] = (raw[i / 8] & (1 << (i % 8))) != 0;
                return bits;
            }

            public void Dispose()
            {
                try { Disconnect(); } catch { }
            }
        }

        private sealed class TcpDtiRegisters : ModbusDtiRegisters
        {
            private readonly string host;
            private readonly int port;
            private ModbusTcpClient client = new ModbusTcpClient();

            public TcpDtiRegisters(string host, int port) { this.host = host; this.port = port; }

            protected override ModbusClient Client => client;
            public override bool IsConnected => client.IsConnected;

            public override void Connect()
            {
                // The guide gives the DTI a 2000 ms slave response timeout.
                client = new ModbusTcpClient { ConnectTimeout = 3000, ReadTimeout = 2000, WriteTimeout = 2000 };
                client.Connect(host + ":" + port);
            }

            public override void Disconnect() => client.Disconnect();
        }

        private sealed class RtuDtiRegisters : ModbusDtiRegisters
        {
            private readonly string port;
            private readonly ModbusRtuClient client = new ModbusRtuClient();

            public RtuDtiRegisters(string port, int baud, string parity)
            {
                this.port = port;
                client.BaudRate = baud;
                // DTI option 24: 0 = even parity with 1 stop bit,
                // 1 = no parity with 2 stop bits. Data bits fixed at 8.
                bool none = parity.StartsWith("N", StringComparison.OrdinalIgnoreCase);
                client.Parity = none ? Parity.None : Parity.Even;
                client.StopBits = none ? StopBits.Two : StopBits.One;
                client.ReadTimeout = 2000;
                client.WriteTimeout = 2000;
            }

            protected override ModbusClient Client => client;
            public override bool IsConnected => client.IsConnected;
            public override void Connect() => client.Connect(port);
            public override void Disconnect() => client.Close();
        }

        // Fake mode: a 90-second loop on boilers 1 and 2 that walks through
        // every event class, so the AMX / MQTT path can be watched without a
        // DTI. Boiler 3 is never online, so it stays unwatched.
        private sealed class FakeDtiRegisters : IDtiRegisters
        {
            private readonly DateTime started = DateTime.Now;
            public bool IsConnected => true;
            public void Connect() { }
            public void Disconnect() { }
            public void Dispose() { }

            private int T => (int)(DateTime.Now - started).TotalSeconds % 90;
            private bool Between(int from, int to) => T >= from && T < to;

            public ushort[] ReadInputRegisters(byte unit, ushort start, ushort count)
            {
                var v = new ushort[count];
                if (start == kDtiBlock && Between(50, 70))
                {
                    v[3] = 1; v[4] = 6; v[5] = 2;            // DTI warning: MM 2 comms failure
                }
                else if (start == kLockoutBase && Between(10, 40))
                {
                    v[0] = 13;                               // boiler 1: No Flame Signal
                }
                else if (start == kLockoutBase && Between(40, 45))
                {
                    v[0] = AutoflameCodes.LockoutCleared;    // boiler 1 reset
                }
                else if (start == kWlBlockBase + kWlStride && Between(20, 60))
                {
                    v[0] = 1; v[3] = (ushort)(T < 35 ? 152 : 153);   // boiler 2: Pre-1st Low, then 1st Low
                }
                else if (start == kWlBlockBase && Between(25, 55))
                {
                    v[1] = 1; v[4] = 3;                      // boiler 1 warning: O2 Upper Limit
                }
                return v;
            }

            public bool[] ReadDiscreteInputs(byte unit, int start, int count)
            {
                var bits = new bool[count];
                bits[0] = true;
                bits[1] = !Between(70, 80);                  // boiler 2 drops offline briefly
                return bits;
            }
        }
        #endregion
    }

    // One [ConnectionN] section of AflMan.ini: a DTI reached over Modbus TCP.
    internal sealed class AutoflameConnectionSettings
    {
        public int Index;            // N of [ConnectionN]
        public string IPAddress;
        public int Port;             // 0 = [SetUp] IPPort (default 502)
        public int ModbusID;         // 0 = [SetUp] ModbusID (default 1)
        public int PanelNumber;      // AMX panel number for this DTI
        public int Offset;           // resolved: entry Offset, else [SetUp] giAmx1Offset
        public string Burners;       // blank = learn any MM seen online
    }
}
