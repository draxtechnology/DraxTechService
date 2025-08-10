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
        const string kappname = "Drax 360 Service";

        const int kfaketimertickseconds = 60;
        const int kfakefireinitialwakeseconds = 0;

        // settings sections
        //const string ksettingsetupsection = "SETUP";
        const string ksettingpanelsection = "PANEL";
       
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

        private List<AbstractPanel> abstractpanels = new List<AbstractPanel>();

        private List<System.Threading.Timer> faketimers = new List<System.Threading.Timer>();

      
        private int indent = 0;
        private string[] args = null;
        private int fakemode = 0;
        private Mutex filelockmutex = new Mutex();
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
            /*int setttingbaudrate = apbase.GetSetting<int>(ksettingsetupsection, "BaudRate");
            string settingparity = apbase.GetSetting<string>(ksettingsetupsection, "Parity");
            int settingdatabits = apbase.GetSetting<int>(ksettingsetupsection, "DataBits");
            int settingstopbits = apbase.GetSetting<int>(ksettingsetupsection, "StopBits");
            kvp("BaudRate", setttingbaudrate);
            kvp("Parity", settingparity);
            kvp("DataBits", settingdatabits);
            kvp("StopBits", settingstopbits);
            pad();
            */

            for (int i = 1; i < 7; i++)
            {
               
                string panel = ksettingpanelsection + i;
                
                // now work out the settings for this panel                               
                int port = apbase.GetSetting<int>(panel, "CommPort");

                if (port <= 0) continue;

                string identifier = "COM" + port;
                AbstractPanel ap = getpanel(identifier);
                

                ap.OnStartUp(fakemode);
                ap.OutsideEvents += Sp_Fire;

                // we are in fake mode
                if (this.fakemode > 0)
                {
                    ln("Opened Fake " + identifier + " Mode " + fakemode);
                    
                    faketimers.Add(new System.Threading.Timer(fake_timer, identifier, kfakefireinitialwakeseconds * 1000, kfaketimertickseconds * 1000));
                }
                else
                {
                    // we are a real serial port 
                    /*ap.Port = new SerialPort(identifier);
                    ap.Port.BaudRate = setttingbaudrate;

                    Parity parity = Parity.None;
                    string friendlyparity = settingparity.Substring(0, 1).ToUpper();
                    if (friendlyparity == "E")
                        parity = Parity.Even;
                    if (friendlyparity == "O")
                        parity = Parity.Odd;

                    ap.Port.Parity = parity;

                    ap.Port.DataBits = settingdatabits;
                    ap.Port.StopBits = (StopBits) settingstopbits;
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

                    ap.OnStartUp();
                    if (ap.Port.IsOpen)
                    {
                        ap.Port.DiscardInBuffer();
                        ap.Port.DiscardOutBuffer();
                    
                    }*/
                    
                }

                abstractpanels.Add(ap);
            }
        }

        private void Sp_Fire(object sender, EventArgs e)
        {
            CustomEventArgs ex = e as CustomEventArgs;
            string msg = ex.Message.ToString();
            bool notifyui = ex.NotifyUI;
            ln("Fired " + msg);
            if (notifyui)
            { 
                sendreturncmd(msg);
            }
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
                    ret = new PanelGent(identifier);
                    break;

                case "MORLEYMAX":
                    ret = new PanelMorelyMax(identifier);
                    break;

                case "ADVANCED":
                    ret = new PanelAdvanced(identifier);
                    break;

                default:
                    throw new Exception("Panel Undefined " + panel);
            }
            return ret;
        }

        // This is the data received event for the serial port
        /*
        private void port_datareceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort serialPort = (SerialPort)sender;

            // MIKE Chunker should fix this - please test and remove this line if we can

            // My worry with this, is that port_datareceived could be called multiple times

            //System.Threading.Thread.Sleep(1000);

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

            byte[] readbytes = new byte[bytestoread];
            int numberread = serialPort.Read(readbytes, 0, bytestoread);
            if (numberread == 0) return;

            spe.Parse(readbytes);

        }*/

        private void startpipeserver()
        {
            pipeserversend = new NamedPipeServerStream(kpipenamesend, PipeDirection.InOut, 254, PipeTransmissionMode.Message);
            ln("Pipe Server Send is Started (" + kpipenamesend + ")");
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
                byte[] response = Encoding.UTF8.GetBytes(strret);

                //send response to a client
                pipeserversend.Write(response, 0, response.Length);
                pipeserversend.Disconnect();

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
            // singular for now
            panel = ConfigurationManager.AppSettings["Panels"].Trim().ToUpper();

          

            Console.Title = kappname;
            title("-- " + kappname + " --");
          
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

            kvp("Version", Assembly.GetEntryAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>().Version);
            
            kvp("Panel",panel);
            pad();
            startpipeserver();
            pad();
            dumpavailableserialports();
            pad();
            
            

            startpipesend();

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
                if (ap.SerialPort == null) continue;

                ln("Closing " + ap.SerialPort.PortName);
                try
                {
                    ap.SerialPort.Close();
                }
                catch
                {
                    warning("Failed To Close " + ap.SerialPort.PortName);
                }
                if (!ap.SerialPort.IsOpen)
                {
                    ln("Closed" + ap.SerialPort.PortName);
                }

                ap.SerialPort.Dispose();
                ap.SerialPort = null;
            }
            abstractpanels.Clear();
            ln("Stopped Service");

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