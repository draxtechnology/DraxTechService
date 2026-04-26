using System;
using System.IO;
using System.Text;

namespace DraxTechnology
{
    /// <summary>
    /// Decoded form of an AMX → service Manual Control message (.MTN file).
    /// The wire layout matches NVM.RenderBytes() byte-for-byte (224 bytes total).
    /// </summary>
    internal sealed class MtnRecord
    {
        public string SourceFile;

        // Header (8 bytes)
        public NwmData OurType;        // 0x00 — action category (e.g. EvacuateToNwm = 12)
        public int OurEvent;           // 0x04 — packed event id from MakeInputNumber

        // 16-bit fields (12 bytes)
        public short On;               // 0x08 — 0=off, !=0=on. AMX uses -1 for momentary/edge.
        public short Value;            // 0x0A
        public short Node;             // 0x0C
        public short Zone;             // 0x0E
        public short Op;               // 0x10
        public short ControlType;      // 0x12

        // 32-bit data fields (24 bytes)
        public int Dat1;               // 0x14
        public int Dat2;               // 0x18
        public int Dat3;               // 0x1C
        public int Dat4;               // 0x20
        public int Dat5;               // 0x24
        public int Dat6;               // 0x28

        public uint Time;              // 0x2C — Unix seconds (local epoch in observed traffic)
        public DateTime Timestamp { get { return DateTimeOffset.FromUnixTimeSeconds(Time).UtcDateTime; } }

        public int[] Spare = new int[8]; // 0x30..0x4F

        // ASCII text fields, null-padded
        public string Text;            // 0x50, 64 bytes
        public string Text2;           // 0x90, 40 bytes
        public string Text3;           // 0xB8, 40 bytes
                                       // total = 0xE0 = 224 bytes

        // OurEvent is the output of CSAMXSingleton.MakeInputNumber:
        //   no = inputn + (loop << 8) + (node << 16) + (inputtype << 27)
        //   if (on) no |= 0x80000000
        public bool EventOn { get { return (OurEvent & unchecked((int)0x80000000)) != 0; } }
        public int EventAddr { get { return OurEvent & 0xFF; } }
        public int EventLoop { get { return (OurEvent >> 8) & 0xFF; } }
        public int EventNode { get { return (OurEvent >> 16) & 0x7FF; } }
        public int EventInputType { get { return ((OurEvent & 0x7FFFFFFF) >> 27) & 0xF; } }

        /// <summary>
        /// CSV form expected by AbstractPanel command methods (Evacuate/Silence/etc.):
        /// "node,loop,zone,device".
        /// </summary>
        public string AsPassedValues()
        {
            return string.Format("{0},{1},{2},{3}", EventNode, EventLoop, 0, EventAddr);
        }

        public override string ToString()
        {
            return string.Format(
                "MTN {0} type={1}({2}) event=0x{3:X8} (node={4} L{5} A{6} type={7} on={8}) on16={9} time={10:o}",
                Path.GetFileNameWithoutExtension(SourceFile ?? ""),
                (int)OurType, OurType, OurEvent,
                EventNode, EventLoop, EventAddr, EventInputType, EventOn,
                On, Timestamp);
        }
    }

    internal static class MtnDecoder
    {
        public const int kMtnRecordSize = 224;

        public static MtnRecord ReadFile(string path)
        {
            byte[] buf = File.ReadAllBytes(path);
            return Decode(buf, path);
        }

        public static MtnRecord Decode(byte[] buf, string sourceFile = null)
        {
            if (buf == null || buf.Length < kMtnRecordSize)
            {
                throw new InvalidDataException(string.Format(
                    "{0}: expected at least {1} bytes, got {2}",
                    sourceFile == null ? "<buffer>" : Path.GetFileName(sourceFile),
                    kMtnRecordSize,
                    buf == null ? 0 : buf.Length));
            }

            MtnRecord rec = new MtnRecord
            {
                SourceFile  = sourceFile,
                OurType     = (NwmData)BitConverter.ToInt32(buf, 0x00),
                OurEvent    = BitConverter.ToInt32(buf, 0x04),
                On          = BitConverter.ToInt16(buf, 0x08),
                Value       = BitConverter.ToInt16(buf, 0x0A),
                Node        = BitConverter.ToInt16(buf, 0x0C),
                Zone        = BitConverter.ToInt16(buf, 0x0E),
                Op          = BitConverter.ToInt16(buf, 0x10),
                ControlType = BitConverter.ToInt16(buf, 0x12),
                Dat1        = BitConverter.ToInt32(buf, 0x14),
                Dat2        = BitConverter.ToInt32(buf, 0x18),
                Dat3        = BitConverter.ToInt32(buf, 0x1C),
                Dat4        = BitConverter.ToInt32(buf, 0x20),
                Dat5        = BitConverter.ToInt32(buf, 0x24),
                Dat6        = BitConverter.ToInt32(buf, 0x28),
                Time        = BitConverter.ToUInt32(buf, 0x2C),
                Text        = ReadText(buf, 0x50, 64),
                Text2       = ReadText(buf, 0x90, 40),
                Text3       = ReadText(buf, 0xB8, 40),
            };

            for (int i = 0; i < 8; i++)
            {
                rec.Spare[i] = BitConverter.ToInt32(buf, 0x30 + (i * 4));
            }

            return rec;
        }

        private static string ReadText(byte[] buf, int offset, int length)
        {
            int end = offset;
            while (end < offset + length && buf[end] != 0) end++;
            return Encoding.ASCII.GetString(buf, offset, end - offset);
        }
    }
}
