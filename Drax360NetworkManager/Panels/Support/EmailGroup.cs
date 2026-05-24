using System.Collections.Generic;

namespace DraxTechnology.Panels
{
    internal class EmailGroup
    {
        public int Number { get; set; }
        public string Name { get; set; }
        public bool InUse { get; set; }
        public string Filter { get; set; }
        public bool LocText { get; set; }
        public bool NodeText { get; set; }
        public bool Html { get; set; }
        public bool Bcc { get; set; }
        public bool ReportsOnly { get; set; }
        public List<string> Addresses { get; set; } = new List<string>();
    }
}
