namespace DraxTechnology.Panels
{
    // Inspire panel — really the Pearl network manager, itself a near-copy of the
    // Notifier ID3K. All protocol behaviour lives in the shared PanelId3k base;
    // this variant just supplies its INI name and file extension.
    // (De-duplicated from the old full copy on 2026-05-30; see PanelId3k.)
    internal class PanelInspire : PanelId3k
    {
        public PanelInspire(string baselogfolder, string identifier)
            : base(baselogfolder, identifier, "INSMan", "INS")
        {
        }
    }
}
