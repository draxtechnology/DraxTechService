using System;

namespace DraxTechnology.Panels
{
    /// <summary>
    /// Device-type and status-text lookups for the RSM panel family.
    /// Ports the three lookup tables from the VB6 source:
    ///   - GetDeviceType / dxdevtypes (RSMNetManagerSubs.bas)
    ///   - GetZitonDeviceType (RSMNetManagerSubs.bas)
    ///   - status-15 text overrides from ParseRSMMessages (RSMNetManager.bas)
    /// All values are direct ports of the VB strings — do not paraphrase, AMX
    /// is potentially keying on the exact text.
    /// </summary>
    internal static class RsmLookups
    {
        /// <summary>
        /// Maps a device-type code to a human-readable name, with module-specific
        /// overrides for codes >= 200. Returns "" when no mapping exists.
        /// </summary>
        public static string GetDeviceType(int devType, string moduleType)
        {
            if (devType < 200)
            {
                switch (devType)
                {
                    case 0:  return "";                            // dvtUNKNOWN
                    case 1:  return "Shop Unit";                   // dvtSHOPUNIT
                    case 2:  return "Sounder";                     // dvtSOUNDER
                    case 3:  return "I/O Unit";                    // dvtIOUNIT
                    case 4:  return "Ionisation Detector";         // dvtIONDET
                    case 5:  return "Zone Monitor";                // dvtZONEMON
                    case 6:  return "Optical Detector";            // dvtOPTDET
                    case 7:  return "Heat Detector";               // dvtHEATDET
                    case 8:  return "Manual Call Point";           // dvtMCP
                    case 9:  return "Relay";                       // dvtRELAY
                    case 10: return "Non-Specified Sensor";        // dvtNONSPECSENSOR
                    case 11: return "8-way Input";                 // dvt8WAYIP
                    case 12: return "Mini Repeater";               // dvtMINIREP
                    case 13: return "Multi-Detector";              // dvtMULTIDET
                    case 14: return "Network Fault";               // dvtNETWORKFAULT
                    case 15: return "Network node missing";        // dvtNODEMISSING
                    case 16: return "FT Network Fault";            // dvtFTNETWORKFAULT
                    case 17: return "Temperature Sensor";          // dvtTEMPSENSOR
                    case 18: return "Volts";                       // dvtVOLTS
                    case 19: return "Current";                     // dvtCURRENT
                    case 20: return "Switch";                      // dvtSWITCH
                    case 21: return "Carbon Monixide Detector";    // dvtCO  (sic — VB typo preserved)
                    case 22: return "Flame Detector";              // dvtFLAMEDET
                    case 23: return "Beacon";                      // dvtBEACON
                    case 24: return "Rate-Of-Rise Heat Detector";  // dvtRORHEAT
                    case 25: return "Input";                       // dvtINPUT
                    default: return "";
                }
            }

            // codes >= 200 are module-type-specific
            switch (moduleType)
            {
                case "DX":
                    switch (devType)
                    {
                        case 201: return "General Control Output";
                        case 202: return "General Monitored Input";
                        case 203: return "Sprinkler System Monitor";
                        case 204: return "View Sensor";
                        case 205: return "Convential Zone Monitor CDI";  // VB spelling preserved
                        case 206: return "Sounder Output";
                        case 207: return "Auxiliary Module";
                        case 208: return "Conventional Zone Monitor ZMX/M512";
                        case 209: return "Advanced Multi Sensor";
                        case 210: return "Gas Sensor Interface";
                        case 211: return "Loop Booster Module";
                        case 212: return "SMART 4 Sensor";
                        case 213: return "SMART 3 Sensor";
                        case 214: return "Unmonitored Relay Output(RLY)";
                        case 215: return "VIEW Reference Sensor(AVR)";
                        case 216: return "Extinguishing System Monitor(ESM)";
                        case 217: return "Extinguishing System Trigger Output(EST)";
                        case 218: return "Manual Call Point (VDS Monitoring) (DKM)";
                        case 219: return "Laser Detector (LSR)";
                        case 220: return "I/O Module (IO)";
                        case 221: return "Carbon Monoxide Montioring Sensor(CO)";  // VB typo preserved
                        case 222: return "Flame Sensor (FLM)";
                        case 223: return "Multimode Heat Sensor(MMH)";
                        default:  return "";
                    }

                case "GE":
                    switch (devType)
                    {
                        case 201: return "Alarms Silenced";
                        case 202: return "Alarms Sounded";
                        case 203: return "Cancel Buzzer";
                        case 204: return "Card/Loop Disablement";
                        case 205: return "Zone Disablement";
                        case 206: return "Card/Loop Disablement Loop No 1";
                        case 207: return "Card/Loop Disablement Loop No 2";
                        case 208: return "Card/Loop Disablement Loop No 3";
                        case 209: return "Card/Loop Disablement Loop No 4";
                        case 210: return "Card/Loop Disablement Loop No 5";
                        case 211: return "Card/Loop Disablement Loop No 6";
                        case 212: return "Card/Loop Disablement Loop No 7";
                        case 213: return "Card/Loop Disablement Loop No 8";
                        case 214: return "Card/Loop Disablement Loop No 9";
                        case 215: return "Card/Loop Disablement Loop No 10";
                        case 216: return "Card/Loop Disablement Loop No 11";
                        case 217: return "Card/Loop Disablement Loop No 12";
                        case 218: return "Card/Loop Disablement Loop No 13";
                        case 219: return "Card/Loop Disablement Loop No 14";
                        case 220: return "Card/Loop Disablement Loop No 15";
                        case 221: return "Card/Loop Disablement Loop No 16";
                        case 222: return "Device Disabled";
                        case 223: return "Disablement Cleared";
                        case 224: return "Supervisory OFF";
                        case 225: return "Supervisory ON";
                        default:  return "";
                    }

                case "CO":
                    switch (devType)
                    {
                        case 201: return "Optical and G Heat";
                        case 202: return "Heat BS";
                        case 203: return "Heat CS";
                        case 204: return "Heat A1R";
                        case 205: return "Optical Heat";
                        case 206: return "Sounder Control Unit";
                        case 207: return "Voice";
                        case 208: return "Repeater";
                        case 209: return "Beam";
                        case 210: return "Filtrex";
                        case 211: return "Access Control";
                        case 212: return "Emergency Light Mod";
                        case 213: return "4~20mA";
                        default:  return "";
                    }

                default:
                    return "";
            }
        }

        /// <summary>
        /// Ziton-specific lookup. When inputType != 15 returns a device-name string
        /// keyed on the hex-coded device type. When inputType == 15 returns a
        /// status/fault description keyed on statusCode (Extension2 in the EVT).
        /// </summary>
        public static string GetZitonDeviceType(int devType, int inputType, int statusCode)
        {
            if (inputType != 15)
            {
                switch (devType)
                {
                    case 0x00: return "";
                    case 0x28: return "Ionization";
                    case 0x2C: return "Heat Fixed Temperature";
                    case 0x30: return "Heat Rate of Rise";
                    case 0x34: return "Optical Smoke";
                    case 0x38: return "Dual Optic/Heat";
                    case 0x3A: return "Paradigm Multi-sensor";
                    case 0x46: return "Radio Base";
                    case 0x48: return "Call-point";
                    case 0x4A: return "Radio Optic/Heat";
                    case 0x4C: return "Interface Sprinkler";
                    case 0x4D: return "Radio Heat";
                    case 0x50: return "Interface General";
                    case 0x52: return "Radio Call-point";
                    case 0x56: return "Radio Optic";
                    case 0x5A: return "Radio Aux. Interface";
                    case 0x5E: return "Radio I/O Unit";
                    case 0x68: return "Interface Conventional";
                    case 0x6C: return "High Sensitivity Aspirating Smoke Detector";
                    case 0x74: return "Interface Conventional";
                    case 0x7E: return "Radio I/O Group";
                    case 0x84: return "ZP755 Line Sounder";
                    case 0x88: return "Addressable Relay";
                    case 0x90: return "Gas Control Unit";
                    case 0x94: return "Sounder Driver";
                    case 0x9C: return "ZP754 Line Sounder";
                    case 0xA8: return "Interface Non Fire";
                    case 0xAC: return "Interface Control Switch";
                    case 0xB4: return "Addressable LED";
                    case 0xB5: return "Ionization (Ex)";
                    case 0xB9: return "Heat (Ex) 6-56 ZP720Ex";
                    case 0xC8: return "Security Interface Latching";
                    case 0xD0: return "Security I/F Non-Latch";
                    case 0xD5: return "Call-point (Ex)";
                    case 0xD9: return "Interface Fire (Ex)";
                    case 0xDD: return "Interface Non-Fire";
                    default:   return "";
                }
            }

            // inputType == 15 — Ziton status/fault codes
            switch (statusCode)
            {
                case 14:  return "Loop Fault";
                case 15:  return "Earth Leakage";
                case 16:  return "Alarm Fault";
                case 17:  return "Manual Call-point Loop";
                case 18:  return "Halon Detonator Loop";
                case 19:  return "Halon Bell Fault";
                case 20:  return "T-Bar Recommended";
                case 21:  return "No Sensors";
                case 22:  return "External Line Fault";
                case 23:  return "Fire Station Fault";
                case 24:  return "RAM Fault";
                case 25:  return "Power Fault";
                case 26:  return "Battery Fault";
                case 27:  return "Charger Fault";
                case 28:  return "RAM Backup Battery Voltage Low";
                case 29:  return "Panel Offline";
                case 30:  return "Panel Online";
                case 31:  return "Zone Disabled";
                case 33:  return "Bell Fault";
                case 36:  return "Panel Reset";
                case 37:  return "Panel Alarms Accepted";
                case 38:  return "Panel Sound Alarms";
                case 39:  return "Printer Options Set";
                case 44:  return "Alarm / Trigger";
                case 54:  return "Panel Data Accessed/Changed";
                case 57:  return "Triggered Pulsed Output";
                case 58:  return "Normal Pulsed Output";
                case 60:  return "RDU Offline";
                case 61:  return "RDU Alarm Fault";
                case 62:  return "RDU Battery Fault";
                case 63:  return "RDU Mains Fault";
                case 66:  return "Memory R/W fault";
                case 67:  return "Checksum Fault";
                case 78:  return "Stack Fault";
                case 79:  return "Ext. Computer Offline";
                case 80:  return "Faulty Z-Input";
                case 81:  return "Input On";
                case 83:  return "Offline Board";
                case 85:  return "System Fault AGV";
                case 86:  return "Dual Monitored Loop";
                case 87:  return "Output Cancelled";
                case 91:  return "Text Area Full";
                case 92:  return "Security Switch Activated";
                case 95:  return "Sounders Silenced";
                case 97:  return "System Fault Restored";
                case 98:  return "EPROM Changed";
                case 99:  return "I/O Disabled";
                case 100: return "I/O Enabled";
                case 101: return "Comms 2 Ext. Computer Responding";
                case 102: return "Addressable Comms Board Offline";
                case 103: return "Addressable Comms Board Online";
                case 104: return "Night Mode";
                case 105: return "Day Mode";
                case 106: return "Standard Mode";
                case 110: return "Output Triggered";
                case 111: return "Watchdog";
                case 112: return "Menu Access Security Code";
                case 113: return "EEPROM Write Fault";
                case 114: return "Sounder Timeout";
                case 117: return "Master No Slaves Online";
                case 118: return "Slave: Address Group Fault";
                case 120: return "Silence Disabled";
                case 121: return "Sounders Disabled";
                case 122: return "Comms Link Down/Modem No Carrier";
                case 123: return "Comms Link Up";
                case 124: return "Common Disable";
                case 125: return "Fire-station Disable";
                case 126: return "GCU Disable";
                case 127: return "General (common) de-isolate/enable";
                case 128: return "Device tamper fault";
                case 129: return "Device battery fault";
                case 130: return "Processor fault: LD-PIC, etc.";
                case 131: return "Double-address fault";
                default:  return "Unknown panel status event";
            }
        }

        /// <summary>
        /// Status-15 device-text overrides from ParseRSMMessages, applied when
        /// LoopNum == 0 and InputType == 15. Returns null when no override
        /// applies (caller keeps the existing device text).
        /// AD module type uses a different address space than the others;
        /// addresses 252 / 253 are common to both with engineer / startup semantics.
        /// </summary>
        public static string GetStatusText(string moduleType, int address, int onOff)
        {
            if (moduleType == "AD")
            {
                switch (address)
                {
                    case 252: return onOff != 0 ? "Engineer Present" : "Engineer No Longer Present";
                    case 253: return "RSM Module Startup";
                    default:  return null;
                }
            }

            // all other module types
            switch (address)
            {
                case 1:   return "Internal Buzzer Muted";
                case 2:   return "Alarms Silenced";
                case 3:   return "General Disablement";
                case 4:   return "Panel in Fire";
                case 5:   return "Panel in Fault";
                case 6:   return "Panel in Pre-alarm";
                case 7:   return "Panel in Test Mode";
                case 8:   return "Panel in Delay Mode Period";
                case 9:   return "Master Panel RS232 Comms Lost";
                case 10:  return "RSM Module Startup";
                case 252: return onOff != 0 ? "Engineer Present" : "Engineer No Longer Present";
                default:  return null;
            }
        }

        /// <summary>
        /// True for status-15 "RSM Module Startup" rows where the VB also
        /// shifts the current deviceText into deviceType (typically the
        /// firmware version string the panel is reporting on restart).
        /// </summary>
        public static bool IsModuleRestart(string moduleType, int address)
        {
            if (moduleType == "AD") return address == 253;
            return address == 10;
        }
    }
}
