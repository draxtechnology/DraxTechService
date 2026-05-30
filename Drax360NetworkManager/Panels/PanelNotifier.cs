namespace DraxTechnology.Panels
{
    // Notifier ID3000/ID3K panel. All protocol behaviour lives in the shared
    // PanelId3k base — this variant just supplies its INI name and file extension.
    // (De-duplicated from the old full copy on 2026-05-30; see PanelId3k.)
    internal class PanelNotifier : PanelId3k
    {
        public PanelNotifier(string baselogfolder, string identifier)
            : base(baselogfolder, identifier, "NOTMan", "NOT")
        {
        }
    }
}
