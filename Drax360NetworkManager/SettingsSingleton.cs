using System;
using System.Collections.Generic;
using System.IO;


namespace Drax360Service
{
    public sealed class SettingsSingleton
    {
        private const string ksettingdelim = "_";
        private static SettingsSingleton instance = null;
        private Dictionary<string, string> settings = new Dictionary<string, string>();
        private string settingfile;
        public SettingsSingleton(string panelfilename)
        {
            settingfile = Path.Combine("ini", panelfilename + ".ini");
            ReLoadSettings();
        }
        public void ReLoadSettings()
        {
            settings.Clear();
            if (!File.Exists(settingfile)) return;
            string section = "";

            string[] lines = File.ReadAllLines(settingfile);
            foreach (string line in lines)
            {
                // we are a section
                if (line.StartsWith("["))
                {
                    section = line.Replace("[", "").Replace("]", "");
                    section = section.ToUpper();
                    continue;
                }

                // we are a value
                if (String.IsNullOrEmpty(section))
                {

                    continue;
                }

                string[] linesplit = line.Split('=');
                if (linesplit.Length != 2)
                {

                    continue;
                }
                string key = section + ksettingdelim + linesplit[0].Trim().ToUpper();
                string value = linesplit[1].Trim();
                if (String.IsNullOrEmpty(value)) { continue; }
                if (settings.ContainsKey(key))
                {

                    continue;
                }

                settings.Add(key, value);
            }
        }

        public T GetSetting<T>(string section, string name)
        {
            
            string key = section.ToUpper() + ksettingdelim + name.ToUpper();
            if (settings.ContainsKey(key))
            {
                string val = settings[key];

                return (T)Convert.ChangeType(val, typeof(T));
            }
            return default(T);
        }


        public static SettingsSingleton Instance(string panelfilename)
        {

            if (instance == null)
            {
                instance = new SettingsSingleton(panelfilename);
            }
            return instance;
        }
    }
}

