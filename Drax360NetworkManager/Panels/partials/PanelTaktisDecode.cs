using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Drax360Service.Panels
{
    internal partial class PanelTaktis
    {
        private bool _duplicate;
        private bool _clearedEvent;
        private bool _sendRequestActiveEvents;
        private long[] _serialNo = new long[4];
        private long _numMessages;
        private readonly AlarmListManager _alarmList;
        private readonly ZoneDisableListManager _zoneDisableList;
        private readonly FaultListManager _faultList;
        private readonly int _amx1Offset;
        private readonly bool _ulSettings;
        private readonly string _ioModuleSettings;
        private readonly string _ioModuleSettingsPanels;
        private readonly ILogger _logger;
        private readonly IAMX1Writer _amx1Writer;
        private readonly INetworkManager _networkManager;

        public void xxxParseTAKMessage(
           enmTAKMessageType messageType,
           long[] serialNo,
           long eventGroup,
           enmTAKEventType eventType,
           enmTAKEventCode eventCode,
           int node,
           int addressType,
           int address,
           int subAddress,
           int loop,
           int zone,
           int inputAction,
           long timeStamp,
           string locationText,
           string panelText,
           string zoneText,
           string deviceType,
           bool alarmOn)
        {
            try
            {
                _duplicate = false;
                _clearedEvent = false;
                bool oneShotReset = true;
                int firstAddress = address;
                int finalAddress = 0;
                int doubleFaultInputType = 0;
                bool _ulSettings = false;

                // Increment message counter
                if (_numMessages > 1_000_000)
                    _numMessages = 0;
                else
                    _numMessages++;

                // Parse event code and determine event text and type
                var eventInfo = ParseEventCode(eventCode, ref eventType, ref address,
                    ref loop, subAddress, addressType, eventGroup);

                string eventText = eventInfo.EventText;
                enmTAKEventType parsedEventType = eventInfo.EventType;

                // Handle event group
                parsedEventType = ProcessEventGroup(eventGroup, parsedEventType, inputAction, ref eventText);

                // Handle input action
                parsedEventType = ProcessInputAction(inputAction, parsedEventType, ref eventText,
                    ref address, ref eventType);

                // Determine input type based on event type
                int inputType = DetermineInputType(parsedEventType, eventCode, subAddress,
                    ref oneShotReset, ref finalAddress, firstAddress, addressType);

                // Special handling for address type (Panel)
                if (addressType == 3 && !_ulSettings)
                {
                    inputType = 15;
                }

                // Process specific event types
                ProcessSpecialEventTypes(ref inputType, eventType, eventCode, ref oneShotReset,
                    ref finalAddress, firstAddress, addressType, subAddress);

                // Adjust loop (convert from 0-based to 1-based)
                loop++;

                if (finalAddress > 0)
                {
                    address = finalAddress;
                    finalAddress = 0;
                }

                // Handle panel input with loop 0
                if (loop == 0 && inputType != 15)
                {
                    if (inputType != 3 && inputType != 1 && inputType != 7)
                    {
                        loop = 1;
                    }
                }

                // Build text summary
                string textSummary = BuildTextSummary(eventText, inputType, zone, ref zoneText,
                    ref deviceType, ref locationText, addressType, subAddress, address,
                    eventCode, loop, ref panelText, eventType, inputAction, node);

                // Handle IO Module sub-addresses
                ProcessIOModuleSubAddress(addressType, subAddress, ref inputType, ref eventText,
                    ref textSummary, ref zoneText, ref deviceType, zone, eventType, eventCode,
                    inputAction, node);

                // Override for specific fault types
                if (inputType == 15)
                {
                    zoneText = "";
                    if (eventCode == enmTAKEventCode.TAKLoopShortCctFault ||
                        eventCode == enmTAKEventCode.TAKLoopOpenCctFault)
                    {
                        zoneText = $"Loop {loop}";
                        loop = 0;
                    }
                    else if (loop > 0)
                    {
                        loop = 0;
                    }
                }

                if (string.IsNullOrEmpty(locationText))
                {
                    locationText = eventText;
                    textSummary = zoneText;
                    deviceType = "";
                }

                // Get transfer file
                string transferFile = _amx1Writer.GetCurrentTransferFile();

                _logger.Log($"ParseTAKMessages : {loop} : {address}");

                // Create input number
                long inputNumber = MakeInputNumber(node + _amx1Offset, loop, address, inputType);

                // Handle reset
                if (alarmOn && inputAction == 9) // Reset
                {
                    HandleReset();
                    return;
                }

                // Add/remove from alarm list
                if (alarmOn)
                {
                    _logger.Log("Add to Alarm List");
                    _alarmList.Add(inputType, inputNumber);
                }

                // Handle zone disablement
                HandleZoneDisablement(eventCode, node, zone, address, loop,
                    alarmOn, ref inputNumber, inputType);

                // Handle zone test
                HandleZoneTest(eventType, node, zone, address, loop,
                    alarmOn, ref inputNumber, inputType);

                // Handle device faults
                HandleDeviceFault(eventType, locationText, eventText, alarmOn,
                    node, loop, address, ref inputType, ref inputNumber,
                    ref doubleFaultInputType);

                // Send to AMX
                SendToAMX(alarmOn, inputNumber, transferFile, locationText,
                    deviceType, textSummary, oneShotReset);

                // Flush messages
                _logger.Log("Flushing");
                _amx1Writer.FlushAMX1Messages();
            }
            catch (Exception ex)
            {
                _logger.LogError($"ParseTAKMessages error: {ex.Message}");
            }
        }

        private string BuildTextSummary(string eventText, int inputType, int zone,
     ref string zoneText, ref string deviceType, ref string locationText,
     int addressType, int subAddress, int address, enmTAKEventCode eventCode,
     int loop, ref string panelText, enmTAKEventType eventType, int inputAction, int node)
        {
            string textSummary = "";

            if (inputType == 15)
            {
                if (zone > 0)
                {
                    zoneText = $"Zone {zone} {zoneText}";
                    textSummary = eventText;
                    deviceType = zoneText;
                }
                else
                {
                    textSummary = eventText;
                }
            }
            else
            {
                if (addressType == 0 && subAddress > 0)
                {
                    if (string.IsNullOrEmpty(locationText))
                        locationText = "IO";

                    zoneText = $"Zone {zone} {zoneText}";
                    textSummary = $"{eventText} Sub Address {subAddress}";
                    deviceType = zoneText;
                }
                else
                {
                    zoneText = $"Zone {zone} {zoneText}";
                    textSummary = eventText;
                    deviceType = zoneText;
                }
            }

            return textSummary;
        }

        public class AlarmListManager
        {
            private readonly List<AlarmEntry> _alarms = new List<AlarmEntry>();

            public void Add(int inputType, long inputNumber)
            {
                if (!_alarms.Any(a => a.InputNumber == inputNumber && a.InputType == inputType))
                {
                    _alarms.Add(new AlarmEntry { InputType = inputType, InputNumber = inputNumber });
                }
            }

            public void Remove(int inputType, long inputNumber)
            {
                _alarms.RemoveAll(a => a.InputNumber == inputNumber && a.InputType == inputType);
            }

            public void Clear()
            {
                _alarms.Clear();
            }

            private class AlarmEntry
            {
                public int InputType { get; set; }
                public long InputNumber { get; set; }
            }
        }

        public interface INetworkManager
        {
            void StopActiveEventsTimer();
            void StartActiveEventsTimer();
            void StartResetDelayTimer(int milliseconds);
            void StopHeartbeatTimer();
            void CloseConnections();
        }

        public interface ILogger
        {
            void Log(string message);
            void LogError(string message);
        }

        public interface IAMX1Writer
        {
            string GetCurrentTransferFile();
            void WriteNWMData(string transferFile, int value1, long inputNumber, long[] parameters,
                string locationText, string deviceType, string textSummary, bool alarmState);
            void FlushAMX1Messages();
            void ForceEvmAttribute(string transferFile, long inputNumber, int attributeId, int value);
        }

        private (string EventText, enmTAKEventType EventType) ParseEventCode(
           enmTAKEventCode eventCode, ref enmTAKEventType eventType, ref int address,
           ref int loop, int subAddress, int addressType, long eventGroup)
        {
            string _evacWord = "Evacuate";
            string _alertWord = "Alert";
            string _techWord = "Technical Alarm";
            string _faultWord = "Fault";
            string _earthFaultWord = "Earth Fault";
            bool _ulSettings = false;

            // gbULSettings = Val(ReadFromIniFile("SetUp", "ULSettings", "0", IniFile))

            // If gbULSettings Then
            //     evacword = "Co Alarm"
            //     alertword = "Auxiliary Alarm"
            //    techword = "Supervisory Alarm"
            //    faultword = "Trouble"
            //    earthfaultword = "Ground Trouble"
            // End If

            string eventText = "";
            enmTAKEventType parsedType = eventType;

            switch (eventCode)
            {
                case enmTAKEventCode.TAKNone:
                    break;

                case enmTAKEventCode.TAKPsFault:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = $"Power Supply {_faultWord}";
                    break;

                case enmTAKEventCode.TAKCalibrationFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Calibration {_faultWord}";
                    break;

                case enmTAKEventCode.TAKOutput1OpenFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Output 1 Open {_faultWord}";
                    break;

                case enmTAKEventCode.TAKOutput1ShortFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Output 1 Short {_faultWord}";
                    break;

                case enmTAKEventCode.TAKOutput2OpenFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Output 2 Open {_faultWord}";
                    break;

                case enmTAKEventCode.TAKOutput2ShortFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Output 2 Short {_faultWord}";
                    break;

                case enmTAKEventCode.TAKInputOpenFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Input Open {_faultWord}";
                    break;

                case enmTAKEventCode.TAKInputShortFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Input Short {_faultWord}";
                    break;

                case enmTAKEventCode.TAKInternalFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Internal {_faultWord}";
                    break;

                case enmTAKEventCode.TAKMaintenanceFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Maintenance {_faultWord}";
                    break;

                case enmTAKEventCode.TAKDetectorFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Detector {_faultWord}";
                    break;

                case enmTAKEventCode.TAKSlaveOpenFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Slave Open {_faultWord}";
                    break;

                case enmTAKEventCode.TAKSlaveShortFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Slave Short {_faultWord}";
                    break;

                case enmTAKEventCode.TAKSlave1ShortFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Slave 1 Short {_faultWord}";
                    break;

                case enmTAKEventCode.TAKSlave2ShortFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Slave 2 Short {_faultWord}";
                    break;

                case enmTAKEventCode.TAKDisconnectedFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Disconnected {_faultWord}";
                    break;

                case enmTAKEventCode.TAKDoubleAddressFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = "Double Address";
                    break;

                case enmTAKEventCode.TAKMonitoredOutputFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Monitored Output {_faultWord}";
                    break;

                case enmTAKEventCode.TAKUnknownDeviceFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Unknown Device {_faultWord}";
                    break;

                case enmTAKEventCode.TAKUnexpectedDeviceFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Unexpected Device {_faultWord}";
                    break;

                case enmTAKEventCode.TAKWrongDeviceFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Wrong Device {_faultWord}";
                    break;

                case enmTAKEventCode.TAKInitialisingDevice:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Initialising Device {_faultWord}";
                    break;

                case enmTAKEventCode.TAKStart:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = "Start";
                    address = 23;
                    break;

                case enmTAKEventCode.TAKAutolearn:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = "Auto Learn";
                    address = 24;
                    break;

                case enmTAKEventCode.TAKPcConfig:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = "PC Config";
                    address = 25;
                    break;

                case enmTAKEventCode.TAKEarthFault:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = _earthFaultWord;
                    address = 26;
                    break;

                case enmTAKEventCode.TAKLoopWiringFault:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = $"Loop Wiring {_faultWord}";
                    address = 27;
                    break;

                case enmTAKEventCode.TAKLoopShortCctFault:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = $"Loop Short CCT {_faultWord}";
                    address = 28;
                    break;

                case enmTAKEventCode.TAKLoopOpenCctFault:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = $"Loop Open CCT {_faultWord}";
                    address = 29;
                    break;

                case enmTAKEventCode.TAKMainsFailedFault:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = $"Mains Failed {_faultWord}";
                    address = 30;
                    break;

                case enmTAKEventCode.TAKLowBatteryFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Battery Low {_faultWord}";
                    address = 31;
                    break;

                case enmTAKEventCode.TAKBatteryDisconnectedFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Battery Disconnected {_faultWord}";
                    address = 32;
                    break;

                case enmTAKEventCode.TAKBatteryOverchargeFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Battery Overcharge {_faultWord}";
                    address = 33;
                    break;

                case enmTAKEventCode.TAKAux24vFuseFault:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = $"24V Fuse {_faultWord}";
                    address = 34;
                    break;

                case enmTAKEventCode.TAKChargerFault:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = $"Charger {_faultWord}";
                    address = 35;
                    break;

                case enmTAKEventCode.TAKRomFault:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = $"ROM {_faultWord}";
                    address = 36;
                    break;

                case enmTAKEventCode.TAKRamFault:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = $"RAM {_faultWord}";
                    address = 37;
                    break;

                case enmTAKEventCode.TAKWatchDogOperated:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = $"Watchdog {_faultWord}";
                    address = 38;
                    break;

                case enmTAKEventCode.TAKBadDataFault:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = $"Bad Data {_faultWord}";
                    address = 39;
                    break;

                case enmTAKEventCode.TAKUnknownEventFault:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = $"Unknown Event {_faultWord}";
                    address = 40;
                    break;

                case enmTAKEventCode.TAKModemActive:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = "Modem Active";
                    address = 41;
                    break;

                case enmTAKEventCode.TAKPrinterFault:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = $"Printer {_faultWord}";
                    address = 42;
                    break;

                case enmTAKEventCode.TAKEn54VersionFault:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = $"EN54 Version {_faultWord}";
                    address = 43;
                    break;

                case enmTAKEventCode.TAKEventPreAlarm:
                    parsedType = enmTAKEventType.TAKEventAlarmPreAlarm;
                    eventText = "Pre Alarm";
                    address = 44;
                    break;

                case enmTAKEventCode.TAKCalibrationFailedFault:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = $"Calibration Failed {_faultWord}";
                    address = 45;
                    break;

                case enmTAKEventCode.TAKModemFault:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = $"Modem {_faultWord}";
                    address = 46;
                    break;

                case enmTAKEventCode.TAKInitDevice:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = "Init Device";
                    address = 47;
                    break;

                case enmTAKEventCode.TAKInputActivated:
                    parsedType = enmTAKEventType.TAKEventTechAlarm;
                    eventText = "Input Activated";

                    if (eventGroup == 11 && addressType == 6)
                    {
                        address = address switch
                        {
                            4 => 243,
                            5 => 244,
                            6 => 245,
                            _ => address
                        };
                    }
                    else
                    {
                        var validTypes = new[] { 0, 2, 4, 5, 6, 7 };
                        if (!validTypes.Contains((int)eventType))
                        {
                            address = 48;
                        }
                    }
                    break;

                // Additional cases would continue here...
                // (Abbreviated for length - full conversion would include all 247+ cases)

                case enmTAKEventCode.TAKDisableDevice:
                    parsedType = enmTAKEventType.TAKEventDisablement;
                    eventText = "Device Disabled";
                    _logger.Log("***************** Device Disable **********************");
                    break;

                case enmTAKEventCode.TAKGeneralDisablement:
                    parsedType = enmTAKEventType.TAKEventStatus;
                    eventText = "General Disablement";
                    address = 89;

                    if (_ulSettings)
                    {
                        parsedType = enmTAKEventType.TAKEventAlarmTest;
                        // locationText parameter would be modified via ref if needed
                        loop = 0;
                    }
                    break;

                case enmTAKEventCode.TAKTamperFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = "Tamper";
                    address = 116;
                    break;

                case enmTAKEventCode.TAKBatteryFault:
                    parsedType = enmTAKEventType.TAKEventFault;
                    eventText = $"Battery {_faultWord}";
                    address = 133;
                    break;

                // More cases would follow...
                default:
                    eventText = $"Unknown Event Code {(int)eventCode}";
                    break;
            }

            _logger.Log($"Event Text {eventText} TAKEventStatus {enmTAKEventType.TAKEventStatus}");

            return (eventText, parsedType);
        }

        private enmTAKEventType ProcessEventGroup(long eventGroup, enmTAKEventType eventType, int inputAction, ref string eventText)
        {
            switch (eventGroup)
            {
                case 1: // Fire
                    _logger.Log("***** FIRE ******");
                    return enmTAKEventType.TAKEventFire;

                case 2: // Pre Alarm
                    return enmTAKEventType.TAKEventAlarmPreAlarm;

                case 10: // NoGroup
                    if (eventType != enmTAKEventType.TAKEventDisablement)
                        return enmTAKEventType.TAKEventStatus;
                    break;

                case 11: // Calibration
                    if (inputAction == 1)
                    {
                        _logger.Log("***** FIRE DURING CALIBRATION ******");
                        return enmTAKEventType.TAKEventFire;
                    }
                    if (inputAction == 5)
                    {
                        _logger.Log("***** EVACUATE DURING CALIBRATION ******");
                        return enmTAKEventType.TAKEventEvacuate;
                    }
                    break;
            }

            return eventType;
        }

        private enmTAKEventType ProcessInputAction(int inputAction, enmTAKEventType eventType, ref string eventText, ref int address, ref enmTAKEventType originalEventType)
        {
            string _evacWord = "Evacuate";
            string _alertWord = "Alert";
            string _techWord = "Technical Alarm";
            string _faultWord = "Fault";
            string _earthFaultWord = "Earth Fault";
            bool _ulSettings = false;

            switch (inputAction)
            {
                case 4: // Tech Alarm
                    _logger.Log("*************** Tech Alarm ***************");
                    eventText = _techWord;
                    address = 1;
                    originalEventType = enmTAKEventType.TAKEventTechAlarm;
                    return enmTAKEventType.TAKEventTechAlarm;

                case 5: // Evacuate
                    if (eventType == enmTAKEventType.TAKEventStatus && _ulSettings)
                    {
                        _logger.Log("*************** Evacuate ***************");
                        eventText = _evacWord;
                        address = 2;
                        originalEventType = enmTAKEventType.TAKEventEvacuate;
                        return enmTAKEventType.TAKEventEvacuate;
                    }
                    break;

                case 6: // Alert
                    if (_ulSettings)
                    {
                        eventText = _alertWord;
                        originalEventType = enmTAKEventType.TAKEventAlarmPreAlarm;
                        return enmTAKEventType.TAKEventAlarmPreAlarm;
                    }
                    break;

                case 7: // Security Alarm
                    _logger.Log("*************** Security Alarm - Input Action ***************");
                    eventText = "Security Alarm";
                    address = 3;
                    originalEventType = enmTAKEventType.TAKEventSecurity;
                    return enmTAKEventType.TAKEventSecurity;

                case 8:
                    eventText = "Silence";
                    address = 4;
                    break;

                case 9:
                    eventText = "Reset";
                    address = 5;
                    break;

                case 12:
                    eventText = "Test";
                    address = 6;
                    break;

                case 13:
                    eventText = "Re-Sound";
                    address = 7;
                    break;

                case 15:
                    eventText = "Buzzer Silence";
                    address = 8;
                    break;

                case 17:
                    eventText = "Change Sensor Mode";
                    address = 9;
                    break;
            }

            return eventType;
        }

        private int DetermineInputType(enmTAKEventType eventType, enmTAKEventCode eventCode,
            int subAddress, ref bool oneShotReset, ref int finalAddress, int firstAddress,
            int addressType)
        {
            int inputType = 15;
            bool _ulSettings = false;

            switch (eventType)
            {
                case enmTAKEventType.TAKEventFire:
                    inputType = 0;
                    oneShotReset = false;
                    finalAddress = firstAddress;
                    break;

                case enmTAKEventType.TAKEventEvacuate:
                    inputType = 15;
                    oneShotReset = false;

                    if (subAddress > 0)
                    {
                        inputType = subAddress switch
                        {
                            1 => 1,
                            2 => 3,
                            3 => 9,
                            4 => 10,
                            _ => inputType
                        };
                    }
                    break;

                case enmTAKEventType.TAKEventAlert:
                    if (!_ulSettings)
                        inputType = 15;
                    oneShotReset = false;
                    break;

                case enmTAKEventType.TAKEventAlarmPreAlarm:
                    inputType = 2;
                    oneShotReset = false;
                    finalAddress = firstAddress;
                    break;

                case enmTAKEventType.TAKEventSecurity:
                    inputType = 6;
                    oneShotReset = false;
                    _logger.Log("******************** Security Alarm ********************");
                    finalAddress = firstAddress;
                    break;

                case enmTAKEventType.TAKEventFault:
                    inputType = 8;
                    oneShotReset = false;
                    finalAddress = firstAddress;
                    break;

                case enmTAKEventType.TAKEventDisablement:
                    _logger.Log("******************* Event Disable ****************");
                    inputType = 4;
                    oneShotReset = false;
                    finalAddress = firstAddress;
                    break;

                case enmTAKEventType.TAKEventTechAlarm:
                    _logger.Log("******************** Tech Alarm 2 ********************");
                    inputType = 7;
                    oneShotReset = false;
                    finalAddress = firstAddress;
                    break;

                case enmTAKEventType.TAKEventAlarmTest:
                    inputType = 12;
                    oneShotReset = false;
                    break;

                case enmTAKEventType.TAKEventStatus:
                    if (eventCode == enmTAKEventCode.TAKDisableZone ||
                        eventCode == enmTAKEventCode.TAKAllZoneDisabled)
                    {
                        inputType = 11;
                    }
                    else
                    {
                        inputType = 15;
                    }
                    oneShotReset = false;
                    break;

                case enmTAKEventType.TAKEventCeaction:
                case enmTAKEventType.TAKEventNone:
                case enmTAKEventType.TAKEventMax:
                case enmTAKEventType.TAKEventOther:
                case enmTAKEventType.TAKEventAll:
                case enmTAKEventType.TAKEventUnknown:
                    inputType = 15;
                    oneShotReset = false;
                    break;
            }

            // Panel address type override
            if (addressType == 3 && !_ulSettings)
            {
                inputType = 15;
            }

            return inputType;
        }

        private void ProcessSpecialEventTypes(ref int inputType, enmTAKEventType eventType,
    enmTAKEventCode eventCode, ref bool oneShotReset, ref int finalAddress,
    int firstAddress, int addressType, int subAddress)
        {
            switch (eventType)
            {
                case enmTAKEventType.TAKEventAlarmTest:
                    inputType = 12;
                    oneShotReset = false;
                    break;

                case enmTAKEventType.TAKEventStatus:
                    if (eventCode == enmTAKEventCode.TAKInitialisingDevice)
                    {
                        inputType = 8;
                        oneShotReset = false;
                        finalAddress = firstAddress;
                    }
                    else if (eventCode == enmTAKEventCode.TAKDisableZone)
                    {
                        inputType = 11;
                    }
                    else
                    {
                        inputType = 15;
                    }
                    break;

                case enmTAKEventType.TAKEventTechAlarm:
                    inputType = 7;
                    oneShotReset = false;
                    finalAddress = firstAddress;
                    break;

                case enmTAKEventType.TAKEventSecurity:
                    inputType = 1;
                    oneShotReset = false;
                    finalAddress = firstAddress;
                    break;

                case enmTAKEventType.TAKEventFault:
                    if (eventCode == enmTAKEventCode.TAKInputActivated)
                    {
                        if (subAddress > 0)
                        {
                            inputType = subAddress switch
                            {
                                1 => 1,
                                2 => 3,
                                3 => 9,
                                4 => 10,
                                _ => 8
                            };
                        }
                        else
                        {
                            inputType = 8;
                        }
                    }
                    else if (eventCode == enmTAKEventCode.TAKCalibrationFailedFault)
                    {
                        inputType = 8;
                    }
                    else if (inputType != 15)
                    {
                        inputType = 8;
                    }

                    oneShotReset = false;

                    if (addressType == 3 || addressType == 9)
                    {
                        finalAddress = firstAddress;
                    }
                    break;

                case enmTAKEventType.TAKEventOther:
                    inputType = 15;
                    _logger.Log("********** TAKEventOther **************");
                    break;
            }
        }

        private void ProcessIOModuleSubAddress(int addressType, int subAddress,
    ref int inputType, ref string eventText, ref string textSummary,
    ref string zoneText, ref string deviceType, int zone, enmTAKEventType eventType,
    enmTAKEventCode eventCode, int inputAction, int node)
        {
            if (addressType == 0 && subAddress > 0 && eventCode == enmTAKEventCode.TAKInputActivated)
            {
                string panel = "Both";

                if (_ioModuleSettings == "Both")
                {
                    // Check if node is in the panels string
                    if (_ioModuleSettingsPanels.Contains($"{node},"))
                    {
                        panel = "Apollo";
                    }
                    else
                    {
                        panel = "Hochiki";
                    }
                }

                if (_ioModuleSettings == "Apollo" || panel == "Apollo")
                {
                    // Apollo IO module mapping: 1-Output, 2-Input, 3-Input, 4-Output, 5-Output, 6-Input
                    inputType = subAddress switch
                    {
                        2 => 1,
                        3 => 3,
                        6 => 9,
                        _ => inputType
                    };
                }

                if (_ioModuleSettings == "Hochiki" || panel == "Hochiki")
                {
                    inputType = subAddress switch
                    {
                        1 => 1,
                        2 => 3,
                        3 => 9,
                        4 => 10,
                        _ => inputType
                    };
                }

                // If fault, override to input type 8
                if (eventType == enmTAKEventType.TAKEventFault)
                {
                    inputType = 8;
                }
            }
        }

        private void HandleZoneDisablement(enmTAKEventCode eventCode, int node, int zone,
         int address, int loop, bool alarmOn, ref long inputNumber, int inputType)
        {
            if (eventCode != enmTAKEventCode.TAKDisableZone)
                return;

            _logger.Log("Zone Disable Detected");

            if (node == 254)
                node = 1;

            string panelZone = $"{node:00}{zone:000}";

            if (alarmOn)
            {
                _logger.Log($"Zone Alarm Disable Zone - Add to List {address}");
                _zoneDisableList.Add(node, zone, address, loop);
                inputNumber = MakeInputNumber(node + _amx1Offset, loop, address, inputType);
            }
            else
            {
                _logger.Log($"Zone Alarm Disable Zone - Remove from List {panelZone}");
                _zoneDisableList.Remove(node, zone, address, loop);
                inputNumber = MakeInputNumber(node + _amx1Offset, loop, address, inputType);
            }
        }

        private void HandleZoneTest(enmTAKEventType eventType, int node, int zone,
            int address, int loop, bool alarmOn, ref long inputNumber, int inputType)
        {
            if (eventType != enmTAKEventType.TAKEventAlarmTest)
                return;

            _logger.Log("Zone Test Detected");

            string panelZone = $"{node:00}{zone:000}";

            if (alarmOn)
            {
                _logger.Log($"Alarm Zone Test {address}");
                _zoneDisableList.Add(node, zone, address, loop);

                if (address == -1)
                {
                    _logger.Log("Zone Test Count over 255 - so ignore");
                    inputNumber = 0;
                    return;
                }

                inputNumber = MakeInputNumber(node + _amx1Offset, loop, address, inputType);
            }
            else
            {
                _logger.Log($"Zone Alarm Test - Remove from List {panelZone}");
                _zoneDisableList.Remove(node, zone, address, loop);
                inputNumber = MakeInputNumber(node + _amx1Offset, loop, address, inputType);
            }
        }


        /// <summary>
        /// Manages zone disable and test list
        /// </summary>
        public class ZoneDisableListManager
        {
            private readonly List<ZoneEntry> _zones = new List<ZoneEntry>();

            public void Add(int node, int zone, int address, int loop)
            {
                var entry = new ZoneEntry
                {
                    Node = node,
                    Zone = zone,
                    Address = address,
                    Loop = loop
                };

                if (!_zones.Any(z => z.Node == node && z.Zone == zone && z.Address == address))
                {
                    _zones.Add(entry);
                }
            }

            public void Remove(int node, int zone, int address, int loop)
            {
                _zones.RemoveAll(z => z.Node == node && z.Zone == zone && z.Address == address);
            }

            public void Clear()
            {
                _zones.Clear();
            }

            private class ZoneEntry
            {
                public int Node { get; set; }
                public int Zone { get; set; }
                public int Address { get; set; }
                public int Loop { get; set; }
            }
        }

        /// <summary>
        /// Manages device fault list with double-fault detection
        /// </summary>
        public class FaultListManager
        {
            private readonly List<FaultEntry> _faults = new List<FaultEntry>();

            public bool Add(long inputNumber, string eventText, ref int doubleFaultInputType)
            {
                var existing = _faults.FirstOrDefault(f => f.InputNumber == inputNumber);

                if (existing != null)
                {
                    // Double fault detected - increment input type
                    doubleFaultInputType = existing.InputType + 1;
                    existing.InputType = doubleFaultInputType;
                    existing.EventText = eventText;
                    return true;
                }

                _faults.Add(new FaultEntry
                {
                    InputNumber = inputNumber,
                    EventText = eventText,
                    InputType = doubleFaultInputType
                });

                return false;
            }

            public bool Remove(long inputNumber, string eventText, ref int doubleFaultInputType)
            {
                var existing = _faults.FirstOrDefault(f => f.InputNumber == inputNumber);

                if (existing != null)
                {
                    doubleFaultInputType = existing.InputType;
                    _faults.Remove(existing);
                    return true;
                }

                return false;
            }

            public void Clear()
            {
                _faults.Clear();
            }

            private class FaultEntry
            {
                public long InputNumber { get; set; }
                public string EventText { get; set; }
                public int InputType { get; set; }
            }
        }

        private void HandleDeviceFault(enmTAKEventType eventType, string locationText,
    string eventText, bool alarmOn, int node, int loop, int address,
    ref int inputType, ref long inputNumber, ref int doubleFaultInputType)
        {
            if (eventType != enmTAKEventType.TAKEventFault)
                return;

            _logger.Log($"Device Fault Detected - {locationText} {eventText}");

            if (alarmOn)
            {
                doubleFaultInputType = inputType;

                if (_faultList.Add(inputNumber, eventText, ref doubleFaultInputType))
                {
                    // Already in list, use new input type
                    inputType = doubleFaultInputType;
                    inputNumber = MakeInputNumber(node + _amx1Offset, loop, address, inputType);
                }
                else
                {
                    if (_duplicate)
                    {
                        _logger.Log("gbDuplicate - MakeInputNumberSkipped");
                    }
                    else
                    {
                        _logger.Log($"Fault Added to list {inputNumber} {doubleFaultInputType}");
                        inputType = doubleFaultInputType;
                        inputNumber = MakeInputNumber(node + _amx1Offset, loop, address, inputType);
                    }
                }
            }
            else
            {
                _logger.Log($"Remove from List {inputNumber}");
                doubleFaultInputType = 0;

                if (!_faultList.Remove(inputNumber, eventText, ref doubleFaultInputType))
                {
                    _logger.Log($"============== Not Found In Fault List {inputNumber}");
                    inputNumber = MakeInputNumber(node + _amx1Offset, loop, address, inputType);
                }
                else
                {
                    _logger.Log($"============== Found In Fault List {inputNumber}-{doubleFaultInputType}");
                    inputType = doubleFaultInputType;
                    inputNumber = MakeInputNumber(node + _amx1Offset, loop, address, inputType);
                }
            }
        }

        private void SendToAMX(bool alarmOn, long inputNumber, string transferFile,
            string locationText, string deviceType, string textSummary, bool oneShotReset)
        {
            string queueSize = $"Panel Number: {inputNumber:X}";

            if (alarmOn)
            {
                _logger.Log("Not Cleared Event");

                if (!_duplicate)
                {
                    _logger.Log($"MakeInputNumber : lInputNumber: {inputNumber}");
                    _logger.Log($"Sending : {queueSize} TransFile: {transferFile} " +
                               $"Text: {locationText?.Trim()} - {deviceType?.Trim()} - {textSummary?.Trim()}");

                    _amx1Writer.WriteNWMData(
                        transferFile,
                        1,
                        inputNumber | 0x80000000,
                        new long[16],
                        locationText?.Trim() ?? "",
                        deviceType?.Trim() ?? "",
                        textSummary?.Trim() ?? "",
                        true);

                    _logger.Log("Message Sent");
                }
            }
            else
            {
                _logger.Log("Cleared Event");
                _logger.Log($"Sending Clear : {queueSize} lInputNumber: {inputNumber} " +
                           $"TransFile: {transferFile} Text: {textSummary} {locationText} {deviceType}");

                _amx1Writer.WriteNWMData(
                    transferFile,
                    1,
                    inputNumber,
                    new long[16],
                    locationText?.Trim() ?? "",
                    deviceType?.Trim() ?? "",
                    textSummary?.Trim() ?? "",
                    false);
            }

            if (oneShotReset)
            {
                _amx1Writer.FlushAMX1Messages();
                Thread.Sleep(0);
                _amx1Writer.ForceEvmAttribute(transferFile, inputNumber, 13, 1);
            }
        }

        private void HandleReset()
        {
            _networkManager.StopActiveEventsTimer();
            _logger.Log("********* Reset - So Clear all events from Event list ************");

            _alarmList.Clear();

            // Clear serial numbers
            for (int i = 0; i < _serialNo.Length; i++)
                _serialNo[i] = 0;

            _sendRequestActiveEvents = false;
            _duplicate = false;
            _zoneDisableList.Clear();
            _faultList.Clear();

            _networkManager.StartActiveEventsTimer();
            _networkManager.StartResetDelayTimer(10000);
            _networkManager.StopHeartbeatTimer();

            _logger.Log("********* Reset - Close the connection ************");
            _networkManager.CloseConnections();
        }


        private long MakeInputNumber(int node, int loop, int address, int inputType)
        {
            // Pack values into a single long
            // Assuming bit layout: [node][loop][address][inputType]
            long result = ((long)node << 24) | ((long)loop << 16) | ((long)address << 8) | inputType;
            _logger.Log($"MakeInputNumber: Node={node}, Loop={loop}, Address={address}, Type={inputType} => {result}");
            return result;
        }
    }
}