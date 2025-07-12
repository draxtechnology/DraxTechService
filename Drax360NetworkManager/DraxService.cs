using Drax360Service.Panels;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.IO.Ports;
using System.Reflection;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace Drax360Service
{
    public enum ActionType
    {
        kEVACTUATE,
        kEVACTUATENETWORK,
        kALERT,
        kRESET,
        kRESETNETWORK,
        kSILENCE,
        kMUTEBUZZERS,
        kDISABLEDEVICE,
        kENABLEDEVICE,
        kDISABLEMODULE,
        kENABLEMODULE,
        kDISABLEZONE,
        kENABLEZONE
    }
    public partial class DraxService : ServiceBase
    {
        #region constants
        const string kpipename = "Drax360Pipe";
        const char kpipedelim = '|';
        const string kappname = "Drax 360 Service";
       
        const char ksettingdelim = '|';
        const int kfaketimertickseconds = 60;
        const int kfakefireinitialwakeseconds = 0;

        // settings sections
        const string ksettingsetupsection = "SETUP";
        const string ksettingpanelsection = "PANEL";
        public enum enmDelimiter
        {
            DelimiterTab = 0,
            DelimiterComma = 1
        }
        #endregion

        #region private variables
        NamedPipeServerStream pipeserver = null;
        string panel = "";
        Dictionary<string, string> settings = new Dictionary<string, string>();

        //List<SerialPortExtra> sps = new List<SerialPortExtra>();
        List<AbstractPanel> abstractpanels = new List<AbstractPanel>();

        List<Timer> faketimers = new List<Timer>();

        // will remove any unused values as we go
        int giMainOffset = 0;
        int giDomainOffset = 0;
        int gsNWMBaud = 0;
        int gsNWMDataBits = 0;
        string gsNWMParity = "";
        int gsNWMStop = 0;
        int gsNWMHeartBeat = 0;
        int giDomainNumber = 0;
        int giNWMPanelTCP = 0;
        bool gbNWMDisplayUnknownEvents = false;
        bool gbDisablePanelText = false;
        bool gbDisplayChkSumFails = false;
        string gsExtendedTextPath = "";
        bool gbExtendedText = false;
        enmDelimiter gDelimiter = enmDelimiter.DelimiterComma;
        int giUseExtendedTextIfOver = 0;
        bool gbOutstationFaultGenFault = false;
        bool DesignTime = false;
        string sLogDate = "";
        DateTime dteDataLogSetDate = DateTime.MinValue;
      
        private int indent = 0;
        string[] args = null;
        int fakemode = 0;
        Mutex filelockmutex = new Mutex();
        #endregion

        #region private methods

        private string friendlytimestamp()
        {

            if (indent > 0) return "";

            string ret = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            return ret + " ";
        }
        private void ln(string message, EventLogEntryType eventtype = EventLogEntryType.Information)
        {
            //if (indent==0) Console.WriteLine(friendlytimestamp());
            Console.WriteLine("".PadLeft(indent, '\t') + message);
            Console.ResetColor();
            log(message);
        }

        private void kvp(string key, object value)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write("".PadLeft(indent, '\t') + key);

            Console.ResetColor();
            Console.WriteLine(" = " + value);

            Console.ResetColor();
            log(key + " = " + value);
        }

        private void dumpavailableserialports()
        {
            title("Available Serial Ports");

            string[] ports = SerialPort.GetPortNames();
            if (ports.Length == 0)
            {
                warning("No Available Serial Ports");
                return;
            }
            indent++;
            foreach (string port in ports)
            {
                ln(port);
            }
            indent--;
        }

        private void dumpsettings()
        {
            string workingsection = "";
            foreach (string key in settings.Keys)
            {
                string[] keysplit = key.Split(ksettingdelim);
                string section = keysplit[0];
                if (section != workingsection)
                {
                    workingsection = section;
                    indent = 0;
                    title(workingsection);

                }
                indent = 1;
                kvp(keysplit[1], settings[key]);

            }
        }

        private T getsetting<T>(string section, string name)
        {
            string key = section.ToUpper() + ksettingdelim + name.ToUpper();
            if (!settings.ContainsKey(key))
            {
                warning("Setting Not Found " + section+" "+name);
                return default(T);
            }
            string val = settings[key];

            return (T)Convert.ChangeType(val, typeof(T));
        }

        private void loadsettings()
        {
            string settingfile = "ini/" + getpanel().GetFileName+".ini";
            
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
                if (String.IsNullOrEmpty(section)) {
                    warning("No Section Specified For Setting " + line);
                    continue;
                }

                string[] linesplit = line.Split('=');
                if (linesplit.Length != 2) {
                    warning("Incorrect Setting "+section+ " "+line);
                    continue; }
                string key = section + ksettingdelim + linesplit[0].Trim().ToUpper();
                string value = linesplit[1].Trim();
                if (String.IsNullOrEmpty(value)) { continue; }
                if (settings.ContainsKey(key))
                {
                    warning("Duplicate Setting" + section + " " + key + " - " + value);
                    continue;
                }

                settings.Add(key, value);

            }
        }

        private void log(string message, EventLogEntryType eventtype = EventLogEntryType.Information)
        {
            filelockmutex.WaitOne();




            string logfile = getpanel().GetFileName + ".log";

            File.AppendAllText(DateTime.UtcNow.ToString("yyyy-MM-dd-") + logfile, friendlytimestamp() + " " + message + "\r\n");

            filelockmutex.ReleaseMutex();
        }

        private void pad()
        {
            Console.WriteLine();

        }
        private void title(string msg)
        {
            
            Console.ForegroundColor = ConsoleColor.Cyan;
            ln(msg);
           
        }

        private void error(string errormessage)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            indent = 0;
            ln("Error "+errormessage, EventLogEntryType.Error);
        }

        private void warning(string warningmessage)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            ln("Warning "+warningmessage, EventLogEntryType.FailureAudit);
        }

        private void init_service()
        {
 //if (AmxLite == 0)

            //Full version
            giMainOffset = getsetting<int>(ksettingsetupsection, "giAmx1Offset");



            giDomainOffset = getsetting<int>(ksettingsetupsection, "DomainOffset");
            gsNWMBaud = getsetting<int>(ksettingsetupsection, "BaudRate");


            gsNWMDataBits = getsetting<int>(ksettingsetupsection, "DataBits");
            gsNWMParity = getsetting<string>(ksettingsetupsection, "Parity");
            gsNWMStop = getsetting<int>(ksettingsetupsection, "StopBits");
            gsNWMHeartBeat = getsetting<int>(ksettingsetupsection, "HeartbeatTimeout");
            giDomainNumber = getsetting<int>(ksettingsetupsection, "DomainNumber");


            giNWMPanelTCP = getsetting<int>(ksettingsetupsection, "PanelcTCP");
            gbNWMDisplayUnknownEvents = getsetting<int>(ksettingsetupsection, "DisplayUnknownEvents") == 1;

            gbDisablePanelText = getsetting<int>(ksettingsetupsection, "DisablePanelText") == 1;
            gbDisplayChkSumFails = getsetting<int>(ksettingsetupsection, "DisplayChkSumFails") == 1;
            gsExtendedTextPath = getsetting<string>(ksettingsetupsection, "ExtendedTextFilePath");


            gbExtendedText = getsetting<int>(ksettingsetupsection, "ExtendedText") == 1;


            // double check these come out in the right order
            int delim = getsetting<int>(ksettingsetupsection, "Delimiter");
            if (delim == 0)
            {
                gDelimiter = enmDelimiter.DelimiterTab;
            }
            else
            {
                gDelimiter = enmDelimiter.DelimiterComma;
            }

            giUseExtendedTextIfOver = getsetting<int>(ksettingsetupsection, "UseExtendedTextIfOver");
            gbOutstationFaultGenFault = getsetting<int>(ksettingsetupsection, "OutStationFaultsGenFault") == 1;


            DesignTime = getsetting<int>(ksettingsetupsection, "DataLogging") == 1;

            sLogDate = getsetting<string>(ksettingsetupsection, "DataLoggingSet");
            if (String.IsNullOrEmpty(sLogDate))
            {
                dteDataLogSetDate = DateTime.Parse("00:00:00");
            }
            else
            {
                dteDataLogSetDate = DateTime.Parse(sLogDate);
            }


            // now go grab com ports
            abstractpanels.Clear();
            //sps.Clear();
            faketimers.Clear();
            for (int i = 1; i < 7; i++)
            {
                string panel = ksettingpanelsection + i;
               
                int zone = getsetting<int>(panel, "ZoneBase");
                int port = getsetting<int>(panel, "CommPort");
                if (port <= 0) continue;

                string identifier = "COM" + port;
                AbstractPanel ap = getpanel(identifier);
                


                //sp.Zone = zone;
                ap.Fire += Sp_Fire;
                

                
                // we are in fake mode
                if (this.fakemode > 0)
                {
                    ln("Opened Fake " + identifier +" Mode "+ fakemode);
                    faketimers.Add( new Timer(fake_timer,identifier, kfakefireinitialwakeseconds*1000, kfaketimertickseconds*1000));  
                   
                }
                else
                {
                    // we are a real serial port 
                    ap.Port = new SerialPort(identifier);
                    ap.Port.BaudRate = gsNWMBaud;
                    
                    Parity parity = Parity.None;
                    string friendlyparity = gsNWMParity.Substring(0, 1).ToUpper();
                    if (friendlyparity == "E")
                        parity = Parity.Even;
                    if (friendlyparity == "O")
                        parity = Parity.Odd;

                    ap.Port.Parity = parity;

                    ap.Port.DataBits = gsNWMDataBits;
                    ap.Port.StopBits = (StopBits)gsNWMStop;
                    ap.Port.Handshake = Handshake.None;
                    ap.Port.DataReceived += port_datareceived;
                    if (ap.Port.IsOpen)
                    {
                        ap.Port.Close();
                    }
                    kvp("Attempting Open", ap.Port.PortName);
                    ap.Port.Encoding = System.Text.Encoding.ASCII;
                    ap.Port.DtrEnable = true;

                    ap.Port.ReadBufferSize = 8000;
                    ap.Port.WriteBufferSize = 200;

                    ap.Port.ReadTimeout = 500;
                    ap.Port.ParityReplace = (byte)0;
                    ap.Port.ReceivedBytesThreshold = 8;
                    try
                    {
                        ap.Port.Open();
                    }
                    catch (Exception e)

                    {
                        warning("Failed To Open " + ap.Port.PortName);
                        indent++;
                        ln(e.Message);
                        indent--;
                        continue;
                    }

                    ln("Opened " + ap.Port.PortName);

                    ap.OnStartUp();  // MH Added 27062025


                    ap.Port.DiscardInBuffer();  //  MH Added
                    ap.Port.DiscardOutBuffer(); //  MH Added
                }
                
                abstractpanels.Add(ap);
            }


            // dumpsettings();
        }

        
        private void Sp_Fire(object sender, EventArgs e)
        {
            CustomEventArgs ex = e as CustomEventArgs;
            string msg = ex.Message.ToString();
            ln("Fired "+msg);
        }

        private void fake_timer(object sender)
        {
            string identifier = sender.ToString();
            ln("Fake Timer Tick " + identifier);

            AbstractPanel ourabstractpanel = null;
            foreach (AbstractPanel ap in abstractpanels)
            {
                if (ap.Identifier == identifier)
                {
                    ourabstractpanel = ap;
                    break;
                }
            }
            if (ourabstractpanel == null) return;
            
            string read = ourabstractpanel.FakeString;

            byte[] bytes = Encoding.ASCII.GetBytes(read);
            ourabstractpanel.Parse(bytes);
        }


        private AbstractPanel getpanel(string identifier="")
        {
            AbstractPanel ret = null;
            switch (panel)
            {
                case "GENT":
                    ret = new PanelGent(identifier);
                    break;

                case "MORLEYMAX":
                    ret = new PanelMorelyMax(identifier);
                    break;

                case "ADVANCED":
                    ret = new PanelAdvanced(identifier);
                    break;

                default:
                    throw new Exception("Panel Undefined "+panel);

            }
            return ret;
        }
        private void port_datareceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort serialPort = (SerialPort)sender;

            AbstractPanel spe = null;
            foreach (AbstractPanel sp in abstractpanels)
            {
                if (sp.Port.PortName == serialPort.PortName)
                {
                    spe = sp;
                    break;
                }
            }
            if (spe == null) return;

            int bytestoread = serialPort.BytesToRead;
            if (bytestoread == 0) return;

            byte[] bytes = new byte[bytestoread];
            int read = serialPort.Read(bytes, 0, bytestoread);
            if (read > 0)
            {   spe.Parse(bytes);
            }
        }
        #endregion

        #region constructors
        public DraxService()
        {
            InitializeComponent();
        }
        #endregion

        #region public methods
        public void Run(string[] args)
        {
            // singular for now
            panel = ConfigurationManager.AppSettings["Panels"].Trim().ToUpper();

            startpipeserver();

            Console.Title = kappname;
            title("-- " + kappname + " --");
            title(panel);
            this.args = args;

            // determine if we are in a fake mode
            if (args.Length > 0)
            {
                try
                {
                    this.fakemode = Convert.ToInt32(args[0]);
                }
                catch
                { }
            }

            ln("Version " + Assembly.GetEntryAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>().Version);
            pad();
            dumpavailableserialports();
            pad();
            ln("Loading Settings");
            loadsettings();
            pad();
            ln("Settings Loaded (" + settings.Count + " Values)");



            listenpipe();

            // start the service
            init_service();

        }

        private void startpipeserver()
        {
            pipeserver = new NamedPipeServerStream(kpipename, PipeDirection.InOut, 254, PipeTransmissionMode.Message);
            ln("Pipe Server is Started (" + kpipename + ")");
        }

        private async void listenpipe()
        {
            while (pipeserver!=null)
            {
                await pipeserver.WaitForConnectionAsync();

                //receive message from client
                var messagebytes = readpipemessage(pipeserver);
                string strresponse = Encoding.UTF8.GetString(messagebytes);
                ln("Message received from client: " + strresponse);
                string strret = handlepiperesponse(strresponse);
                //prepare some response
                byte[] response = Encoding.UTF8.GetBytes(strret);

                //send response to a client
                pipeserver.Write(response, 0, response.Length);
                pipeserver.Disconnect();
             
            }

        }

        /*
         TODO May be required
          
        private async void writepipe(string msg)
        {
            if (pipeserver == null) return;
            await pipeserver.WaitForConnectionAsync();
            byte[] message = Encoding.UTF8.GetBytes(msg);
            pipeserver.Write(message, 0, message.Length);
            pipeserver.Disconnect();
           
        }*/

        private string handlepiperesponse(string strresponse)
        {
            string passedvalues = "";
            string[] parts = strresponse.Split(kpipedelim);
            if (parts.Length > 1)
            {
                string[] values = ExtractTextBoxValues(parts[1]);
                passedvalues = string.Join(",", values); // "1,2,3,4"
            }
            string cmd = parts[0].Trim().ToUpper();
            string ret = "OK";
            switch (cmd)
            {
                case "SILENCE":

                    // for now alert all connected panels to silence
                    foreach (var panel in abstractpanels)
                    {
                        panel.Silence(passedvalues);
                    }

                    break;
                case "MUTEBUZZERS":

                    // for now alert all connected panels to mute buzzers
                    foreach (var panel in abstractpanels)
                    {
                        panel.MuteBuzzers(passedvalues);
                    }

                    break;

                case "RESET":

                    // for now alert all connected panels to reset
                    foreach (var panel in abstractpanels)
                    {
                        panel.Reset(passedvalues);
                    }

                    break;

                case "EVACUATE":

                    // for now alert all connected panels to evacuate
                    foreach (var panel in abstractpanels)
                    {
                        panel.Evacuate(passedvalues);
                    }

                    break;

                case "ALERT":

                    // for now alert all connected panels to evacuate
                    foreach (var panel in abstractpanels)
                    {
                        panel.Alert(passedvalues);
                    }

                    break;

                case "EVACUATENETWORK":

                    // for now alert all connected panels to evacuate
                    foreach (var panel in abstractpanels)
                    {
                        panel.EvacuateNetwork(passedvalues);
                    }

                    break;

                case "DISABLEDEVICE":

                    if (passedvalues.Length > 0)
                    {
                        foreach (var panel in abstractpanels)
                        {
                            panel.DisableDevice(passedvalues);
                        }
                    }
                    break;
                case "ENABLEDEVICE":

                    if (passedvalues.Length > 0)
                    {
                        foreach (var panel in abstractpanels)
                        {
                            panel.EnableDevice(passedvalues);
                        }
                    }

                    break;

                case "DISABLEZONE":

                    if (passedvalues.Length > 0)
                    {
                        foreach (var panel in abstractpanels)
                        {
                            panel.DisableZone(passedvalues);
                        }
                    }
                    break;
                case "ENABLEZONE":

                    if (passedvalues.Length > 0)
                    {
                        foreach (var panel in abstractpanels)
                        {
                            panel.EnableZone(passedvalues);
                        }
                    }

                    break;
                case "GETPANELTYPE":
                    ret = panel;
                    break;


                default:
                    throw new Exception("Pipe Message Not Handled " + cmd);
            }
        
            return ret;
        }
        private string[] ExtractTextBoxValues(string input)
        {
            // Match "Text: X" and extract X
            var matches = Regex.Matches(input, @"Text:\s*(\d+)");
            return matches.Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
        }
        private static byte[] readpipemessage(PipeStream pipe)
        {
            byte[] buffer = new byte[2048];
            using (var ms = new MemoryStream())
            {
                do
                {
                    var readBytes = pipe.Read(buffer, 0, buffer.Length);
                    ms.Write(buffer, 0, readBytes);
                }
                while (!pipe.IsMessageComplete);

                return ms.ToArray();
            }
        }

        private void stoppipeserver()
        {
            if (pipeserver == null) return;

            ln("Pipe Server is stopping...");
            pipeserver.Close();
            pipeserver.Dispose();
            pipeserver = null;
        }

        public void Stopit()
        {
            indent = 0;
            ln("Stopping Service");
            stoppipeserver();
            // close fake timers
            if (this.fakemode>0)
            {
                foreach(Timer timer in this.faketimers)
                {
                    
                    timer.Dispose();
                   
                }
                faketimers.Clear();
            }
            
            // close serial ports
            foreach(AbstractPanel ap in abstractpanels)
            {
                if (ap.Port == null) continue;

                ln("Closing " + ap.Port.PortName);
                try
                {
                    ap.Port.Close();
                } catch {
                    warning("Failed To Close " + ap.Port.PortName);
                }
                if (!ap.Port.IsOpen)
                {
                    ln("Closed" + ap.Port.PortName);
                }
                
                ap.Port.Dispose();
                ap.Port = null;
            }
            abstractpanels.Clear();
            ln("Stopped Service");
        }
        #endregion

        #region protected methods
        protected override void OnStart(string[] args)
        {
            Run(args);
        }

        protected override void OnStop()
        {
            Stopit();
        }
        #endregion
    }
}