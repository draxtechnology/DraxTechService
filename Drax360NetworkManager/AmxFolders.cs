#region usings
using System.Configuration;
using System.IO;
using System.Text.RegularExpressions;
#endregion

namespace DraxTechnology
{
    internal static class AmxFolders
    {
        // AMX's own root folder: where its Temp\Current.Nwm, its logs and the
        // network-manager inis live. The installer puts each service instance
        // one level down (C:\AMX1\NWM1..4, Product.wxs) and stamps that folder
        // into Configuration, so the root is its parent. A single legacy
        // install configured straight at C:\AMX1 has no NWM{n} leaf and is its
        // own root. AmxRoot in App.config overrides both for an AMX that lives
        // somewhere else entirely.
        public static string Root(string basefolder)
        {
            string configured = ConfigurationManager.AppSettings["AmxRoot"]?.Trim();
            if (!string.IsNullOrEmpty(configured))
                return configured;

            string trimmed = basefolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string leaf = Path.GetFileName(trimmed);
            if (Regex.IsMatch(leaf, @"^NWM\d+$", RegexOptions.IgnoreCase))
            {
                string parent = Path.GetDirectoryName(trimmed);
                if (!string.IsNullOrEmpty(parent))
                    return parent;
            }
            return basefolder;
        }
    }
}
