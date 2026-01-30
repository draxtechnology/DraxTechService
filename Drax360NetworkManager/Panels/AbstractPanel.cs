using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime;
using System.Security;
using System.Text;
using System.Threading;

namespace DraxTechnology.Panels
{
    internal abstract class AbstractPanel
    {
        #region Constants
        protected const byte kHeartbeatInitialDelaySeconds = 60;
        protected const byte kHeartbeatDelaySeconds = 60;
        protected const string ksettingsetupsection = "SETUP";
        protected const string ksettingsyncrosection = "SYNCRO";
        protected const string ksettingsignifiresection = "SIGNIFIRE";
        protected const string ksettingpanelsection = "PANEL";
        protected const string ksettingmainsection = "MAIN";
        private const string kinifolder = "";

        private Queue<byte[]> commandQueue = new Queue<byte[]>();
        private object queueLock = new object();

        #endregion

        #region private fields
        protected readonly List<byte> buffer = new List<byte>();
        protected Timer heartbeat_timer;
        public string Extension;


        #endregion

        #region Properties
        protected SerialPort serialport { get; set; }
        public event EventHandler Fire;
        public event EventHandler OutsideEvents;

        public string Identifier { get; private set; }
        public string GetFileName { get; private set; }
        public string FullFilePath { get; private set; }
        public int Offset { get; set; }

        public DateTime lastDataReceived = DateTime.MinValue;

        // Abstract properties

        public abstract string FakeString { get; }

        #endregion

        #region Constructors
        public AbstractPanel(string basesettingsfolder, string identifier, string inifile, string extension)
        {
            Identifier = identifier;
            string inifolder = Path.Combine(basesettingsfolder, kinifolder);
            if (!Directory.Exists(inifolder))
            {
                Directory.CreateDirectory(inifolder);
            }

            this.Extension = extension;
            this.GetFileName = inifile;
            this.FullFilePath = Path.Combine(inifolder, inifile);
        }
        #endregion

        #region Public Methods
        public abstract void StartUp(int fakemode);
        public abstract void Evacuate(string passedValues);
        public abstract void EvacuateNetwork(string passedValues);
        public abstract void Alert(string passedValues);
        public abstract void Silence(string passedValues);
        public abstract void MuteBuzzers(string passedValues);
        public abstract void Reset(string passedValues);
        public abstract void DisableDevice(string passedValues);
        public abstract void EnableDevice(string passedValues);
        public abstract void DisableZone(string passedValues);
        public abstract void EnableZone(string passedValues);
        public abstract void Analogue(string passedValues);

        public void NotifyClient(string message, bool notifyui = false)
        {
            OutsideEvents?.Invoke(this, new CustomEventArgs(message, notifyui));
        }

        public void SendEvent(string panel, NwmData type, int inputtype, string text, bool on, int node = 0, int loop = 0, int device = 0)
        {
            EventHandler handler = Fire;

            if (handler != null) handler(this, new CustomEventArgs(text, true));

            if (type == NwmData.AlarmToAmx || type == NwmData.ResetToNwm || type == NwmData.IsolationToAmx)
            {
                amxalarm(text, inputtype, on, node, loop, device);
            }
            else
            {
                amxsend(type, text, inputtype, on, node, loop, device);
            }
        }
        public void Shutdown()
        {
            if (serialport != null)
            {
                try
                {
                    serialport.Close();
                }
                catch
                { }
                serialport.Dispose();
                serialport = null;
            }
        }

        public virtual void Parse(byte[] buffer)
        {
            this.buffer.AddRange(buffer);
        }

        public virtual void SerialPort_Datareceived(object sender, SerialDataReceivedEventArgs e)
        {
            System.Threading.Thread.Sleep(1000);
            int bytestoread = serialport.BytesToRead;
            if (bytestoread == 0) return;

            byte[] readbytes = new byte[bytestoread];
            int numberread = serialport.Read(readbytes, 0, bytestoread);
            if (numberread == 0) return;

            Parse(readbytes);
        }

        public Boolean SerialPortIsOpen()
        {
            if (serialport == null) return false;
            return serialport.IsOpen;
        }

        public T GetSetting<T>(string section, string name)
        {
            return SettingsSingleton.Instance(this.FullFilePath).GetSetting<T>(section, name);
        }

        #endregion

        #region Protected Methods
        protected virtual void heartbeat_timer_callback(object sender)
        {
            Console.WriteLine("Sent Heartbeat");
        }

        protected bool serialsend(byte[] toSend)
        {
            // Always add to queue first
            QueueCommand(toSend);
            this.NotifyClient("Command added to queue");

            // If port is open, process the queue immediately
            if (serialport?.IsOpen == true)
            {
                this.ProcessQueuedCommands();
                return true;
            }
            else
            {
                this.NotifyClient("Port not open, command queued for later");
                return false;
            }
        }

        private void QueueCommand(byte[] command)
        {
            lock (queueLock)
            {
                commandQueue.Enqueue(command);
                this.NotifyClient($"Command queued (queue size: {commandQueue.Count})", false);
            }
        }

        protected void ProcessQueuedCommands()
        {
            lock (queueLock)
            {
                if (commandQueue.Count == 0)
                {
                    Console.WriteLine("No queued commands to process");
                    return;
                }

                this.NotifyClient($"Processing {commandQueue.Count} queued commands", false);

                int successCount = 0;
                int failCount = 0;

                while (commandQueue.Count > 0)
                {
                    byte[] command = commandQueue.Peek(); // Look at first item without removing

                    if (serialport?.IsOpen == true)
                    {
                        try
                        {
                            serialport.Write(command, 0, command.Length);
                            string hex = BitConverter.ToString(command);
                            Console.WriteLine($"Queued command sent (Hex): {hex}");

                            commandQueue.Dequeue(); // Remove from queue after successful send
                            successCount++;

                            // Small delay between queued commands to avoid overwhelming the device
                            Thread.Sleep(50);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to send queued command: {ex.Message}");
                            failCount++;
                            break; // Stop processing if send fails
                        }
                    }
                    else
                    {
                        Console.WriteLine("Port closed while processing queue");
                        break; // Port closed, stop processing
                    }
                }

                this.NotifyClient($"Sent {successCount} queued commands ({commandQueue.Count} remaining)", false);
            }
        }

        protected bool serialsendstring(string[] values)
        {
            if (serialport?.IsOpen == true)
            {
                byte[] toSend = values
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Select(v => (byte)Convert.ToInt32(v))
                    .ToArray();

                serialport.Write(toSend, 0, toSend.Length);
                this.NotifyClient("Sent: " + string.Join(", ", values), false);
                return true;
            }
            return false;
        }

        protected bool serialsendstring_analogue(string[] values)
        {
            if (serialport?.IsOpen == true)
            {
                foreach (var v in values)
                {
                    if (!string.IsNullOrEmpty(v))
                    {
                        byte b = unchecked((byte)Convert.ToInt32(v));
                        serialport.Write(new byte[] { b }, 0, 1);

                        Thread.Sleep(20); // increase if needed (10–20ms sometimes)
                    }
                }
                Thread.Sleep(1000);

                this.NotifyClient("Sent analogue: " + string.Join(", ", values), false);
                return true;
            }
            return false;
        }


        protected void SendChar(char ch)
        {
            if (serialport?.IsOpen != true)
            {
                serialport.Open();
            }

            // Send a single character as ASCII byte
            byte[] b = Encoding.ASCII.GetBytes(new char[] { ch });
            serialport.Write(b, 0, b.Length);

            this.NotifyClient("Sent Char: " + ch + " (" + ((int)ch) + ")", false);
        }

        protected void serialsend(string toSend)
        {
            serialsend(Encoding.ASCII.GetBytes(toSend));
        }
        #endregion

        #region Private Methods

        private void amxsend(NwmData type, string text, int inputtype, bool on, int node = 0, int loop = 0, int device = 0)
        {
            //int amxoffset = 0; // 0 amxlight

            int evnum = CSAMXSingleton.CS.MakeInputNumber(node, loop, device, inputtype, on);

            CSAMXSingleton.CS.WriteData(type, evnum, text, "", "");
            CSAMXSingleton.CS.FlushMessages();
        }

        private void amxalarm(string text, int inputtype, bool on, int node = 0, int loop = 0, int device = 0)
        {
            //int amxoffset = 0; // 0 amxlight

            int evnum = CSAMXSingleton.CS.MakeInputNumber(node, loop, device, inputtype, on);

            CSAMXSingleton.CS.SendAlarmToAMX(evnum, "", "", text);
            CSAMXSingleton.CS.FlushMessages();
        }

        public void TryReconnect()
        {
            try
            {
                if (!SerialPortIsOpen())
                {
                    serialport.Open();
                }
            }
            catch
            {
                // handle/log failure
            }
        }
        #endregion
    }
}