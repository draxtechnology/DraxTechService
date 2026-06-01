namespace DraxTechnology.Panels
{
    // Inspire panel — really the Pearl network manager, itself a near-copy of the
    // Notifier ID3K. All protocol behaviour lives in the PanelId3k base; this
    // variant just supplies its INI name and file extension. PanelId3k now backs
    // Inspire only (Notifier was split back out to its own driver on 2026-06-01).
    internal class PanelInspire : PanelId3k
    {
        public PanelInspire(string baselogfolder, string identifier)
            : base(baselogfolder, identifier, "INSMan", "INS")
        {
        }
    }
}
