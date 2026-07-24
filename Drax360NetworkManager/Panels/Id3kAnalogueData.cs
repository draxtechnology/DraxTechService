using System.Collections.Generic;

namespace DraxTechnology.Panels
{
    // Extended Device Status decode/build for the Notifier / Pearl / Inspire
    // family, per Honeywell Third-Party Protocol document 099-048 Issue 07.04b,
    // section 3.3.4. Per the document this feature is protocol version 0013 /
    // Pearl panel only (or a licensed gas interface) — older panels may simply
    // not answer the request.
    //
    // Byte positions are the document's 1-based positions counting from the
    // frame's leading '>' (position 1 here is the first character of the
    // message, confirmed by the request layout in 3.3.4.1).
    //
    // Two-digit panel and address fields are "mixed hex/decimal": the tens
    // character runs '0'-'9' then 'A'-'F' (so "A3" = 103), the units character
    // stays decimal. Panels range 0-125, addresses 0-159.

    internal enum Id3kDeviceProtocol
    {
        Clip = 0,
        S200 = 1,
    }

    // One analogue value from the generic block. For CLIP, Index is the
    // pulse-width number (1-5); for S200, Index is the sub-address (0 = the
    // 'System' value).
    internal sealed class Id3kAnalogueReading
    {
        public int Index { get; set; }
        public int Value { get; set; }
    }

    // The "Device Type Dependent Data - Other Types - Analogue Value" block
    // (3.3.4.3.2), bytes 22-55 of the >ISE response. This is the generic
    // layout; device type 17 (gas) uses Id3kExtendedDeviceStatus.GasAnalogueValue
    // instead.
    //
    //   byte  22      device protocol: '0' = CLIP, '1' = S200
    //   CLIP:  bytes 23-47   five 5-digit pulse-width values PW1-PW5 (0-65535)
    //   S200:  bytes 23-25   3-digit 'System' value, sub-address 0 (0-255)
    //          bytes 26-55   3-digit values for sub-addresses 1-10 (0-255);
    //                        only the first N are valid, N = bytes 18-19
    internal sealed class Id3kAnalogueData
    {
        public Id3kDeviceProtocol Protocol { get; private set; }
        public int DeviceType { get; private set; }
        public List<Id3kAnalogueReading> Readings { get; } = new List<Id3kAnalogueReading>();

        private const int MaxSubAddresses = 10;

        public static bool TryParse(string response, out Id3kAnalogueData data)
        {
            data = null;

            if (!Id3kExtendedDeviceStatus.TryField(response, 22, 1, out int protocolDigit)) return false;
            if (protocolDigit != 0 && protocolDigit != 1) return false;

            var parsed = new Id3kAnalogueData
            {
                Protocol = (Id3kDeviceProtocol)protocolDigit,
            };

            if (Id3kExtendedDeviceStatus.TryField(response, 12, 2, out int deviceType))
                parsed.DeviceType = deviceType;

            if (parsed.Protocol == Id3kDeviceProtocol.Clip)
            {
                // PW1-PW5, five consecutive 5-digit fields from byte 23.
                for (int pw = 1; pw <= 5; pw++)
                {
                    if (!Id3kExtendedDeviceStatus.TryField(response, 23 + (pw - 1) * 5, 5, out int value)) return false;
                    parsed.Readings.Add(new Id3kAnalogueReading { Index = pw, Value = value });
                }
            }
            else
            {
                // The 'System' value (sub-address 0) is always present.
                if (!Id3kExtendedDeviceStatus.TryField(response, 23, 3, out int systemValue)) return false;
                parsed.Readings.Add(new Id3kAnalogueReading { Index = 0, Value = systemValue });

                // Only the first N sub-address values are valid, N from bytes 18-19.
                if (!Id3kExtendedDeviceStatus.TryField(response, 18, 2, out int subAddressCount)) subAddressCount = 0;
                if (subAddressCount > MaxSubAddresses) subAddressCount = MaxSubAddresses;

                for (int sub = 1; sub <= subAddressCount; sub++)
                {
                    if (!Id3kExtendedDeviceStatus.TryField(response, 26 + (sub - 1) * 3, 3, out int value)) return false;
                    parsed.Readings.Add(new Id3kAnalogueReading { Index = sub, Value = value });
                }
            }

            data = parsed;
            return true;
        }
    }

    // The full Extended Device Status message (3.3.4.3), ">ISE" + header +
    // device-type-dependent data + quoted text description. Sent asynchronously
    // by the panel once all the requested data has been gathered; note values
    // from networked panels remote from the connected one can be several
    // seconds stale.
    //
    //   bytes 1-4     ">ISE"
    //   bytes 5-6     panel number (mixed hex/dec, "00" = local)
    //   bytes 7-8     loop (2-digit decimal)
    //   byte  9       'S' sensor / 'M' module
    //   bytes 10-11   address (mixed hex/dec, up to 159)
    //   bytes 12-13   device type code programmed at this address, 0-31
    //                 ("00" = none programmed - not necessarily found)
    //   bytes 14-17   device status word, four hex digits (bitmap per 3.3.3.4,
    //                 kept raw here; not meaningful if the device is not
    //                 programmed)
    //   bytes 18-19   number of sub-addresses (00 = normal device)
    //   bytes 20-21   sub-address being reported (00 = whole device, a logical
    //                 OR of all sub-address states)
    //   bytes 22-55   device type dependent data (34 bytes)
    //   byte  56      '"', then text 0-32 chars, then closing '"'
    //   then checksum + <CR> per duplex mode (trailing content is ignored here)
    internal sealed class Id3kExtendedDeviceStatus
    {
        public const int GasSensorTypeCode = 17;

        public int Panel { get; private set; }
        public int Loop { get; private set; }
        public bool IsSensor { get; private set; }
        public int Address { get; private set; }
        public int DeviceTypeCode { get; private set; }
        public string StatusWord { get; private set; } = "";
        public int SubAddressCount { get; private set; }
        public int SubAddress { get; private set; }
        public string Text { get; private set; } = "";

        // Exactly one of these is populated for a decodable device: the generic
        // analogue block, or the gas value for device type 17 (3.3.4.3.1,
        // 5-digit analogue value at bytes 25-29). Both stay empty when the
        // device is unprogrammed or the block is unrecognised - the header and
        // text are still returned.
        public Id3kAnalogueData Analogue { get; private set; }
        public int? GasAnalogueValue { get; private set; }

        public static bool TryParse(string frame, out Id3kExtendedDeviceStatus status)
        {
            status = null;

            // Fixed part runs through byte 56 (the opening quote of the text).
            if (frame == null || frame.Length < 56 || !frame.StartsWith(">ISE")) return false;

            var parsed = new Id3kExtendedDeviceStatus();

            if (!TryMixedField(frame, 5, out int panel)) return false;
            if (!TryField(frame, 7, 2, out int loop)) return false;

            char sensorFlag = frame[9 - 1];
            if (sensorFlag != 'S' && sensorFlag != 'M') return false;

            if (!TryMixedField(frame, 10, out int address)) return false;
            if (!TryField(frame, 12, 2, out int deviceType)) return false;
            if (!TryField(frame, 18, 2, out int subCount)) return false;
            if (!TryField(frame, 20, 2, out int subAddress)) return false;

            parsed.Panel = panel;
            parsed.Loop = loop;
            parsed.IsSensor = sensorFlag == 'S';
            parsed.Address = address;
            parsed.DeviceTypeCode = deviceType;
            parsed.StatusWord = frame.Substring(14 - 1, 4);
            parsed.SubAddressCount = subCount;
            parsed.SubAddress = subAddress;

            if (deviceType == GasSensorTypeCode)
            {
                if (TryField(frame, 25, 5, out int gasValue))
                    parsed.GasAnalogueValue = gasValue;
            }
            else if (Id3kAnalogueData.TryParse(frame, out Id3kAnalogueData analogue))
            {
                parsed.Analogue = analogue;
            }

            // Text sits between the quote at byte 56 and the next quote; the
            // checksum/CR that follow are hex digits so can't false-match.
            if (frame[56 - 1] == '"')
            {
                int close = frame.IndexOf('"', 56);
                if (close > 56) parsed.Text = frame.Substring(56, close - 56);
            }

            status = parsed;
            return true;
        }

        // Builds the Extended Device Status Request body (3.3.4.1): ">IQE" +
        // panel + loop + S/M + address + sub-address, without the checksum/CR
        // tail - that differs per panel wire format, so the caller appends it.
        // Devices at address 100+ follow the family convention of being modules
        // offset by 100 (see PanelNotifier kModuleAddressMin).
        // Sub-address "00" = don't use sub-addressing; the response then reports
        // the whole device.
        public static string BuildStatusRequestBody(int panel, int loop, int device)
        {
            bool isModule = device >= 100;
            int address = isModule ? device - 100 : device;
            return ">IQE"
                + EncodeMixedField(panel)
                + loop.ToString("D2")
                + (isModule ? "M" : "S")
                + EncodeMixedField(address)
                + "00";
        }

        // Extracts a plain ASCII decimal field at a 1-based document byte position.
        internal static bool TryField(string message, int startByte, int length, out int value)
        {
            value = 0;
            int startIndex = startByte - 1;
            if (message == null || startIndex + length > message.Length) return false;
            return int.TryParse(message.Substring(startIndex, length).Trim(), out value);
        }

        // Two-digit mixed hex/decimal field: tens character '0'-'9' then
        // 'A'-'F' (so "A3" = 103), units character decimal.
        private static bool TryMixedField(string message, int startByte, out int value)
        {
            value = 0;
            int i = startByte - 1;
            if (message == null || i + 2 > message.Length) return false;

            char tensCh = message[i];
            char unitsCh = message[i + 1];
            int tens;
            if (tensCh >= '0' && tensCh <= '9') tens = tensCh - '0';
            else if (tensCh >= 'A' && tensCh <= 'F') tens = 10 + (tensCh - 'A');
            else return false;
            if (unitsCh < '0' || unitsCh > '9') return false;

            value = tens * 10 + (unitsCh - '0');
            return true;
        }

        private static string EncodeMixedField(int value)
        {
            int tens = value / 10;
            int units = value % 10;
            char tensCh = tens <= 9 ? (char)('0' + tens) : (char)('A' + tens - 10);
            return "" + tensCh + (char)('0' + units);
        }
    }
}
