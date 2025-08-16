using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;


namespace Drax360Service
{
    public sealed class SettingsSingleton
    {
        private const char ksettingdelim = '_';
        private const char ksettingvaluedelim = '=';
        private const char ksectionstart = '[';
        private const char ksectionend = ']';
        private static SettingsSingleton instance = null;
        private Dictionary<string, string> settings = new Dictionary<string, string>();
        private string settingsfile;
        public SettingsSingleton(string panelfilename)
        {
            settingsfile = Path.Combine("ini", panelfilename + ".ini");
            ReLoadSettings();
        }

        public void SaveSettings()
        {
            //string settingfiletemp = Path.Combine("ini", "temp" + ".ini");
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
                    buffer += msgsection+ Environment.NewLine;
                     Console.WriteLine("Adding section: " + msgsection);

                }

                string msgline = splits[1] + ksettingvaluedelim + settings[key];
                buffer += msgline+ Environment.NewLine;
                Console.WriteLine("\tAdding Line: " + msgline);
            }
            //File.WriteAllText(settingfiletemp, buffer);
            File.WriteAllText(settingsfile, buffer);
        }

        public void ReLoadSettings()
        {
            settings.Clear();
            if (!File.Exists(settingsfile)) return;
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
                if (linesplit.Length != 2)
                {

                    continue;
                }

                string key = makekey(section, linesplit[0]);
                
                string value = linesplit[1].Trim();
                if (String.IsNullOrEmpty(value)) { continue; }
                if (settings.ContainsKey(key))
                {

                    continue;
                }

                settings.Add(key, value);
            }
        }


        public void SetSetting(string section, string name,object value)
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

        public string GetSettingsKeyInSection(string section)
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

        private string makekey(string section, string name)
        {
            return section.ToUpper() + ksettingdelim + name.Trim().ToUpper();
        }

        public static SettingsSingleton Instance(string panelfilename)
        {

            if (instance == null)
            {
                instance = new SettingsSingleton(panelfilename);
            }
            return instance;
        }

        internal string GetSettingSections()
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
    }
}

