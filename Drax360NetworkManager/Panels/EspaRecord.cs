using System.Collections.Generic;
using System.Text;

namespace DraxTechnology.Panels
{
    /// <summary>
    /// One ESPA 4.4.4 message frame, decoded into the six standard record codes
    /// plus a catch-all dictionary for non-standard codes. The payload between
    /// STX and ETX is parsed by <see cref="Parse"/>; record separator is RS
    /// (0x1E) and intra-record separator is US (0x1F).
    ///
    /// Standard codes (ESPA 4.4.4):
    ///   1 = Call Address (pager number)
    ///   2 = Display Text (alarm/fault message)
    ///   3 = Beeps
    ///   4 = Call Type    (1 normal, 2 urgent, 3 invitation, ...)
    ///   5 = Transmission Type
    ///   6 = Priority
    /// </summary>
    internal sealed class EspaRecord
    {
        public string CallAddress { get; private set; } = "";
        public string DisplayText { get; private set; } = "";
        public int Beeps { get; private set; }
        public int CallType { get; private set; }
        public int TransmissionType { get; private set; }
        public int Priority { get; private set; }

        public Dictionary<int, string> Raw { get; } = new Dictionary<int, string>();

        public static EspaRecord Parse(byte[] payload)
        {
            // payload is the bytes between STX and ETX (exclusive of both).
            // Layout: code<US>value<RS>code<US>value<RS>...code<US>value
            const byte RS = 0x1E;
            const byte US = 0x1F;

            var rec = new EspaRecord();
            if (payload == null || payload.Length == 0) return rec;

            int i = 0;
            while (i < payload.Length)
            {
                int rsIdx = IndexOf(payload, RS, i);
                int end = rsIdx == -1 ? payload.Length : rsIdx;

                int usIdx = IndexOf(payload, US, i, end);
                if (usIdx == -1)
                {
                    i = end + 1;
                    continue;
                }

                string codeStr = Encoding.ASCII.GetString(payload, i, usIdx - i).Trim();
                string value = Encoding.ASCII.GetString(payload, usIdx + 1, end - (usIdx + 1));

                if (int.TryParse(codeStr, out int code))
                {
                    rec.Raw[code] = value;
                    switch (code)
                    {
                        case 1: rec.CallAddress = value.Trim(); break;
                        case 2: rec.DisplayText = value; break;
                        case 3: int.TryParse(value.Trim(), out int b); rec.Beeps = b; break;
                        case 4: int.TryParse(value.Trim(), out int ct); rec.CallType = ct; break;
                        case 5: int.TryParse(value.Trim(), out int tt); rec.TransmissionType = tt; break;
                        case 6: int.TryParse(value.Trim(), out int pr); rec.Priority = pr; break;
                    }
                }

                i = end + 1;
            }
            return rec;
        }

        private static int IndexOf(byte[] buffer, byte target, int from, int upto = -1)
        {
            int end = upto < 0 ? buffer.Length : upto;
            for (int i = from; i < end; i++)
                if (buffer[i] == target) return i;
            return -1;
        }

        public override string ToString()
        {
            return string.Format(
                "ESPA[addr={0} text=\"{1}\" beeps={2} type={3} trans={4} pri={5}]",
                CallAddress, DisplayText.Trim(), Beeps, CallType, TransmissionType, Priority);
        }
    }
}
