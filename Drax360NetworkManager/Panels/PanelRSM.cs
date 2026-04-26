
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Web.Script.Serialization;

namespace DraxTechnology.Panels
{
    internal class PanelRSM : AbstractPanel
    {
        #region constants
        const byte kheartbeatdelayseconds = 60;
        // Wire-format field separator. VB6: 'Global Const sepCHAR = 199' — that's
        // 'Ç' (0xC7). NOT ';'. Outgoing ACKs must use this byte or the panel rejects.
        const char kSeparator = 'Ç';
        #endregion

        #region message field offsets (mirror VB RSMenum.mField)
        const int F_MessageType = 0;
        const int F_MessageID = 1;
        const int F_ModuleNumber = 2;
        const int F_SerialNumber = 3;
        const int F_ModuleType = 4;
        const int F_NodeNum = 5;
        const int F_LoopNum = 6;
        const int F_Address = 7;
        const int F_SubAddress = 8;
        const int F_OnOff = 9;
        const int F_EventType = 10;
        const int F_DeviceType = 11;
        const int F_Extension = 12;
        const int F_Text = 13;
        const int F_Extension2 = 14;
        #endregion

        #region poll field offsets (mirror VB RSMenum.mPollField)
        const int P_ExpiryDateDays = 5;
        const int P_NumberOfPanels = 6;
        const int P_Options = 7;
        const int P_LicenseActivated = 8;
        #endregion

        #region SPX extension codes (mirror VB RSMenum.evext)
        const int SPX_RST1TO14 = 1;
        const int SPX_RST0TO14 = 2;
        const int SPX_RST0TO14NOT4 = 3;
        const int SPX_RST0TO15 = 4;
        #endregion

        #region per-module state (in-memory; no config)
        private class ModuleState
        {
            public int ModuleNumber;
            public string LastKnownIP = "";
            public string FriendlyName = "";
            public string ModuleType = "";
            public string SerialNumber = "";
            public DateTime LastRX = DateTime.MinValue;
            public long RXmessages;
            public readonly Dictionary<int, string> ZoneTexts = new Dictionary<int, string>();
            public long ExpiryDateDays;
            public int PanelsAllowed;
            public string ModuleOptions = "";

            // Set by Parse when bytes arrive on this module's TCP connection,
            // cleared by UnregisterStream when the connection closes. Used for
            // outbound commands (Evacuate/Silence/Reset/etc.).
            public NetworkStream Stream;
            public readonly object WriteLock = new object();
        }
        private readonly Dictionary<int, ModuleState> modules = new Dictionary<int, ModuleState>();
        private readonly object modulesLock = new object();

        // Outbound message ID counter. Wraps within int range; the panel only
        // requires uniqueness within the small in-flight window.
        private int nextMessageID = 1;
        private readonly object messageIDLock = new object();

        // Devices loaded from client's devices.json, keyed by IP address.
        // Mirrors the PanelEmail pattern of reading client config at startup.
        private readonly Dictionary<string, ClientDevice> devicesByIP =
            new Dictionary<string, ClientDevice>(StringComparer.OrdinalIgnoreCase);
        #endregion

        public override string FakeString
        {
            get { return ""; }
        }

        public PanelRSM(string baselogfolder, string identifier) : base(baselogfolder, identifier, "RSMMan", "RSM")
        {
            if (!String.IsNullOrEmpty(identifier))
            {
                heartbeat_timer = new System.Threading.Timer(heartbeat_timer_callback, this.Identifier, 500, kheartbeatdelayseconds * 1000);
                this.Offset = base.GetSetting<int>(ksettingsetupsection, "giAmx1Offset");
                LoadDevices();
            }
        }

        private void LoadDevices()
        {
            Paths.MigrateLegacyFile("devices.json");
            string jsonPath = Paths.GetFile("devices.json");

            if (!File.Exists(jsonPath))
            {
                base.NotifyClient($"PanelRSM: devices file not found ({jsonPath})", false);
                return;
            }

            try
            {
                string json = File.ReadAllText(jsonPath, Encoding.UTF8);
                var serializer = new JavaScriptSerializer();
                var raw = serializer.Deserialize<List<ClientDevice>>(json);
                if (raw == null) return;

                foreach (var d in raw)
                {
                    string ip = (d.IP ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(ip)) continue;
                    devicesByIP[ip] = d;
                }

                base.NotifyClient($"PanelRSM: loaded {devicesByIP.Count} device(s) from {jsonPath}", false);
            }
            catch (Exception ex)
            {
                base.NotifyClient($"PanelRSM: error loading devices — {ex.Message}", false);
            }
        }

        public byte[] Parse(byte[] buffer, string clientIP, NetworkStream stream, out int parsedModuleNumber)
        {
            parsedModuleNumber = 0;

            string hexData = BitConverter.ToString(buffer).Replace("-", " ");
            this.NotifyClient($"[{clientIP}] RX: {hexData}", false);

            string decoded = decodedata(buffer);
            this.NotifyClient($"[{clientIP}] DECODED: {decoded}", false);

            // strip trailing checksum-error annotation if present
            int chkIdx = decoded.IndexOf("[CHECKSUM ERROR");
            if (chkIdx >= 0)
            {
                decoded = decoded.Substring(0, chkIdx).Trim();
            }

            string[] parts = decoded.Split(kSeparator);
            if (parts.Length < 3)
            {
                return new byte[0];
            }

            string messageType = parts[F_MessageType];
            int messageID = ParseInt(GetField(parts, F_MessageID));
            int moduleNumber = ParseInt(GetField(parts, F_ModuleNumber));
            string moduleType = GetField(parts, F_ModuleType);
            string serialNumber = GetField(parts, F_SerialNumber);

            parsedModuleNumber = moduleNumber;
            ModuleState state = TouchModule(moduleNumber, clientIP, moduleType, serialNumber, stream);

            string ack;
            switch (messageType)
            {
                case "EVT":
                    HandleEVT(state, parts);
                    ack = $"ACK{kSeparator}{moduleNumber}{kSeparator}{messageID}";
                    break;

                case "POL":
                    HandlePOL(state, parts);
                    int licenseStatus = 0; // 0 = good
                    ack = $"PAK{kSeparator}{moduleNumber}{kSeparator}{messageID}{kSeparator}{licenseStatus}";
                    break;

                case "ZTX":
                    HandleZTX(state, parts);
                    ack = $"ACK{kSeparator}{moduleNumber}{kSeparator}{messageID}";
                    break;

                case "ANA":
                    HandleANA(state, parts);
                    ack = $"ACK{kSeparator}{moduleNumber}{kSeparator}{messageID}";
                    break;

                case "SPX":
                    HandleSPX(state, parts);
                    ack = $"ACK{kSeparator}{moduleNumber}{kSeparator}{messageID}";
                    break;

                case "CAK":
                case "SAK":
                case "GAK":
                case "ACK":
                case "NAK":
                    // these are responses to commands the manager sent — nothing to ack
                    return new byte[0];

                default:
                    this.NotifyClient($"Unknown message type '{messageType}' from module {moduleNumber}", false);
                    ack = $"ACK{kSeparator}{moduleNumber}{kSeparator}{messageID}";
                    break;
            }

            return scrambleandencodemessage(ack);
        }

        #region message handlers

        private void HandleEVT(ModuleState state, string[] parts)
        {
            int loopNum = ParseInt(GetField(parts, F_LoopNum));
            int address = ParseInt(GetField(parts, F_Address));
            int onOff = ParseInt(GetField(parts, F_OnOff));
            int inputType = ParseInt(GetField(parts, F_EventType));
            string deviceText = GetField(parts, F_Text).Trim();
            int extension = ParseInt(GetField(parts, F_Extension));
            int extension2 = ParseInt(GetField(parts, F_Extension2));

            // Zone derivation differs by module type (mirrors VB)
            int zone;
            if (state.ModuleType == "ZI" || state.ModuleType == "MZ")
                zone = extension;
            else
                zone = extension + (256 * extension2);

            // Device-type field: literal text if prefixed with $, otherwise look up
            // by numeric code (Ziton modules use a different table keyed on hex).
            string rawDevType = GetField(parts, F_DeviceType);
            string sDeviceType;
            if (rawDevType.StartsWith("$"))
            {
                sDeviceType = rawDevType.Substring(1);
            }
            else
            {
                int devTypeCode = ParseInt(rawDevType);
                if (state.ModuleType == "ZI")
                    sDeviceType = RsmLookups.GetZitonDeviceType(devTypeCode, inputType, extension2);
                else
                    sDeviceType = RsmLookups.GetDeviceType(devTypeCode, state.ModuleType);
            }

            // Truncate device text per module type (VB rules)
            int maxTextLen = (state.ModuleType == "AD") ? 26 : 40;
            if (deviceText.Length > maxTextLen)
                deviceText = deviceText.Substring(0, maxTextLen);

            // Status-15 events (LoopNum=0, InputType=15) get a fixed device-text override.
            // Module-restart rows additionally shift the prior deviceText into sDeviceType
            // (typically the firmware version reported on startup).
            if (loopNum == 0 && inputType == 15)
            {
                string statusText = RsmLookups.GetStatusText(state.ModuleType, address, onOff);
                if (statusText != null)
                {
                    if (RsmLookups.IsModuleRestart(state.ModuleType, address))
                    {
                        sDeviceType = deviceText;
                    }
                    deviceText = statusText;
                }
            }

            // Pull cached zone text from prior ZTX (or contact-text override for status 200)
            string zoneText = "";
            lock (state.ZoneTexts)
            {
                if (state.ZoneTexts.TryGetValue(zone, out string zt)) zoneText = zt;
            }
            if (inputType == 15 && address == 200)
            {
                zoneText = "Contact Your Fire Alarm Maintainer";
            }

            bool on = onOff != 0;

            // VB NodeInUse override: events from a module whose IP isn't in the
            // configured device list are routed to address 248 with input-type 15
            // ("Event from a node that is not in use") so AMX can flag them.
            // We use the in-memory devicesByIP loaded from the client's devices.json
            // as the "in use" list — empty list means no filtering.
            if (devicesByIP.Count > 0
                && !string.IsNullOrEmpty(state.LastKnownIP)
                && !devicesByIP.ContainsKey(state.LastKnownIP))
            {
                loopNum = 0;
                address = 248;
                inputType = 15;
                deviceText = "Event from a node that is not in use";
                sDeviceType = "";
                zoneText = "";
            }

            int evnum = CSAMXSingleton.CS.MakeInputNumber(state.ModuleNumber + Offset, loopNum, address, inputType, on);
            CSAMXSingleton.CS.SendAlarmToAMX(evnum, deviceText, sDeviceType, zoneText);
            CSAMXSingleton.CS.FlushMessages();

            this.NotifyClient($"EVT {Label(state)} L{loopNum} A{address} type={inputType} on={on} text='{deviceText}' devtype='{sDeviceType}' zone={zone}", false);
        }

        private void HandlePOL(ModuleState state, string[] parts)
        {
            state.ExpiryDateDays = ParseInt(GetField(parts, P_ExpiryDateDays));
            state.PanelsAllowed = ParseInt(GetField(parts, P_NumberOfPanels));
            state.ModuleOptions = GetField(parts, P_Options);
            this.NotifyClient($"POL {Label(state)} type={state.ModuleType} expiry-days={state.ExpiryDateDays} panels={state.PanelsAllowed} options={state.ModuleOptions}", false);
        }

        private void HandleZTX(ModuleState state, string[] parts)
        {
            // VB: Zone = Val(Ext2) + (256 * Val(Ext))
            int extension = ParseInt(GetField(parts, F_Extension));
            int extension2 = ParseInt(GetField(parts, F_Extension2));
            int zone = extension2 + (256 * extension);
            string text = GetField(parts, F_Text);
            lock (state.ZoneTexts)
            {
                state.ZoneTexts[zone] = text;
            }
            this.NotifyClient($"ZTX {Label(state)} zone={zone} text='{text}'", false);
        }

        private void HandleANA(ModuleState state, string[] parts)
        {
            int loopNum = ParseInt(GetField(parts, F_LoopNum));
            int address = ParseInt(GetField(parts, F_Address));
            int subAddress = ParseInt(GetField(parts, F_SubAddress));
            string value = GetField(parts, F_Text).Trim();
            string mode = GetField(parts, F_Extension);

            string rawDevType = GetField(parts, F_DeviceType);
            string sDeviceType = rawDevType.StartsWith("$") ? rawDevType.Substring(1) : rawDevType;

            int panelAndOffset = state.ModuleNumber + Offset;
            string msg = $"C2M:DEVANALOG|{panelAndOffset}|{loopNum}|{address}|{subAddress}|{value}|{mode}|{sDeviceType}||||";
            AMXTransfer.Instance.SendMessage(msg);

            this.NotifyClient($"ANA {Label(state)} L{loopNum} A{address} sub={subAddress} value={value} mode={mode}", false);
        }

        private void HandleSPX(ModuleState state, string[] parts)
        {
            int loopNum = ParseInt(GetField(parts, F_LoopNum));
            int address = ParseInt(GetField(parts, F_Address));
            int extension = ParseInt(GetField(parts, F_Extension));

            int startInputType, endInputType;
            bool skipFour = false;
            switch (extension)
            {
                case SPX_RST1TO14: startInputType = 1; endInputType = 14; break;
                case SPX_RST0TO14: startInputType = 0; endInputType = 14; break;
                case SPX_RST0TO14NOT4: startInputType = 0; endInputType = 14; skipFour = true; break;
                case SPX_RST0TO15: startInputType = 0; endInputType = 15; break;
                default:
                    this.NotifyClient($"SPX module={state.ModuleNumber} unrecognised extension={extension}", false);
                    return;
            }

            int nodePlusOffset = state.ModuleNumber + Offset;
            for (int n = startInputType; n <= endInputType; n++)
            {
                if (skipFour && n == 4) continue;
                int evnum = CSAMXSingleton.CS.MakeInputNumber(nodePlusOffset, loopNum, address, n, false);
                CSAMXSingleton.CS.SendResetToAMX(evnum, "", "", "");
            }
            CSAMXSingleton.CS.FlushMessages();

            this.NotifyClient($"SPX {Label(state)} L{loopNum} A{address} reset {startInputType}..{endInputType}{(skipFour ? " (excl 4)" : "")}", false);
        }

        #endregion

        #region helpers

        private ModuleState TouchModule(int moduleNumber, string ip, string moduleType, string serialNumber, NetworkStream stream)
        {
            string friendlyName = "";
            if (!string.IsNullOrEmpty(ip) && devicesByIP.TryGetValue(ip, out ClientDevice known))
            {
                friendlyName = known.Name ?? "";
            }

            lock (modulesLock)
            {
                if (!modules.TryGetValue(moduleNumber, out ModuleState state))
                {
                    state = new ModuleState { ModuleNumber = moduleNumber };
                    modules[moduleNumber] = state;
                    string label = string.IsNullOrEmpty(friendlyName) ? "<unknown>" : friendlyName;
                    this.NotifyClient($"NEW module {moduleNumber} ({moduleType}) at {ip} — {label}", false);
                }
                state.LastKnownIP = ip;
                state.FriendlyName = friendlyName;
                state.LastRX = DateTime.Now;
                state.RXmessages++;
                if (!string.IsNullOrEmpty(moduleType)) state.ModuleType = moduleType;
                if (!string.IsNullOrEmpty(serialNumber)) state.SerialNumber = serialNumber;
                // Outbound write target for this module's connection.
                if (stream != null) state.Stream = stream;
                return state;
            }
        }

        /// <summary>
        /// Called by rsmHandleClient when the TCP connection closes. Clears the
        /// outbound write target if it's still pointing at this stream (a newer
        /// connection from the same module would have already replaced it).
        /// </summary>
        public void UnregisterStream(int moduleNumber, NetworkStream stream)
        {
            lock (modulesLock)
            {
                if (modules.TryGetValue(moduleNumber, out ModuleState state)
                    && ReferenceEquals(state.Stream, stream))
                {
                    state.Stream = null;
                    this.NotifyClient($"Disconnected {Label(state)}", false);
                }
            }
        }

        private int NextMessageID()
        {
            lock (messageIDLock)
            {
                int id = nextMessageID++;
                if (nextMessageID > 9999) nextMessageID = 1;
                return id;
            }
        }

        private static string GetField(string[] parts, int idx)
        {
            return idx < parts.Length ? parts[idx] : "";
        }

        private static string Label(ModuleState s)
        {
            return string.IsNullOrEmpty(s.FriendlyName)
                ? $"module={s.ModuleNumber}"
                : $"'{s.FriendlyName}' (module={s.ModuleNumber})";
        }

        private static int ParseInt(string s)
        {
            int.TryParse(s == null ? "" : s.Trim(), out int v);
            return v;
        }

        #endregion

        #region wire-format encoding/decoding

        private byte[] scrambleandencodemessage(string message)
        {
            // Calculate checksum over message chars
            int checksum = 0;
            foreach (char c in message)
            {
                checksum += (int)c;
            }
            checksum = (checksum % 200) + 33;

            // Reverse the string then apply scramble formula
            char[] chars = message.ToCharArray();
            Array.Reverse(chars);
            string reversed = new string(chars);

            List<byte> scrambled = new List<byte>();
            for (int n = 1; n <= reversed.Length; n++)
            {
                int charValue = (int)reversed[n - 1];
                int encoded = charValue + 3 + (n % 9) + ((n % 5) * 7);
                while (encoded > 255) encoded -= 256;
                scrambled.Add((byte)encoded);
            }

            // STX + scrambled + checksum + ETX
            List<byte> fullMessage = new List<byte>();
            fullMessage.Add(0x02);
            fullMessage.AddRange(scrambled);
            fullMessage.Add((byte)checksum);
            fullMessage.Add(0x03);

            return fullMessage.ToArray();
        }

        private string decodedata(byte[] data)
        {
            if (data.Length < 3)
                return Encoding.ASCII.GetString(data);

            // Strip STX (0x02) and ETX (0x03)
            List<byte> cleanedData = new List<byte>();
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] != 0x02 && data[i] != 0x03)
                {
                    cleanedData.Add(data[i]);
                }
            }
            if (cleanedData.Count < 2)
                return "";

            int dataLength = cleanedData.Count - 1; // exclude trailing checksum
            int checksumByte = cleanedData[cleanedData.Count - 1];

            // Descramble
            StringBuilder descrambled = new StringBuilder();
            for (int n = 1; n <= dataLength; n++)
            {
                int byteValue = cleanedData[n - 1];
                int decoded = byteValue - 3 - (n % 9) - ((n % 5) * 7);
                while (decoded < 0) decoded += 256;
                while (decoded > 255) decoded -= 256;
                descrambled.Append((char)decoded);
            }

            // Reverse
            char[] chars = descrambled.ToString().ToCharArray();
            Array.Reverse(chars);
            string result = new string(chars);

            // Verify checksum
            int calculatedChecksum = 0;
            for (int n = 0; n < result.Length; n++)
            {
                calculatedChecksum += (int)result[n];
            }
            calculatedChecksum = (calculatedChecksum % 200) + 33;
            if (calculatedChecksum != checksumByte)
            {
                result += $" [CHECKSUM ERROR: Expected {checksumByte}, Got {calculatedChecksum}]";
            }

            return result;
        }

        #endregion

        #region heartbeat / lifecycle

        protected override void heartbeat_timer_callback(object sender)
        {
            base.heartbeat_timer_callback(sender);
        }

        public override void StartUp(int fakemode)
        {
            if (fakemode > 0)
            {
                return;
            }
        }

        #endregion

        // POCO matching the client's devices.json schema
        // (Drax360Client/Panels/RSM/Device.cs — { ID: Guid, Name: string, IP: string })
        private class ClientDevice
        {
            public string ID { get; set; }
            public string Name { get; set; }
            public string IP { get; set; }
        }

        #region commands out (TX path — writes scrambled CMD packets to the
        // module's open TCP connection. Wire format mirrors VB6 MakeNewMessage:
        //   STX + Scramble("CMD;{ID};{Node};{Serial};;{cmdToPanel};{params}") + ETX

        // Mirror of VB6 cmdToPanel enum (RSMenum.bas). Position-sensitive — the
        // numeric values are the wire protocol. Don't reorder.
        private enum CmdToPanel
        {
            MuteBuzzer = 0,
            Evacuate = 1,
            SilenceAlarms = 2,
            Reset = 3,
            ResoundAlarms = 4,
            DisableDevice = 5,
            EnableDevice = 6,
            DisableZone = 7,
            EnableZone = 8,
            // 9..36 omitted — add as needed; values must still match VB enum order
            EvacuateNetwork = 37,
            SilenceNetworkAlarms = 38,
            MuteNetworkBuzzers = 39,
            ResetNetwork = 40,
        }

        public override void Evacuate(string passedvalues)        { SendCommand(CmdToPanel.Evacuate,         passedvalues); }
        public override void Alert(string passedvalues)           { /* no Alert verb in cmdToPanel — panels don't accept this */ }
        public override void EvacuateNetwork(string passedvalues) { SendCommand(CmdToPanel.EvacuateNetwork,  passedvalues); }
        public override void Silence(string passedvalues)         { SendCommand(CmdToPanel.SilenceAlarms,    passedvalues); }
        public override void MuteBuzzers(string passedvalues)     { SendCommand(CmdToPanel.MuteBuzzer,       passedvalues); }
        public override void Reset(string passedvalues)           { SendCommand(CmdToPanel.Reset,            passedvalues); }
        public override void DisableDevice(string passedvalues)   { SendCommand(CmdToPanel.DisableDevice,    passedvalues, withDeviceParams: true); }
        public override void EnableDevice(string passedvalues)    { SendCommand(CmdToPanel.EnableDevice,     passedvalues, withDeviceParams: true); }
        public override void DisableZone(string passedvalues)     { SendCommand(CmdToPanel.DisableZone,      passedvalues, withDeviceParams: true); }
        public override void EnableZone(string passedvalues)      { SendCommand(CmdToPanel.EnableZone,       passedvalues, withDeviceParams: true); }
        public override void Analogue(string passedvalues)        { throw new NotImplementedException(); }

        /// <summary>
        /// Resolves the target module from passedvalues' first CSV field
        /// ("node,loop,zone,device") with the AMX offset applied in reverse,
        /// then writes a scrambled CMD packet to that module's open TCP
        /// connection. No-ops with a log line if the target isn't connected.
        /// </summary>
        private void SendCommand(CmdToPanel cmd, string passedvalues, bool withDeviceParams = false)
        {
            string[] parts = string.IsNullOrEmpty(passedvalues) ? new string[0] : passedvalues.Split(',');
            int node = parts.Length > 0 ? ParseInt(parts[0]) : 0;
            int loop = parts.Length > 1 ? ParseInt(parts[1]) : 0;
            int zone = parts.Length > 2 ? ParseInt(parts[2]) : 0;
            int device = parts.Length > 3 ? ParseInt(parts[3]) : 0;

            // Inbound EVTs report (Node + Offset) as the module identifier in AMX;
            // outbound commands target the raw ModuleNumber stored in ModuleState.
            int targetModule = node - this.Offset;

            ModuleState state;
            NetworkStream stream;
            lock (modulesLock)
            {
                if (!modules.TryGetValue(targetModule, out state))
                {
                    this.NotifyClient($"SendCommand {cmd}: module {targetModule} (from node {node} - offset {Offset}) not known", false);
                    return;
                }
                stream = state.Stream;
            }
            if (stream == null)
            {
                this.NotifyClient($"SendCommand {cmd} {Label(state)}: not currently connected", false);
                return;
            }

            // Build the DataPacket. Panel-wide verbs need just the cmd value;
            // device/zone verbs append loop, address, sub-address (zone here).
            string dataPacket = ((int)cmd).ToString();
            if (withDeviceParams)
            {
                dataPacket += kSeparator + loop + kSeparator + device + kSeparator + zone;
            }

            int messageID = NextMessageID();
            string body = "CMD" + kSeparator + messageID + kSeparator + targetModule + kSeparator
                          + (state.SerialNumber ?? "") + kSeparator + kSeparator + dataPacket;
            byte[] bytes = scrambleandencodemessage(body);

            lock (state.WriteLock)
            {
                try
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }
                catch (Exception ex)
                {
                    this.NotifyClient($"SendCommand {cmd} {Label(state)} write failed: {ex.Message}", false);
                    return;
                }
            }

            this.NotifyClient($"SendCommand {cmd} -> {Label(state)} (msgID={messageID}, params='{dataPacket}')", false);
        }

        #endregion
    }
}
