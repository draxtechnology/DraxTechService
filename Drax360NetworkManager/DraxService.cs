using Drax360Service.Panels;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Net.Sockets;
using System.Reflection;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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
    public enum NwmData
    {
        Blank = 0,
        AlarmToAmx = 1,
        IsolationToAmx = 2,
        IsolationToNwm = 3,
        OutputControlToNwm = 4,
        SounderIsolationToNwm = 5,
        ControlIsolationToNwm = 6,
        SounderControlToNwm = 7,
        ControlControlToNwm = 8,
        MessageForSystemHistoryToAmx = 9,
        NWMErrorToAmx = 10,
        GeneralControlToNwm = 11,
        EvacuateToNwm = 12,
        AlertToNwm = 13,
        SilenceToNwm = 14,
        ResetToNwm = 15,
        BuzzerMuteToNwm = 16,
        ForceEVMAttrToAmx = 17,
        EventOutputToNwm = 18,
        StartTestToNwm = 19,
        endTestToNwm = 20
    }

    public partial class DraxService : ServiceBase
    {
        #region constants
        const string kpipenamesend = "Drax360PipeSend";
        const string kpipenamereturn = "Drax360PipeReturn";
        const char kpipedelim = '|';
        const string kappname = "Drax360 Service";
        const int kfaketimertickseconds = 60;
        const int kfakefireinitialwakeseconds = 0;
        const string klogfilefolder = "System";
        // settings sections
        const string ksettingsetupsection = "SETUP";
        const string ksettingpanelsection = "PANEL";
        const string ksettingmainsection = "MAIN";

        protected SerialPort serialport { get; set; }

        #endregion

        #region private variables
        private int _port = 3090;
        private string _address = "localhost";
        private TcpClient _tcpClient;
        private bool _connected = true;

        private NetworkStream _stream;
        private StreamWriter _writer;
        private System.Timers.Timer _heartbeatTimer;
        private NamedPipeServerStream pipeserversend = null;
       
        private string panel = "";
        private string configurationbasefolder="";

        private List<AbstractPanel> abstractpanels = new List<AbstractPanel>();

        private List<System.Threading.Timer> faketimers = new List<System.Threading.Timer>();

        private int indent = 0;
        private string[] args = null;
        private int fakemode = 0;
        private Mutex filelockmutex = new Mutex();

        public bool DebugLog { get; set; }

        #endregion

        #region private methods
        private string friendlytimestamp()
        {
            if (indent > 0) return "";

            var ukTimeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
            var ukTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ukTimeZone);

            string ret = ukTime.ToString("dd-MM-yyyy HH:mm:ss.fff", CultureInfo.InvariantCulture);

            return ret + " ";
        }

        private void ln(string message, EventLogEntryType eventtype = EventLogEntryType.Information)
        {
            Console.WriteLine("".PadLeft(indent, '\t') + message);
            Console.ResetColor();
            log(message);
        }

        private void kvp(string key, object value)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write("".PadLeft(indent, '\t') + key.Trim());

            Console.ResetColor();
            Console.WriteLine(" = " + value);

            Console.ResetColor();
            log(key + " = " + value);
        }

        private void dumpavailableserialports()
        {
            string[] ports = SerialPort.GetPortNames();
            if (ports.Length == 0)
            {
                warning("No Available Serial Ports");
                return;
            }
            title("Available Serial Ports");
            indent++;
            foreach (string port in ports)
            {
                ln(port);
            }
            indent--;
        }

        private void log(string message, EventLogEntryType eventtype = EventLogEntryType.Information)
        {
            if (this.DebugLog == true)
            {
                filelockmutex.WaitOne();

                // changed to new log file path
                string logdir = Path.Combine(configurationbasefolder, klogfilefolder);
                if (!Directory.Exists(logdir))
                {
                    Directory.CreateDirectory(logdir);
                }

                string workinglogfile = Path.Combine(logdir, DateTime.Now.ToString("yyyy-MM-dd-") + getpanel().GetFileName + ".log");
                File.AppendAllText(workinglogfile, friendlytimestamp() + " " + message + "\r\n");

                filelockmutex.ReleaseMutex();
            }
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

        private void warning(string warningmessage)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            ln("Warning " + warningmessage, EventLogEntryType.FailureAudit);
        }

        private void init_service()
        {
            // now go grab com ports
            abstractpanels.Clear();
            //sps.Clear();
            faketimers.Clear();

            // used to just load our settings from the ini file
            AbstractPanel apbase = getpanel();
            

            for (int i = 1; i < 7; i++)
            {
                string panel = ksettingpanelsection + i;
                
                // now work out the settings for this panel                               
                int port = apbase.GetSetting<int>(panel, "CommPort");

                if (port <= 0) continue;

                string identifier = "COM" + port;
                AbstractPanel ap = getpanel(identifier);

                ap.StartUp(fakemode);
                ap.OutsideEvents += Sp_Fire;

                // we are in fake mode
                if (this.fakemode > 0)
                {
                    ln("Opened Fake " + identifier + " Mode " + fakemode);
                    
                    faketimers.Add(new System.Threading.Timer(fake_timer, identifier, kfakefireinitialwakeseconds * 1000, kfaketimertickseconds * 1000));
                }
                else
                { }

                abstractpanels.Add(ap);
            }
            StartDeviceWatcher();
        }

        private void Sp_Fire(object sender, EventArgs e)
        {
            CustomEventArgs ex = e as CustomEventArgs;
            string msg = ex.Message.ToString();
            bool notifyui = ex.NotifyUI;
            ln(msg);
            if (notifyui)
            { 
                sendreturncmd(msg);
            }
        }

        private void Sp_Log(object sender, EventArgs e)
        {
            CustomEventArgs ex = e as CustomEventArgs;
            string msg = ex.Message.ToString();
            ln(msg);

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
        private AbstractPanel getpanel(string identifier = "")
        {
            AbstractPanel ret = null;
            switch (panel)
            {
                case "GENT":
                    ret = new PanelGent(this.configurationbasefolder,identifier);
                    break;

                case "MORLEYMAX":
                    ret = new PanelMorelyMax(this.configurationbasefolder, identifier);
                    break;

                case "ADVANCED":
                    ret = new PanelAdvanced(this.configurationbasefolder, identifier);
                    break;

                default:
                    throw new Exception("Panel Undefined " + panel);
            }
            return ret;
        }

        private void startpipeserver()
        {
            try
            {
                pipeserversend = new NamedPipeServerStream(kpipenamesend, PipeDirection.InOut, 254, PipeTransmissionMode.Message);
                ln("Pipe Server Send is Started (" + kpipenamesend + ")");
            }
            catch (Exception ex)
            {
                string err = "Error starting Pipe Server.  Check it is not running elsewhere as " + kpipenamesend+" "+ex.Message;
                // ln(err, EventLogEntryType.Error);
                throw new Exception(err);
            }

        }
        private async void startpipesend()
        {
            while (pipeserversend != null)
            {
                await pipeserversend.WaitForConnectionAsync();

                //receive message from client
                var messagebytes = readpipemessage(pipeserversend);
                string strresponse = Encoding.UTF8.GetString(messagebytes);
                ln("Message received from client: " + strresponse);
                string strret = handlepiperesponse(strresponse);
                //prepare some response
                byte[] response = null;

                try
                {
                    response = Encoding.UTF8.GetBytes(strret);
                }
                catch
                { }

                //send response to a client
                try
                {
                    pipeserversend?.Write(response ?? Array.Empty<byte>(), 0, response?.Length ?? 0);

                    pipeserversend.Disconnect();
                }
                catch (Exception ex)
                {
                    ln("Error sending response: " + ex.Message, EventLogEntryType.Error);
                }
            }
        }

        private async Task<string> sendreturnserver(string message)
        {
            using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", kpipenamereturn, PipeDirection.InOut))
            {
                pipe.Connect(5000);
                pipe.ReadMode = PipeTransmissionMode.Message;

                byte[] ba = Encoding.Default.GetBytes(message);
                pipe.Write(ba, 0, ba.Length);

                var result = await Task.Run(() =>
                {
                    return readmessagereturn(pipe);
                });

                string strresponse = Encoding.Default.GetString(result);

                Console.WriteLine("Response received from Return server: " + strresponse);

                return strresponse;
            }
        }
        private static byte[] readmessagereturn(PipeStream pipe)
        {
            if (!pipe.IsConnected) return new byte[0];

            byte[] buffer = new byte[1024];
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
        private string handlepiperesponse(string strresponse)
        {
            string passedvalues = "";
            string[] partssplit = null;
            string[] parts = strresponse.Split(kpipedelim);
            if (parts.Length > 1)
            {
                partssplit = parts[1].Split(',');
                string[] values = ExtractTextBoxValues(parts[1]);
                passedvalues = string.Join(",", values); // "1,2,3,4"
            }
            string cmd = parts[0].Trim().ToUpper();
            string ret = "OK";

            if (String.IsNullOrEmpty(cmd)) return ret;
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

                case "GETCOMMPORTSTATUS":
                    if (partssplit.Length != 1) break;
                    string identifier = partssplit[0];
                    DateTime lastSeen = DateTime.MinValue;
                    AbstractPanel ourabstractpanel = null;
                    foreach (AbstractPanel ap in abstractpanels)
                    {
                        if (ap.Identifier == identifier)
                        {
                            ourabstractpanel = ap;
                            lastSeen = ourabstractpanel.lastDataReceived;
                         
                            break;
                        }
                    }
                    if (ourabstractpanel == null) return "ERROR";
                    ret = ourabstractpanel.SerialPortIsOpen() ? "CONNECTED" : "DISCONNECTED";
                    if (ret == "CONNECTED")
                    {
                        if (lastSeen > DateTime.MinValue)
                        {
                            ret = "Data Last Received: " + lastSeen.ToString();
                        }
                    }
                    break;

                case "TEST BOX":

                    if (partssplit.Length >= 4)
                    {
                        int p1 = int.Parse(partssplit[0]); int p2 = int.Parse(partssplit[1]);
                        int p3 = int.Parse(partssplit[2]); int p4 = int.Parse(partssplit[3]);

                        int evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1, true);
                        CSAMXSingleton.CS.SendAlarmToAMX(evnum, "##TEST", "", "");
                        CSAMXSingleton.CS.FlushMessages();
                    }
                    break;

                case "TEST BOX RESET":

                    if (partssplit.Length >= 4)
                    {
                        int p1 = int.Parse(partssplit[0]); int p2 = int.Parse(partssplit[1]);
                        int p3 = int.Parse(partssplit[2]); int p4 = int.Parse(partssplit[3]);

                        int evnum = CSAMXSingleton.CS.MakeInputNumber(p2, p3, p4, p1, false);
                        CSAMXSingleton.CS.SendResetToAMX(evnum, "##TEST", "", "");
                        CSAMXSingleton.CS.FlushMessages();
                    }
                    break;

                case "SETTINGSGET":
                    if (partssplit.Length != 2) break;
                    {
                        string section = partssplit[0];
                        string key = partssplit[1];

                        ret = SettingsSingleton.Instance(panel).GetSetting<string>(section, key);
                    }
                    break;

                case "SETTINGSGETKEYSINSECTION":
                    if (partssplit.Length != 1) break;
                    {
                        string section = partssplit[0];
                        ret = SettingsSingleton.Instance(panel).GetSettingsKeysInSection(section);
                    }
                    break;

                case "SETTINGSGETSECTIONS":
                    ret = SettingsSingleton.Instance(panel).GetSettingSections();
                    break;

                case "SETTINGSSET":
                    if (partssplit.Length != 3) break;
                    {
                        string section = partssplit[0];
                        string key = partssplit[1];
                        string value = partssplit[2];
                        SettingsSingleton.Instance(panel).SetSetting(section, key, value);
                    }
                    break;

                case "SETTINGSSAVE":
                    SettingsSingleton.Instance(panel).SaveSettings();
                    break;

                case "SERVICERESTART":
                    init_service();
                    break;

                case "SETTINGSRELOAD":
                    SettingsSingleton.Instance(panel).ReLoadSettings();
                    break;

                default:

                    ln("Pipe Message Not Handled " + cmd);
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
            if (!pipe.IsConnected) return new byte[0];
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
            if (pipeserversend != null)
            {
                ln("Pipe Server Send is stopping...");
                pipeserversend.Close();
                pipeserversend.Dispose();
                pipeserversend = null;
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
            this.DebugLog = true;
            this.args = args;

            // singular for now
            panel = ConfigurationManager.AppSettings["Panels"].Trim().ToUpper();

            // New log file path
            configurationbasefolder = ConfigurationManager.AppSettings["Configuration"].Trim();

            if (!Directory.Exists(configurationbasefolder))
            {
                Directory.CreateDirectory(configurationbasefolder);
            }
            if (!firstruncheck()) return;



            // determine if we are in a fake mode
            fakemode = Convert.ToInt32(ConfigurationManager.AppSettings["FakeMode"].Trim());



            string longbar = "".PadRight(48, '-');

            string msg = " " + kappname + " Started ";
            string shortbar = "".PadRight((longbar.Length - msg.Length) / 2, '-');
            title(longbar);

            title(shortbar + msg + shortbar);
            title(longbar);




            if (args.Length > 0)
            {
                try
                {


                }
                catch
                { }
            }
            else
            {
                EventLogger.WriteToEventLog("No Command Line Args", EventLogEntryType.Warning);
            }

            kvp("Version", Assembly.GetEntryAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>().Version);
            kvp("Panel", panel);
            kvp("Configuration", this.configurationbasefolder);
            if (!Elements.isService)
            {
                title("Interactive Session");
                Console.Title = kappname;
            }
            if (fakemode > 0)
            {
                title("Fake Mode");
            }

            pad();

            AbstractPanel apbase = getpanel();
            switch (panel)
            {
                case "GENT":
                    this.DebugLog = Convert.ToBoolean(apbase.GetSetting<int>(ksettingsetupsection, "DataLogging"));
                    break;
                case "ADVANCED":
                    this.DebugLog = apbase.GetSetting<bool>(ksettingmainsection, "DesignTime");
                    break;
                default:
                    this.DebugLog = true;
                    break;
            }
            AMXTransfer amxtransfer = new AMXTransfer();
            amxtransfer.OutsideEvents += Sp_Log;
            AMXTransfer.Instance.Run(args);

            startpipeserver();

            pad();
            dumpavailableserialports();
            pad();

            startpipesend();
            CSAMXSingleton.CS.Startup(configurationbasefolder, apbase.Extension);
            CSAMXSingleton.CS.OutsideEvents += Sp_Log;

            init_service();    // start the service
        }
       
        public string sendreturncmd(string cmd, string parameters = "")
        {
            string strcmd = cmd;
            if (!string.IsNullOrEmpty(parameters))
            {
                strcmd += kpipedelim + parameters;
            }

            string result = "";
           
            try
            {
                result = Task.Run(() => sendreturnserver(strcmd)).Result;
            }
            catch (Exception ex)
            {
                result = "Error: " + ex;
            }

            return result;
        }

        public void Stopit()
        {
            indent = 0;
            ln("Stopping Service");
            stoppipeserver();
            // close fake timers
            if (this.fakemode > 0)
            {
                foreach (System.Threading.Timer timer in this.faketimers)
                {

                    timer.Dispose();

                }
                faketimers.Clear();
            }

            // close serial ports
            foreach (AbstractPanel ap in abstractpanels)
            {
                ap.Shutdown();
            }
            abstractpanels.Clear();
           
            try
            {
                _tcpClient?.Close();
                _tcpClient?.Dispose();
                _tcpClient = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error closing TCP connection: " + ex.Message);
            }
            ln("Stopped Service");
        }

        private void StartDeviceWatcher()
        {
            var watcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent"));
            watcher.EventArrived += (s, e) =>
            {
                Console.WriteLine("Device change detected. Rescanning ports...");
                RescanPorts();
            };
            watcher.Start();
        }

        private bool firstruncheck()
        {
            const string kinifolder = "";
            string inifolder = Path.Combine(this.configurationbasefolder, kinifolder);
            if (!Directory.Exists(inifolder))
            {
                Directory.CreateDirectory(inifolder);
            }
            var dirInfo = new DirectoryInfo(inifolder);
            var allFiles = dirInfo.GetFiles("*." + "ini", SearchOption.TopDirectoryOnly);
            if (allFiles.Length == 0) {
                ln("Error No Ini Files Copied into " + inifolder,EventLogEntryType.Error);
                return false; }

            return true;
        }

        private void RescanPorts()
        {
            var availablePorts = SerialPort.GetPortNames();

            foreach (AbstractPanel panel in abstractpanels)
            {
                if (!panel.SerialPortIsOpen())
                {
                    panel.TryReconnect();
                }
            }
        }
        #endregion

        #region protected methods
        protected override void OnStart(string[] args)
        {
            EventLogger.WriteToEventLog("Service is starting...", EventLogEntryType.Information);
            try
            {
                Run(args);
            }
            catch(Exception e)
            {
                EventLogger.WriteToEventLog(e.Message, EventLogEntryType.Error);
                Console.Error.Write(e.Message);
            }
        }

        protected override void OnStop()
        {
            EventLogger.WriteToEventLog("Service is Stopping...", EventLogEntryType.Information);
            try
            {
                Stopit();
            }
            catch (Exception e)
            {
                EventLogger.WriteToEventLog(e.Message, EventLogEntryType.Error);
                Console.Error.Write(e.Message);
            }
        }
        #endregion
    }
}