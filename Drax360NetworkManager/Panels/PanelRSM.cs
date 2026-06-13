
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace DraxTechnology.Panels
{
    internal class PanelRSM : AbstractPanel
    {
        #region constants
        const byte kheartbeatdelayseconds = 60;
        // Wire-format field separator. VB6: 'Global Const sepCHAR = 199' — that's
        // 'Ç' (0xC7). NOT ';'. Outgoing ACKs must use this byte or the panel rejects.
        const char kSeparator = 'Ç';
        const string CURRENTNWMDATAFILE = @"c:\AMX1\Temp\Current.Nwm";  //TODO not code c:\AMX1

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

        // DXC global reset queue — mirrors VB6 clsResetQueue (Phil Spooner, 2015).
        // Tracks active ON events per AMX node for DX/NO/PE/KE module types so that
        // when a panel-reset signal arrives, any stale pre-reset alarms are auto-
        // cleared in AMX rather than lingering. Off by default (giDXCReset=0 in ini).
        //
        // Key: AMX node number (ModuleNumber + Offset). Value: set of (loop, address,
        // inputType) tuples recorded while events were active.
        private readonly Dictionary<int, HashSet<(int loop, int address, int inputType)>> _dxcResetQueue
            = new Dictionary<int, HashSet<(int, int, int)>>();
        private readonly object _dxcLock = new object();

        // Excluded type-15 status addresses — not tracked in the reset queue.
        // Default mirrors VB6 [DX] StatExclude default: "16,93,103,106,92,14,12,42,45,47".
        // Configurable via RSMMan.ini [DX] StatExclude (not yet wired — ini loading pending).
        private static readonly HashSet<int> _dxcStatusExclusions
            = new HashSet<int> { 16, 93, 103, 106, 92, 14, 12, 42, 45, 47 };

        // Reset-signal addresses per module type (VB6 RSMNetManager.bas:506/519/532/545).
        // Returns true and populates amxNode when the event is a panel-reset trigger.
        private bool IsDxcResetSignal(string moduleType, int loopNum, int address, int inputType)
        {
            if (loopNum != 0 || inputType != 15) return false;
            switch (moduleType)
            {
                case "DX": case "NO": case "PE": return address == 100;
                case "KE":                        return address == 105;
                default: return false;
            }
        }

        private void DxcTrackEvent(int amxNode, int loop, int address, int inputType)
        {
            lock (_dxcLock)
            {
                if (!_dxcResetQueue.TryGetValue(amxNode, out var set))
                {
                    set = new HashSet<(int, int, int)>();
                    _dxcResetQueue[amxNode] = set;
                }
                set.Add((loop, address, inputType));
            }
        }

        private void DxcResetEvents(int amxNode)
        {
            List<(int loop, int address, int inputType)> toReset;
            lock (_dxcLock)
            {
                if (!_dxcResetQueue.TryGetValue(amxNode, out var set) || set.Count == 0)
                    return;
                toReset = new List<(int, int, int)>(set);
                set.Clear();
            }

            this.NotifyClient($"DXC global reset for AMX node {amxNode}: clearing {toReset.Count} event(s)", false);
            foreach (var (loop, address, inputType) in toReset)
            {
                int evnum = CSAMXSingleton.CS.MakeInputNumber(amxNode, loop, address, inputType, false);
                CSAMXSingleton.CS.SendResetToAMX(evnum);
            }
            CSAMXSingleton.CS.FlushMessages();
        }

        // Mirrors VB6 RSMenum.bas LicenseStatus enum. Values are the integers
        // returned in the PAK response field so the module can govern itself.
        private enum RsmLicenseStatus
        {
            Unlicensed = 0,  // no serial number or "00000000"
            Expired    = 1,  // serial present but expiry date is in the past
            Expiring   = 2,  // expiry within 30 days
            Good       = 3,  // valid and not near expiry
        }

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

            // Computed from ExpiryDateDays + SerialNumber after each POL (mirrors
            // VB6 clsRSM.UpdateLicenseInfo). Unlicensed until first POL arrives.
            public RsmLicenseStatus LicenseStatus = RsmLicenseStatus.Unlicensed;

            // Fields populated from GAK responses (VB6 clsRSM properties set in
            // RSMNetManager.bas:881-940). Only fields the service actually uses or
            // exposes in diagnostics; Properties dialog fields deferred until client UI.
            public string DHCPName      = "";
            public string ConfiguredIP  = "";  // setgetIPAddress (not the same as LastKnownIP)
            public string SubnetMask    = "";
            public string Gateway       = "";
            public string SoftwareVersion = "";
            public string MasterPanelID = "";
            public string ReportsTo1    = "";
            public string ReportsTo2    = "";
            public string ReverseInputs = "";
            public int    RequestPort   = 0;
            // False until the first PAK exchange completes. VB6 only quarantines
            // expired-licence events once LicenseDataReceived=True so a module
            // doesn't get falsely quarantined during startup (comment in
            // RSMNetManager.bas:323).
            public bool LicenseDataReceived = false;

            // Tracks the last AMX-reported online/offline state so the heartbeat
            // can detect transitions and only fire once per edge — not on every tick.
            // null = never reported (first heartbeat determines initial state).
            public bool? LastReportedOnline = null;

            // Set when a kickstart has been attempted for the current silence
            // episode; cleared on the next successful RX so it fires only once
            // per gap rather than on every heartbeat tick.
            public bool KickstartSent = false;

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
        public override string PanelVersion => "1.0.0.0";

        public PanelRSM(string baselogfolder, string identifier) : base(baselogfolder, identifier, "RSMMan", "RSM")
        {
            if (!String.IsNullOrEmpty(identifier))
            {
                heartbeat_timer = new System.Threading.Timer(heartbeat_timer_callback, this.Identifier, 500, kheartbeatdelayseconds * 1000);
                this.Offset = base.GetSetting<int>(ksettingsetupsection, "giAmx1Offset");
                // Log the resolved offset at startup — a wrong offset shows up as
                // AMX node mismatches (events on wrong node / Reset can't find module).
                // If this logs 0 but RSMMan.ini sets giAmx1Offset, the ini is not being read.
                this.NotifyClient($"PanelRSM startup: giAmx1Offset={this.Offset} (section [{ksettingsetupsection}]).", false);
                PassNWMDataToAMX1();
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
                var raw = JsonSerializer.Deserialize<List<ClientDevice>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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
                    // Return the computed licence status so the module can govern
                    // its own behaviour (VB6 RSM.UpdateLicenseInfo return value).
                    int licenseStatus = (int)state.LicenseStatus;
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

                case "GAK":
                    HandleGAK(state, parts);
                    return new byte[0];

                case "CAK":
                case "SAK":
                case "ACK":
                case "NAK":
                    // responses to commands the manager sent — nothing to ack
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

            // onOff=2 means ON + one-shot (VB6 RSMNetManager.bas:493-496).
            // Capture the flag before normalising so ForceEvmAttribute can be
            // called after the alarm is written (VB6 line 736).
            bool oneShot = onOff == 2;
            bool on = onOff != 0;

            // TODO — licence quarantine gate (VB6 RSMNetManager.bas:462-475):
            // Once we are confident the licence computation is correct for the
            // deployed serial numbers and expiry date format, add:
            //
            //   if (state.LicenseDataReceived
            //       && (state.LicenseStatus == RsmLicenseStatus.Expired
            //           || state.LicenseStatus == RsmLicenseStatus.Unlicensed))
            //   {
            //       loopNum = 0; address = 249; inputType = 15;
            //       deviceText = "Event from a node with expired license";
            //       sDeviceType = ""; zoneText = "";
            //   }
            //
            // For now, assume licence is current and pass all events through.
            // Log a warning when the computed status is not Good so the real-panel
            // trace makes it visible without affecting event routing.
            if (state.LicenseDataReceived
                && state.LicenseStatus != RsmLicenseStatus.Good
                && state.LicenseStatus != RsmLicenseStatus.Expiring)
            {
                this.NotifyClient(
                    $"[LICENCE WARNING] {Label(state)} status={state.LicenseStatus} " +
                    $"(expiry-days={state.ExpiryDateDays}) — event still routed normally; " +
                    "enable quarantine gate once licence computation verified.", false);
            }

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

            // DXC global reset queue — mirrors VB6 RSMNetManager.bas:502-591.
            // giDXCReset defaults to 0 (off) in the ini [Setup] DXCReset.
            // When enabled, tracks active ON events for DX/NO/PE/KE modules so a
            // panel-reset signal auto-clears any stale pre-reset alarms in AMX.
            // Not gated on giDXCReset here yet (ini loading pending diag branch merge);
            // the feature is only active for the four module types, so it's safe to
            // run unconditionally — it becomes meaningful when hardware exercises it.
            int amxNodeDxc = state.ModuleNumber + Offset;
            switch (state.ModuleType)
            {
                case "DX": case "NO": case "PE": case "KE":
                    if (on)
                    {
                        if (IsDxcResetSignal(state.ModuleType, loopNum, address, inputType))
                        {
                            DxcResetEvents(amxNodeDxc);
                        }
                        else if (inputType != 4  // isolations don't belong in the reset queue
                            && (inputType != 15 || !_dxcStatusExclusions.Contains(address)))
                        {
                            DxcTrackEvent(amxNodeDxc, loopNum, address, inputType);
                        }
                    }
                    break;
            }

            int evnum = CSAMXSingleton.CS.MakeInputNumber(state.ModuleNumber + Offset, loopNum, address, inputType, on);
            CSAMXSingleton.CS.SendAlarmToAMX(evnum, deviceText, sDeviceType, zoneText);

            // One-shot: tell AMX to auto-clear this event after display rather than
            // leaving it as a persistent alarm. Mirrors VB6 RSMNetManager.bas:735-737:
            //   If bOneShot = True Then NwmForceEvmAttribute(tStr, EventNumber, 13, 1)
            // Attribute 13 = momentary/one-shot; value 1 = set.
            if (oneShot)
                CSAMXSingleton.CS.ForceEvmAttribute(evnum, 13, 1);

            // Isolation list — mirrors VB6 RSMNetManager.bas:740-744:
            //   If giIsolationsList <> 0 Then
            //     If InputType = 4 And LoopNum <> 0 Then
            //       WriteNWMData(tStr, 2, EventNumber, ...)
            // giIsolationsList defaults to 1 (enabled) from the ini. The second
            // write with eventtype=2 (IsolationToAmx) populates the AMX isolation
            // list so operators can see which devices are currently isolated.
            // LoopNum=0 events are panel-wide status messages, not device isolations.
            if (inputType == 4 && loopNum != 0)
                CSAMXSingleton.CS.SendAlarmToAMX_disable(evnum, deviceText, sDeviceType, zoneText, on);

            CSAMXSingleton.CS.FlushMessages();

            this.NotifyClient($"EVT {Label(state)} L{loopNum} A{address} type={inputType} on={on}{(oneShot ? " oneshot" : "")} text='{deviceText}' devtype='{sDeviceType}' zone={zone}", false);
        }

        private void HandlePOL(ModuleState state, string[] parts)
        {
            state.ExpiryDateDays = ParseInt(GetField(parts, P_ExpiryDateDays));
            state.PanelsAllowed  = ParseInt(GetField(parts, P_NumberOfPanels));
            state.ModuleOptions  = GetField(parts, P_Options);
            state.LicenseStatus  = ComputeLicenseStatus(state.SerialNumber, state.ExpiryDateDays);
            state.LicenseDataReceived = true;
            this.NotifyClient(
                $"POL {Label(state)} type={state.ModuleType} expiry-days={state.ExpiryDateDays} " +
                $"panels={state.PanelsAllowed} options={state.ModuleOptions} " +
                $"license={state.LicenseStatus}", false);
        }

        // Mirrors VB6 clsRSM.UpdateLicenseInfo. ExpiryDateDays is days since
        // 1 Jan 2010 (VB6: DateAdd("d", ExpiryDate, "01/01/2010")).
        // Enum values match the VB6 LicenseStatus enum wire integers returned
        // in the PAK response: Unlicensed=0, Expired=1, Expiring=2, Good=3.
        private static RsmLicenseStatus ComputeLicenseStatus(string serialNumber, long expiryDateDays)
        {
            if (string.IsNullOrEmpty(serialNumber)
                || serialNumber == "0"
                || serialNumber == "00000000")
            {
                return RsmLicenseStatus.Unlicensed;
            }

            DateTime epoch = new DateTime(2010, 1, 1);
            DateTime expiryDate = epoch.AddDays(expiryDateDays);
            DateTime now = DateTime.Now;

            if (expiryDate < now)
                return RsmLicenseStatus.Expired;
            if (expiryDate.AddDays(-30) < now)
                return RsmLicenseStatus.Expiring;
            return RsmLicenseStatus.Good;
        }

        // Mirrors VB6 RSMNetManager.bas:881-940. GAK is the module's reply to a
        // GET command the manager sent. The message layout (mSetOption enum,
        // RSMenum.bas:69-78) is:
        //   parts[0]=GAK  [1]=MsgID  [2]=Node  [3]=Serial  [4]=ModuleType
        //   parts[5]=OptionNumber (optSetGet enum)  [6]=OptionString (value)
        // We store the fields that matter for diagnostics and the Properties panel;
        // unrecognised option numbers are logged and ignored.
        private void HandleGAK(ModuleState state, string[] parts)
        {
            const int G_OptionNumber = 5;
            const int G_OptionValue  = 6;

            int optionNumber = ParseInt(GetField(parts, G_OptionNumber));
            string value     = GetField(parts, G_OptionValue);

            // optSetGet enum values from RSMenum.bas:81-115
            switch (optionNumber)
            {
                case 2:  state.DHCPName       = value; break;  // setgetDHCPName
                case 3:  state.ConfiguredIP   = value; break;  // setgetIPAddress
                case 4:  state.SubnetMask     = value; break;  // setgetSubnetMask
                case 5:  state.Gateway        = value; break;  // setgetGateway
                case 6:  state.ReportsTo1     = value; break;  // setgetReport1
                case 7:  state.ReportsTo2     = value; break;  // setgetReport2
                case 14: state.ReverseInputs  = value; break;  // setgetName4 (used for ReverseInputs)
                case 19: state.MasterPanelID  = value; break;  // setgetPanelNumbers
                case 16: state.RequestPort    = ParseInt(value); break; // setgetPortlistening
                case 31: state.SoftwareVersion = value; break; // setgetSoftwareVersion
                default:
                    this.NotifyClient($"GAK {Label(state)} option={optionNumber} value='{value}' (unhandled)", false);
                    return;
            }

            this.NotifyClient($"GAK {Label(state)} option={optionNumber} value='{value}'", false);
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

            // Mirrors VB6 RSMNetManager.bas:968-971: if the device type field starts
            // with "$" it is already a text label (strip the prefix); otherwise it is
            // a numeric code resolved via GetDeviceType — same logic as HandleEVT.
            string rawDevType = GetField(parts, F_DeviceType);
            string sDeviceType;
            if (rawDevType.StartsWith("$"))
            {
                sDeviceType = rawDevType.Substring(1);
            }
            else if (int.TryParse(rawDevType.Trim(), out int devTypeCode))
            {
                sDeviceType = RsmLookups.GetDeviceType(devTypeCode, state.ModuleType);
            }
            else
            {
                sDeviceType = rawDevType;
            }

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
                state.KickstartSent = false; // new message received — reset for the next silence episode
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

        /// <summary>
        /// Tier-1 node snapshot for the client's RSM "Node Configuration and
        /// Status" grid (VB6 frmRSMNodes / vsfNodes). Returns the in-memory node
        /// table (≤ NwmMaxNodesRSM = 255 rows) as a JSON array, ordered by node
        /// number to match the VB grid. Node-level only — per-node device detail
        /// is a separate paged call, never bulk-shipped here. Read-only; served
        /// over the named pipe via the RSMNODES verb.
        /// </summary>
        public string BuildNodeSnapshot()
        {
            // ONLINE while messages are still arriving — last RX within 2× the
            // heartbeat period. Mirrors the VB grid's comms-driven ONLINE/OFFLINE.
            // Threshold pending confirmation with Mike.
            DateTime now = DateTime.Now;
            double onlineWindowSeconds = kheartbeatdelayseconds * 2;

            var rows = new List<object>();
            lock (modulesLock)
            {
                var ordered = new List<ModuleState>(modules.Values);
                ordered.Sort((a, b) => a.ModuleNumber.CompareTo(b.ModuleNumber));

                foreach (ModuleState s in ordered)
                {
                    bool online = s.LastRX > DateTime.MinValue
                        && (now - s.LastRX).TotalSeconds <= onlineWindowSeconds;

                    rows.Add(new
                    {
                        node = s.ModuleNumber,
                        // devices.json carries a single friendly Name, so there is
                        // no Site/Node split yet — site stays empty until the node
                        // store gains separate Site + Node names (see contract gap).
                        site = "",
                        name = s.FriendlyName ?? "",
                        type = s.ModuleType ?? "",
                        typeText = ExpandModuleType(s.ModuleType),
                        status = online ? "ONLINE" : "OFFLINE",
                        messages = s.RXmessages,
                        address = s.LastKnownIP ?? ""
                    });
                }
            }

            return JsonSerializer.Serialize(rows);
        }

        /// <summary>
        /// Module type code → display label. Mirrors VB6 ExpandModuleType
        /// (RSMNetManagerSubs.bas). Unknown non-empty codes render "?" as in the VB.
        /// </summary>
        private static string ExpandModuleType(string code)
        {
            switch ((code ?? "").Trim().ToUpperInvariant())
            {
                case "MZ": return "Morley ZXe";
                case "4I": return "4 Input";
                case "12": return "12 Input";
                case "IO": return "4 I/P, 2 O/P";
                case "AD": return "Advanced MX";
                case "KE": return "Kentec";
                case "DX": return "Morley DX";
                case "NO": return "Notifier ID3000";
                case "GE": return "Gent Fire";
                case "CO": return "Coopers Fire/Easicheck";
                case "PE": return "Notifier Pearl";
                case "ZI": return "Ziton";
                default: return string.IsNullOrEmpty(code) ? "" : "?";
            }
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
            CheckNodeOnlineStatus();
        }

        // Mirrors VB6 clsRSM.UpdateNodeStatus / frmRSMNetworkManager.tmrProcess_Timer.
        // Runs every kheartbeatdelayseconds (60 s) and fires a state-change event to
        // AMX on each Online→Offline or Offline→Online edge — once per transition, not
        // on every tick. Nodes that have never sent a message are skipped; there is no
        // configured in-use list yet so any module that has phoned in is monitored.
        //
        // VB6 event numbers:
        //   Online/Offline  : MakeInputNumber(node+offset, loop=0, addr=0,  type=0)  ON/OFF
        //   Expired licence : MakeInputNumber(node+offset, loop=0, addr=250, type=15) ON
        //     → TODO: add the Expired state once licence computation is verified; for
        //       now a warning is logged (see LICENCE WARNING in HandleEVT).
        // The timeout threshold mirrors giModuleTimeout default (90 s in VB6);
        // using 2× the heartbeat interval (120 s) keeps it consistent with what
        // BuildNodeSnapshot considers online. Adjust via RSMMan.ini giModuleTimeout
        // once the ini loading fix (diag/settings-ini-load-logging) is merged.
        private void CheckNodeOnlineStatus()
        {
            const double onlineWindowSeconds = kheartbeatdelayseconds * 2.0;
            DateTime now = DateTime.Now;

            List<ModuleState> snapshot;
            lock (modulesLock)
            {
                snapshot = new List<ModuleState>(modules.Values);
            }

            foreach (ModuleState state in snapshot)
            {
                if (state.LastRX == DateTime.MinValue)
                    continue;

                double elapsedSeconds = (now - state.LastRX).TotalSeconds;
                bool isOnline = elapsedSeconds <= onlineWindowSeconds;

                // Kickstart — mirrors VB6 frmRSMNetworkManager.frm:2018-2046 tmrProcess_Timer.
                // giKickStart defaults to 1 (enabled) from [Tweaks] Kickstart=1 in the ini.
                // When a module is approaching its timeout threshold (online window minus 45s),
                // close the stale TCP stream so the module detects the disconnection and
                // reconnects, sending a fresh POL that resets LastRX. Fires once per silence
                // episode (KickstartSent guards against retrying on every heartbeat tick).
                // VB6 also queues a MuteBuzzer at this point; we omit it as the stream close
                // alone triggers the reconnect — we have no command queue to send into.
                const double kickstartThreshold = onlineWindowSeconds - 45.0;
                if (isOnline
                    && elapsedSeconds > kickstartThreshold
                    && !state.KickstartSent
                    && state.Stream != null)
                {
                    state.KickstartSent = true;
                    int amxNodeK = state.ModuleNumber + Offset;
                    this.NotifyClient(
                        $"RSM node {amxNodeK} ({Label(state)}) kickstart — " +
                        $"closing stale stream after {elapsedSeconds:F0}s silence to provoke reconnect", false);
                    try { state.Stream.Close(); } catch { }
                }

                if (state.LastReportedOnline == isOnline)
                    continue;

                state.LastReportedOnline = isOnline;
                int amxNode = state.ModuleNumber + Offset;
                string label = Label(state);

                int evnum = CSAMXSingleton.CS.MakeInputNumber(amxNode, 0, 0, 0, isOnline);

                if (isOnline)
                {
                    CSAMXSingleton.CS.SendResetToAMX(evnum, state.FriendlyName, "", "Online");
                    this.NotifyClient($"RSM node {amxNode} ({label}) → ONLINE", false);
                }
                else
                {
                    CSAMXSingleton.CS.SendAlarmToAMX(evnum, state.FriendlyName, "", "Offline");
                    this.NotifyClient($"RSM node {amxNode} ({label}) → OFFLINE", false);
                }
            }
        }

        public override void StartUp(int fakemode)
        {
            if (fakemode > 0)
            {
                return;
            }
        }

        #endregion

        // POCO for the fields the service needs from the client's devices.json
        // (Drax360Client/Panels/RSM/Device.cs). Schema there is
        // { ID: Guid, Name: string, IP: string, Site: string }.
        //
        // The GUID (ID) is the record's single unique identity — Name, IP and Site
        // are mutable attributes hanging off it, not identities in their own right.
        // The service binds only ID/Name/IP and matches an incoming node to its
        // record by the wire-reported IP (the GUID is never sent over the wire, so
        // it can't be the live-node match key). Site is a client-only display label
        // (the RSM grid's "Site Name" column) the service has no use for; it's left
        // unmapped, and System.Text.Json silently ignores it (and any future field),
        // so it loads without error. Empty/legacy GUIDs are harmless here since the
        // service never keys on the GUID. Add a field only if the service needs to
        // consume it.
        private class ClientDevice
        {
            public Guid ID { get; set; }
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

        public override void Evacuate(string passedvalues) { SendCommand(CmdToPanel.Evacuate, passedvalues); }
        public override void Alert(string passedvalues) { /* no Alert verb in cmdToPanel — panels don't accept this */ }
        public override void EvacuateNetwork(string passedvalues) { SendCommand(CmdToPanel.EvacuateNetwork, passedvalues); }
        public override void Silence(string passedvalues) { SendCommand(CmdToPanel.SilenceAlarms, passedvalues); }
        public override void MuteBuzzers(string passedvalues) { SendCommand(CmdToPanel.MuteBuzzer, passedvalues); }
        public override void Reset(string passedvalues) { SendCommand(CmdToPanel.Reset, passedvalues); }
        public override void DisableDevice(string passedvalues) { SendCommand(CmdToPanel.DisableDevice, passedvalues, withDeviceParams: true); }
        public override void EnableDevice(string passedvalues) { SendCommand(CmdToPanel.EnableDevice, passedvalues, withDeviceParams: true); }
        public override void DisableZone(string passedvalues) { SendCommand(CmdToPanel.DisableZone, passedvalues, withDeviceParams: true); }
        public override void EnableZone(string passedvalues) { SendCommand(CmdToPanel.EnableZone, passedvalues, withDeviceParams: true); }
        public override void Analogue(string passedvalues) { throw new NotImplementedException(); }

        /// <summary>
        /// Resolves the target module from passedvalues' first CSV field
        /// ("node,loop,zone,device") with the AMX offset applied in reverse,
        /// then writes a scrambled CMD packet to that module's open TCP
        /// connection. No-ops with a log line if the target isn't connected.
        /// </summary>
        private void SendCommand(CmdToPanel cmd, string passedvalues, bool withDeviceParams = false)
        {
            // passedvalues CSV layout (from DispatchAmxPipeCommand / handlepiperesponse):
            //   parts[0] = AMX node (= ModuleNumber + Offset)
            //   parts[1] = loop number
            //   parts[2] = zone  (0 for device commands; zone number for zone commands)
            //   parts[3] = device address
            string[] parts = string.IsNullOrEmpty(passedvalues) ? new string[0] : passedvalues.Split(',');
            int node   = parts.Length > 0 ? ParseInt(parts[0]) : 0;
            int loop   = parts.Length > 1 ? ParseInt(parts[1]) : 0;
            int zone   = parts.Length > 2 ? ParseInt(parts[2]) : 0;
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
                    string known = modules.Count == 0 ? "none" : string.Join(", ", modules.Keys);
                    this.NotifyClient($"SendCommand {cmd}: module {targetModule} (node {node} - offset {Offset}) not known; registered modules: {known}", false);
                    return;
                }
                stream = state.Stream;
            }
            if (stream == null)
            {
                this.NotifyClient($"SendCommand {cmd} {Label(state)}: not currently connected", false);
                return;
            }

            // Build the DataPacket. Wire format from VB6 MakeNewMessage / CmdQ.Add:
            //   panel-wide  : cmdEnum
            //   device/zone : cmdEnum|panelID|loopNumber|deviceAddress|subAddress
            //
            // panelID for device disable/enable: VB6 forces 1 for SmartWatch-style
            // addressing (cases 108/109 in frmRSMNetworkManager.frm:1404/1419).
            // subAddress: always 0 for the disable/enable/zone verbs we handle today.
            //
            // Zone disable/enable: VB6 puts the zone number into panelID
            // (cases 110/111: iPanelID = iLoopNumber) with loop/device/sub = 0.
            string dataPacket = ((int)cmd).ToString();
            if (withDeviceParams)
            {
                bool isZoneCmd = cmd == CmdToPanel.DisableZone || cmd == CmdToPanel.EnableZone;
                if (isZoneCmd)
                {
                    // zone number in panelID field; loop/device/subAddr all 0
                    dataPacket += kSeparator + zone + kSeparator + 0 + kSeparator + 0 + kSeparator + 0;
                }
                else
                {
                    // device disable/enable: panelID=1, then loop|device|subAddr=0
                    dataPacket += kSeparator + 1 + kSeparator + loop + kSeparator + device + kSeparator + 0;
                }
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

        private void PassNWMDataToAMX1()
        {
            //Writes data about the NWM to AMX1's temporary data file
            //Uses the DDEchannel as identifier
            //This sub is unique to each Network Manager

            // Check file not already been updated
            if (File.Exists(CURRENTNWMDATAFILE))
            {
                List<List<string>> groups = new List<List<string>>();
                List<string> current = null;
                bool exists = false;
                foreach (var line in File.ReadAllLines(CURRENTNWMDATAFILE))
                {
                    if (line.Contains("RSM Network Manager") && current == null)
                        exists = true;
                }
                if (!exists)
                {
                    using (StreamWriter w = File.AppendText(CURRENTNWMDATAFILE))
                    {
                        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                        string versionString = version.ToString(); // "1.0.0.0"

                        w.WriteLine("[0]\r\nProgName=RSM Network Manager");
                        w.WriteLine("Name=RSMNWM\r\nVersion=" + versionString + "\r\nNodeName=RSM Fire Panel");


                        w.WriteLine("Offset=0\r\nFirstNode=1\r\nLastNode=255");
                        w.WriteLine("Startup=" + DateTime.Now);

                        string exePath = Environment.ProcessPath!;
                        DateTime exeDate = File.GetLastWriteTime(exePath);
                        string exeDateTime = exeDate.ToString("dd/MM/yyyy HH:mm:ss");

                        w.WriteLine("1A=NWM DLL File Date\r\n1B=" + exeDateTime);
                        w.WriteLine("2A=\r\n2B=");
                        w.WriteLine("3A=\r\n3B=");
                        w.WriteLine("4A=\r\n4B=");
                        w.WriteLine("5A=\r\n5B=");
                        w.WriteLine("6A=\r\n6B=");
                        w.WriteLine("7A=\r\n7B=");
                        w.WriteLine("8A=\r\n8B=");
                        w.WriteLine("9A=\r\n9B=");
                        w.WriteLine("10A=\r\n10B=");
                        w.WriteLine("11A=\r\n11B=");
                        w.WriteLine("12A=\r\n12B=");
                        w.WriteLine("13A\r\n13B=");
                        w.WriteLine("14A=\r\n14B=");
                        w.WriteLine("15A=\r\n15B=\r\n16A=\r\n16B=\r\n17A=\r\n17B=\r\n18A=\r\n18B=\r\n19A=\r\n19B=\r\n20A=\r\n20B=\r\n21A=\r\n21B=\r\n22A=\r\n22B=\r\n23A=\r\n23B=\r\n24A=\r\n24B=\r\n25A=\r\n25B=\r\n");
                        w.Flush();
                    }
                }
            }
        }
    }
}
