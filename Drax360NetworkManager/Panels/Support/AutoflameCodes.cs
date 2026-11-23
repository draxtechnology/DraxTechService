#region usings
using System.Collections.Generic;
#endregion

namespace DraxTechnology.Panels
{
    // Code tables from the Autoflame "Mk8 D.T.I. Set-Up Guide" (04.10.2018),
    // section 9.3 (MM codes) and section 10.1 (the DTI's own errors and
    // warnings). The registers only carry the number; these turn it into the
    // text AMX shows. Mk8 MM column only: the Mini Mk8 MM numbers some codes
    // differently, and the hospital site runs full Mk8 MMs.
    internal static class AutoflameCodes
    {
        // 9.3.2 Lockout codes (register 30830 + 50 per MM).
        public static readonly IReadOnlyDictionary<int, string> Lockouts = new Dictionary<int, string>
        {
            [1] = "CPI Input Wrong State",
            [2] = "No Air Proving",
            [3] = "Ignition Output Fault",
            [4] = "Motor Output Fault",
            [5] = "Start Gas Output Fault",
            [6] = "Main Gas 1 Output Fault",
            [7] = "Main Gas 2 Output Fault",
            [8] = "Vent Valve Output Fault",
            [9] = "Failsafe Relay (Check F1)",
            [10] = "Simulated Flame",
            [11] = "VPS Air Proving Fail",
            [12] = "VPS Gas Proving Fail",
            [13] = "No Flame Signal",
            [14] = "Shutter Fault",
            [15] = "No CPI Reset",
            [16] = "Prolonged Lockout Reset",
            [17] = "Gas Pressure Low",
            [18] = "Gas Pressure High",
            [19] = "RAM Test Failed",
            [20] = "PROM Test Failed",
            [21] = "FSR Test 1A",
            [22] = "FSR Test 2A",
            [23] = "FSR Test 1B",
            [24] = "FSR Test 2B",
            [25] = "Watchdog Fail 2A",
            [26] = "Watchdog Fail 2B",
            [27] = "Watchdog Fail 2C",
            [28] = "Watchdog Fail 2D",
            [29] = "Input Fault",
            [32] = "Gas Pressure Low Limit",
            [33] = "VPS Air Zeroing",
            [36] = "Oil Pressure Too Low",
            [37] = "Oil Pressure Too High",
            [39] = "Freeze Timeout",
            [40] = "Purge Air Pressure Low",
            [42] = "Terminal 86 Inverse",
            [43] = "Terminal 85/86 Fault",
            [44] = "Proving Circuit Fail T52",
            [45] = "No Proving Circuit Set",
            [46] = "Proving Interlock Timeout",
            [52] = "High IR Ambient",
            [53] = "IR Comms Lost",
            [54] = "Watchdog Long X A",
            [55] = "Watchdog Long Y A",
            [56] = "Watchdog Off A",
            [57] = "Watchdog Short X B",
            [58] = "Watchdog Short Y B",
            [59] = "Watchdog Long X B",
            [60] = "Watchdog Long Y B",
            [61] = "Watchdog Off B",
            [62] = "UV Signal Too High",
            [63] = "Purge Limit Switch",
            [64] = "Start Limit Switch",
            [65] = "FSR A",
            [66] = "FSR B",
            [67] = "Gas Sensor Comms",
            [68] = "Gas Sensor Type",
            [69] = "Gas Sensor Fault",
            [70] = "UV Pot Fault",
            [71] = "Air Sensor Comms",
            [72] = "Air Sensor Type",
            [73] = "Air Sensor Fault",
            [74] = "Air Sensor Zero",
            [75] = "Air Sensor Signal High",
            [76] = "Air Sensor Error Window",
            [77] = "Wait Air Switch Timeout",
            [78] = "Gas Proving Fail High",
            [79] = "FSR Test 1C",
            [80] = "Timeout on Reaching Purge",
            [81] = "Oil Pressure Sensor Fault",
            [82] = "Purge Pressure Proving Input",
            [198] = "BC Input Short",
            [199] = "Lockout 199",
            [201] = "Power up CPU Test Fail",
            [202] = "Power up EEPROM Test Fail",
        };

        // 200 is "Lockout Cleared": the MM reports it after a reset, so it
        // means no lockout, not a new one.
        public const int LockoutCleared = 200;

        // 9.3.3 Alarm and warning codes (registers 32012 / 32013 + 100 per MM).
        // One table: whether a fault reports as an alarm or a warning is MM
        // configuration, not the code.
        public static readonly IReadOnlyDictionary<int, string> Alarms = new Dictionary<int, string>
        {
            [1] = "EGA Internal Error",
            [2] = "No EGA Communications",
            [3] = "O2 Upper Limit",
            [4] = "O2 Absolute Limit",
            [5] = "O2 Lower Limit",
            [6] = "CO2 Upper Limit",
            [7] = "CO2 Absolute Limit",
            [8] = "CO2 Lower Limit",
            [9] = "CO Upper Limit",
            [10] = "CO Absolute Limit",
            [11] = "NO Upper Limit",
            [12] = "Exhaust Temperature Upper Limit",
            [13] = "Exhaust Temperature Absolute Limit",
            [50] = "Load Sensor Fault",
            [51] = "Auxiliary Input Low",
            [80] = "Oil Pressure Sensor Fault",
            [100] = "Cap Probe 1 Communications Fault",
            [101] = "Cap Probe 2 Communications Fault",
            [102] = "Cap Probe 1 Short Circuit",
            [103] = "Cap Probe 2 Short Circuit",
            [104] = "Cap Probe 1 Temp Compensation Error",
            [105] = "Cap Probe 2 Temp Compensation Error",
            [106] = "Cap Probe 1 Still Water Detected",
            [107] = "Cap Probe 2 Still Water Detected",
            [108] = "Cap Probe 1 Serial Number Mismatch",
            [109] = "Cap Probe 2 Serial Number Mismatch",
            [110] = "Cap Probe 1 Detected But Not Optioned",
            [111] = "Cap Probe 2 Detected But Not Optioned",
            [112] = "External Level Sensor Input Low",
            [113] = "Probe Reading Mismatch",
            [114] = "Probe Serial Numbers are the Same",
            [120] = "Aux WL Inputs Mismatch",
            [121] = "Water Levels Diverse",
            [122] = "Permanent Alarm Reset Input",
            [123] = "Second Low Probe Communications Fault",
            [124] = "Second Low Probe Hardware Fault",
            [125] = "Permanent Test Input",
            [126] = "Second Low Probe Detected But Not Optioned",
            [127] = "Aux WL Inputs Detect But Not Optioned",
            [130] = "Feed Water Servo Position Error",
            [131] = "Feed Water Servo Movement Error",
            [150] = "High Water",
            [151] = "Pre-High Water",
            [152] = "Pre-1st Low",
            [153] = "1st Low",
            [154] = "2nd Low",
            [155] = "Shunt Switch Time Expired",
            [200] = "Top Blowdown Sensor Communications Fault",
            [201] = "Top Blowdown Servo Position Error",
            [202] = "Top Blowdown Servo Movement Error",
            [250] = "Top Blowdown Reading High",
            [300] = "Bottom Blowdown Controller Comms",
            [301] = "Bottom Blowdown Controller Software Fault",
            [302] = "Bottom Blowdown Servo Closing Fault",
            [303] = "Bottom Blowdown Servo Opening Fault",
            [304] = "Bottom Blowdown Servo Battery Drive Fault",
            [305] = "Bottom Blowdown Controller Main Power Fault",
            [350] = "Bottom Blowdown Servo Not Commissioned",
            [400] = "Draught Pressure Sensor Timeout",
            [410] = "Draught Pressure Outside Tolerance",
            [420] = "Fuel flow Feedback Input Low",
            [430] = "Fuel flow Feedback Below Tolerance",
            [431] = "Fuel flow Feedback Above Tolerance",
            [440] = "Temperature Sensor T1 Fault",
            [441] = "Temperature Sensor T2 Fault",
            [442] = "Temperature Sensor T3 Fault",
            [443] = "Make Up Flow Meter Fault",
            [444] = "Condensate Flow Meter Fault",
            [445] = "Deaerator IO Comms Fault",
            [500] = "Multi-Burner Communications Fault",
            [501] = "Multi-Burner Version Mismatch",
            [502] = "Multi-Burner Not Polled",
            [503] = "Multi-Burner Config (Multi-Burner Mode)",
            [504] = "Multi-Burner Config (Fuel Index)",
            [505] = "Multi-Burner Config (Fuel Type)",
            [506] = "Multi-Burner Config (Pilot Type)",
            [507] = "Multi-Burner Config (Load Sensor)",
            [550] = "Fuel Flow Meter Fault",
            [551] = "Air Flow Meter Fault",
            [552] = "Fuel Temperature Sensor Fault (T2)",
            [553] = "Air Temp Sensor Fault (T3)",
            [554] = "Fuel Pressure Sensor Fault",
            [555] = "Air Pressure Sensor Fault",
            [560] = "Fully Metered Air Adjustment Failure",
            [580] = "Servo Control I/O Unit Communications Fault",
            [581] = "Servo Control I/O Unit Channel 1 Output Fault",
            [582] = "Servo Control I/O Unit Channel 2 Output Fault",
            [583] = "Servo Control I/O Unit Channel 3 Output Fault",
            [584] = "Servo Control I/O Unit Channel 4 Output Fault",
            [585] = "Servo Control I/O Unit Channel 7 Output Fault",
            [586] = "Servo Control I/O Unit Channel 1 Input Fault",
            [587] = "Servo Control I/O Unit Channel 2 Input Fault",
            [588] = "Servo Control I/O Unit Channel 3 Input Fault",
            [589] = "Servo Control I/O Unit Channel 4 Input Fault",
            [590] = "Servo Control I/O Unit Channel 7 Input Fault",
        };

        // 9.3.1 MM error codes (register 30113 + 50 per MM).
        public static readonly IReadOnlyDictionary<int, string> MmErrors = new Dictionary<int, string>
        {
            [1] = "Channel 1 Positioning Error",
            [2] = "Channel 2 Positioning Error",
            [3] = "Channel 3 Positioning Error",
            [4] = "Channel 4 Positioning Error",
            [5] = "Channel 7 Positioning Error",
            [6] = "Channel 1 Gain Error",
            [7] = "Channel 2 Gain Error",
            [8] = "Channel 3 Gain Error",
            [9] = "Channel 4 Gain Error",
            [10] = "Channel 7 Gain Error",
            [11] = "Channel 1 Movement Error",
            [12] = "Channel 2 Movement Error",
            [13] = "Channel 3 Movement Error",
            [14] = "Channel 4 Movement Error",
            [15] = "Channel 7 Movement Error",
            [16] = "Analogue Power Supply Error",
            [17] = "Digital Power Supply Error",
            [18] = "EEPROM Error",
            [19] = "ADC Error",
            [20] = "Watchdog Timeout",
            [21] = "Processor Clock Error",
            [22] = "System Error",
            [23] = "Flash Data Error",
            [24] = "Processor Temperature Error",
            [25] = "Burner Control Comms Error",
            [26] = "Burner Control Reset",
            [28] = "Zero-Crossing Detection Error",
            [29] = "Mains Input Detection Error",
            [30] = "Channel 5 VSD Error",
            [31] = "Channel 6 VSD Error",
            [32] = "VSD Feedback Change Too Small",
            [33] = "Missing Commissioning Data",
            [34] = "FAR Execution Speed",
            [39] = "VSD Sampling Error",
            [40] = "VSD Feedback Too Low",
            [41] = "APS Commission Data Fault",
            [42] = "Comm VPS Gas Pressure Low",
            [43] = "Comm Running Gas Pressure Low",
            [44] = "Comm Air Pressure Low",
            [47] = "Expansion PF Output (Check F5)",
            [48] = "WL Alarm Output Internal Fault",
            [49] = "Expansion Servo Hardware Fault",
            [50] = "Triac Power Supply Error (Check F2)",
            [51] = "Fused 12V Supply Error (Check F4)",
            [52] = "Fused 13.5V Supply Error (Check F3)",
            [53] = "Air Pressure Zeroing Fault",
            [64] = "ADC Reference Voltage Error",
        };

        // 10.1 The DTI's own errors and warnings (registers 30002 / 30005,
        // device ID in 30003 / 30006). "ID" in the guide's text is that device.
        public static string DtiFault(int code, int deviceId)
        {
            switch (code)
            {
                case 1: return "Failed to write to SD card";
                case 2: return "Pressure Sensor " + deviceId + " Reading High";
                case 3: return "Pressure Sensor " + deviceId + " Reading Low";
                case 4: return "Pressure Sensor " + deviceId + " Comms Failure";
                case 5: return "IO Module " + deviceId + " Comms Failure";
                case 6: return "MM " + deviceId + " Comms Failure";
                case 7: return "EGA " + deviceId + " Comms Failure";
                case 8: return "Lead Boiler Rotation Prevented by MM";
                case 9: return "MM " + deviceId + " Not Ready to Become Lead";
                case 10: return "Rule Fault " + deviceId;
                case 11: return "Change of EGA with Id " + deviceId + " detected";
                default: return "DTI Fault " + code;
            }
        }

        // Codes Autoflame list as "Software Error", plus any the guide does
        // not cover, still reach AMX — with the number, so the engineer can
        // look it up.
        public static string Describe(IReadOnlyDictionary<int, string> table, int code, string fallback)
        {
            return table.TryGetValue(code, out string text) ? text : fallback + " " + code;
        }
    }
}
