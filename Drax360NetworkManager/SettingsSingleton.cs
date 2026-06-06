#region usings
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#endregion

namespace DraxTechnology
{
    public sealed class SettingsSingleton
    {
        #region constants
        private const char ksettingdelim = '_';
        private const char ksettingvaluedelim = '=';
        private const char ksectionstart = '[';
        private const char ksectionend = ']';
        #endregion

        #region private variables
        private static SettingsSingleton instance = null;
        private static readonly object _instanceLock = new object();
        private Dictionary<string, string> settings = new Dictionary<string, string>();
        private string settingsfile;
        #endregion

        #region public methods
        public SettingsSingleton(string panelfilename)
        {
            settingsfile = Path.Combine("ini", panelfilename + ".ini");
            ReLoadSettings();
        }

        public void SaveSettings()
        {
            // this is a test mode, so we can write to a temp file
            bool testmode = false;

            string settingfiletemp = Path.Combine("ini", "temp" + ".ini");
            string section = "";
            string buffer = "";
            foreach (string key in settings.Keys.OrderBy(i => i))
            {
                string[] splits = key.Split(ksettingdelim);
                if (splits.Length != 2) continue;
                string workingsection = splits[0];
                if (workingsection != section)
                {
                    section = workingsection;
                    string msgsection = ksectionstart + section + ksectionend;
                    buffer += msgsection + Environment.NewLine;
                    Console.WriteLine(DateTime.Now + ": " + "Adding section: " + msgsection);
                }

                string msgline = splits[1] + ksettingvaluedelim + settings[key];
                buffer += msgline + Environment.NewLine;
                Console.WriteLine(DateTime.Now + ": " + "\tAdding Line: " + msgline);
            }
            if (testmode)
            {
                File.WriteAllText(settingfiletemp, buffer);
            }
            else
            {
                File.WriteAllText(settingsfile, buffer);
            }
        }

        public void ReLoadSettings()
        {
            settings.Clear();

            // Surface where the ini is actually being read from. settingsfile is a
            // CWD-relative "ini\<panel>.ini", so the resolved absolute path depends
            // on the service's working directory — log it so a missing or stale ini
            // is obvious rather than silently falling back to defaults (e.g.
            // giAmx1Offset=0, which reads as an AMX node mismatch on a real panel).
            string resolvedpath = Path.GetFullPath(settingsfile);
            if (!File.Exists(settingsfile))
            {
                Console.WriteLine(DateTime.Now + ": " +
                    $"SettingsSingleton: ini NOT FOUND at '{resolvedpath}' — every setting will fall back to its default. " +
                    "Check the ini is deployed alongside the service (or in the Configuration folder).");
                return;
            }
            string section = "";

            string[] lines = File.ReadAllLines(settingsfile);
            foreach (string line in lines)
            {
                // we are a section
                if (line.StartsWith(ksectionstart.ToString()))
                {
                    section = line.Replace(ksectionstart.ToString(), "").Replace(ksectionend.ToString(), "");
                    section = section.ToUpper();
                    continue;
                }

                // we are a value
                if (String.IsNullOrEmpty(section))
                {

                    continue;
                }

                string[] linesplit = line.Split(ksettingvaluedelim);
                if (linesplit.Length < 2)
                {
                    continue;
                }

                string key = makekey(section, linesplit[0]);

                string value = linesplit[1];

                // edge case if value ends with an =
                for (int i = 2; i < linesplit.Length; i++)
                {
                    value += "=";
                }

                if (settings.ContainsKey(key))
                {
                    continue;
                }

                settings.Add(key, value);
            }

            Console.WriteLine(DateTime.Now + ": " +
                $"SettingsSingleton: loaded {settings.Count} setting(s) from '{resolvedpath}'.");
        }

        public void SetSetting(string section, string name, object value)
        {
            RemoveSetting(section, name);
            string key = makekey(section, name);
            settings.Add(key, value.ToString());
        }

        public void RemoveSetting(string section, string name)
        {

            string key = makekey(section, name);
            if (settings.ContainsKey(key))
            {
                settings.Remove(key);
            }
        }

        public T GetSetting<T>(string section, string name)
        {

            string key = makekey(section, name);
            if (settings.ContainsKey(key))
            {
                string val = settings[key];

                return (T)Convert.ChangeType(val, typeof(T));
            }
            return default(T);
        }
        public string GetSettingsKeysInSection(string section)
        {
            string ret = "";
            string findsection = section.Trim().ToUpper();
            foreach (string key in settings.Keys.OrderBy(i => i))
            {
                string[] splits = key.Split(ksettingdelim);
                if (splits.Length != 2) continue;
                string workingsection = splits[0];
                if (workingsection != findsection) continue;

                if (!String.IsNullOrEmpty(ret)) ret += ",";
                ret += splits[1];
            }
            return ret;
        }

        public static SettingsSingleton Instance(string panelfilename)
        {
            // Double-checked locking around lazy init. Today only one panel
            // type is active at a time (App.config "Panels"), so the cached
            // instance's filename is expected to match every call. If it
            // doesn't, the caller would silently get the wrong settings —
            // log so we'd notice if the architecture ever changed to support
            // multiple concurrent panel types.
            if (instance == null)
            {
                lock (_instanceLock)
                {
                    if (instance == null)
                    {
                        instance = new SettingsSingleton(panelfilename);
                    }
                }
            }
            else
            {
                string expected = Path.Combine("ini", panelfilename + ".ini");
                if (!string.Equals(instance.settingsfile, expected, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(DateTime.Now + ": " + 
                        $"SettingsSingleton: requested '{panelfilename}.ini' but cached instance holds '{instance.settingsfile}' — returning cached (may be wrong settings)");
                }
            }
            return instance;
        }

        public string GetSettingSections()
        {
            string ret = "";
            string section = "";
            foreach (string key in settings.Keys.OrderBy(i => i))
            {
                string[] splits = key.Split(ksettingdelim);
                if (splits.Length != 2) continue;
                string workingsection = splits[0];
                if (workingsection != section)
                {
                    section = workingsection;
                    if (!String.IsNullOrEmpty(ret)) ret += ",";
                    ret += section;
                }
            }
            return ret;
        }
        #endregion

        #region private methods
        private string makekey(string section, string name)
        {
            return section.ToUpper() + ksettingdelim + name.Trim().ToUpper();
        }

        #endregion
    }
}
