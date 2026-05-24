using System;

namespace DraxTechnology.Panels
{
    internal class EspaRecord
    {
        public string PagerAddress { get; set; }
        public string Line1 { get; set; }
        public string Line2 { get; set; }
        public string DisplayText { get; set; }
        public string Beeps { get; set; }
        public string MessageType { get; set; }
        public string Transmission { get; set; }
        public string Priority { get; set; }
        public DateTime Timestamp { get; set; }
        public byte[] RawBytes { get; set; }
    }
}
