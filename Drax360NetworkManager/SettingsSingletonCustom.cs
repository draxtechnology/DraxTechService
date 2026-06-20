#region usings
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#endregion

namespace DraxTechnology
{
    public sealed class SettingsSingletonCustom
    {
        #region constants
        private const char ksettingdelim = '_';
        private const char ksettingvaluedelim = '=';
        private const char ksectionstart = '[';
        private const char ksectionend = ']';
        #endregion

        #region private variables
        private static SettingsSingletonCustom file = null;
        private static readonly object _instanceLock = new object();
        private Dictionary<string, string> settings = new Dictionary<string, string>();
        // Guards every read/write of `settings` — PanelAdvanced persists zone text
        // from event-handling threads while other paths read settings concurrently.
        private readonly object _settingsLock = new object();
        private string settingsfile;
        #endregion

        #region public methods
        public SettingsSingletonCustom(string filename)
        {
            settingsfile = Path.Combine("", filename + ".txt");
            ReLoadSettings();
        }

        public void SaveSettings()
        {
            string section = "";
            string buffer = "";
            List<string> orderedKeys;
            Dictionary<string, string> snapshot;
            lock (_settingsLock)
            {
                snapshot = new Dictionary<string, string>(settings);
                orderedKeys = snapshot.Keys.OrderBy(i => i).ToList();
            }
            foreach (string key in orderedKeys)
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

                string msgline = splits[1] + ksettingvaluedelim + snapshot[key];
                buffer += msgline + Environment.NewLine;
                Console.WriteLine(DateTime.Now + ": " + "\tAdding Line: " + msgline);
            }

            File.WriteAllText(settingsfile, buffer);

        }

        public void ReLoadSettings()
        {
            if (!File.Exists(settingsfile))
            {
                lock (_settingsLock)
                {
                    settings.Clear();
                }
                return;
            }
            string section = "";

            // Build then swap in atomically (see SettingsSingleton for rationale).
            var loaded = new Dictionary<string, string>();
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
                if (linesplit.Length != 2)
                {
                    continue;
                }

                string key = makekey(section, linesplit[0]);

                string value = linesplit[1].Trim();
                if (String.IsNullOrEmpty(value)) { continue; }
                if (loaded.ContainsKey(key))
                {
                    continue;
                }

                loaded.Add(key, value);
            }

            lock (_settingsLock)
            {
                settings = loaded;
            }
        }

        public void SetSetting(string section, string name, object value)
        {
            string key = makekey(section, name);
            lock (_settingsLock)
            {
                settings.Remove(key);
                settings.Add(key, value.ToString());
            }
        }

        public void RemoveSetting(string section, string name)
        {
            string key = makekey(section, name);
            lock (_settingsLock)
            {
                settings.Remove(key);
            }
        }

        public T GetSetting<T>(string section, string name)
        {
            string key = makekey(section, name);
            string val;
            lock (_settingsLock)
            {
                if (!settings.TryGetValue(key, out val))
                {
                    return default(T);
                }
            }

            try
            {
                return (T)Convert.ChangeType(val, typeof(T));
            }
            catch (Exception ex)
            {
                // Malformed value — log and fall back to the type default rather
                // than throwing out of a panel event handler.
                Console.WriteLine(DateTime.Now + ": " +
                    $"SettingsSingletonCustom: '{key}' value '{val}' not convertible to {typeof(T).Name} " +
                    $"({ex.Message}) — using default.");
                return default(T);
            }
        }

        public string GetSettingsKeysInSection(string section)
        {
            string ret = "";
            string findsection = section.Trim().ToUpper();
            List<string> keys;
            lock (_settingsLock)
            {
                keys = settings.Keys.OrderBy(i => i).ToList();
            }
            foreach (string key in keys)
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

        public static SettingsSingletonCustom Filename(string filename)
        {
            // Double-checked locking — PanelAdvanced can reach this from event threads.
            if (file == null)
            {
                lock (_instanceLock)
                {
                    if (file == null)
                    {
                        file = new SettingsSingletonCustom(filename);
                    }
                }
            }
            return file;
        }

        public string GetSettingSections()
        {
            string ret = "";
            string section = "";
            List<string> keys;
            lock (_settingsLock)
            {
                keys = settings.Keys.OrderBy(i => i).ToList();
            }
            foreach (string key in keys)
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
