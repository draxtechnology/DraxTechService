using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
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
        public abstract string PanelVersion { get; }
        public virtual int NumHeartbeats => 0;

        public virtual int NumMessages => 0;
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
            try { _hdTimer?.Dispose(); } catch { }
            _hdTimer = null;

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
            // Stamp every received-bytes event so GETCOMMPORTSTATUS can report
            // "Data Last Received: <ts>" — drives the client's connection
            // progress bar. Overrides that don't call base must do this too
            // (PanelGent does at line 1096; PanelEspa and PanelMorleyZX need
            // the same stamp at the top of their handlers).
            lastDataReceived = DateTime.Now;

            // Bus is busy — bytes are arriving. Hold any half-duplex transmit until the
            // frame has fully landed (Parse signals completion via NoteHalfDuplexReceive).
            // Stamped here, before the Sleep below, so a command queued during the read
            // window correctly sees the bus as active rather than racing into a collision.
            NoteHalfDuplexReceive(false);

            // TODO(2026-05-23): this 1-second Sleep is a fixed wait giving the
            // SerialPort buffer time to accumulate the rest of a frame before
            // we read it. It's an anti-pattern (serialises receive at 1 Hz on
            // chatty panels) and should be replaced with a bounded poll-until-
            // buffer-stable loop like PanelGent already uses at line ~1183.
            // Leaving as-is for now because every panel that doesn't override
            // depends on it for frame accumulation — needs per-panel testing
            // (Notifier, MorleyZX, MorleyMax, Pearl, RSM) before swapping in
            // the bounded version.
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
            Console.WriteLine(DateTime.Now + ": " + "Sent Heartbeat");
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
                    Console.WriteLine(DateTime.Now + ": " + "No queued commands to process");
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
                            Console.WriteLine(DateTime.Now + ": " + $"Queued command sent (Hex): {hex}");

                            commandQueue.Dequeue(); // Remove from queue after successful send
                            successCount++;

                            // Small delay between queued commands to avoid overwhelming the device
                            Thread.Sleep(50);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(DateTime.Now + ": " + $"Failed to send queued command: {ex.Message}");
                            failCount++;
                            break; // Stop processing if send fails
                        }
                    }
                    else
                    {
                        Console.WriteLine(DateTime.Now + ": " + "Port closed while processing queue");
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
            if (serialport == null)
            {
                this.NotifyClient("No serial port configured.", false);
                return;
            }

            if (serialport?.IsOpen != true)
            {
                try
                {
                    serialport.Open();
                }
                catch (Exception ex)
                {
                    this.NotifyClient("Failed to open " + serialport.PortName +
                                      ": " + ex.Message, false);
                    return;
                }
            }

            // Send a single character as ASCII byte
            byte[] b = Encoding.ASCII.GetBytes(new char[] { ch });
            serialport.Write(b, 0, b.Length);

            this.NotifyClient("Sent Char: " + ch + " (" + ((int)ch) + ")", false);
        }

        #region Half-duplex gated send (faithful port of the VB Notifier tmrNotifierSend discipline)
        // WHY THIS EXISTS — the "press twice" fix (2026-05-29).
        // The legacy VB Network Manager never wrote a control to the panel while a
        // frame was arriving (gbReceivingMsg): it queued the command and clocked it
        // out only in the idle gap *after* a complete inbound frame, then resent it
        // (up to twice — tmrSendTimeOut) if the panel didn't answer. The old C# path
        // (send_message -> serialsend -> ProcessQueuedCommands) wrote the whole frame
        // the instant the AMX CTRL arrived. On the half-duplex line, a write landing
        // mid inbound-frame collides and is silently lost — so the operator had to
        // press a second time to catch a clear bus. This restores the VB's two
        // guarantees: (1) only transmit when the bus is idle, (2) resend if the panel
        // doesn't answer. Whole-frame write (the VB half-duplex SendMessage path, not
        // char-by-char) — matches how send_message already assembles the frame.
        //
        // OPT-IN + ROLLBACK: only panels that set UseHalfDuplexGatedSend = true use
        // this path — the standalone Inspire and Notifier drivers.
        // Every other panel is untouched.
        // To revert a panel to the old immediate behaviour, set the flag back to false
        // (send_message then falls through to serialsend).
        //
        // NOTE FOR MIKE: this is faithful to the VB but has NOT been run against a real
        // panel yet — verify on hardware. The ack here is "the next complete inbound
        // frame after we transmit" (in half-duplex the panel echoes the command back,
        // which serves as the ack). Tunables are the three constants below.

        // Hold transmits until the line has been quiet this long since the last byte
        // in — covers the small inter-frame gap on the half-duplex bus.
        private static readonly TimeSpan kHalfDuplexQuietGap = TimeSpan.FromMilliseconds(40);
        // Resend the in-flight command if the panel hasn't answered within this window
        // (the VB glCommandTimeOut default was ~1s).
        private static readonly TimeSpan kHalfDuplexAckTimeout = TimeSpan.FromMilliseconds(1000);
        // VB comment: "messages are tried only twice to prevent problems if the panel
        // does not respond".
        private const int kHalfDuplexMaxAttempts = 2;

        protected bool UseHalfDuplexGatedSend = false;

        private readonly object _hdLock = new object();
        private readonly Queue<byte[]> _hdQueue = new Queue<byte[]>();
        private byte[] _hdInFlight;            // current command (written or about to be), else null
        private int _hdAttempts;               // writes issued for _hdInFlight so far
        private bool _hdAwaitingAck;           // a write is outstanding; don't re-write until timeout/ack
        private bool _hdReceivingFrame;        // true while a frame is arriving (bus busy)
        private DateTime _hdLastRx = DateTime.MinValue;
        private bool _hdLastFrameWasEcho;      // set when a complete frame released an in-flight command

        /// <summary>True when the most-recently-completed inbound frame was the panel's echo of a
        /// service-sent command, not an independently-generated panel event. Parse overrides should
        /// check this after calling NoteHalfDuplexReceive(true) and skip event processing if true.</summary>
        protected bool ReceivedFrameWasCommandEcho => _hdLastFrameWasEcho;
        private Timer _hdTimer;                // quiet-gap re-check + ack-timeout, single-shot

        /// <summary>
        /// Queue a fully-assembled frame (incl. checksum + CR) for half-duplex send.
        /// The frame is written only when the bus is idle, and resent up to
        /// kHalfDuplexMaxAttempts if the panel doesn't answer. Multiple queued frames
        /// (e.g. a bulk multi-device disable) are sent one at a time, in order.
        /// </summary>
        protected void HalfDuplexSend(string frame)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(frame);
            lock (_hdLock)
            {
                _hdQueue.Enqueue(bytes);
                this.NotifyClient($"Half-duplex: queued (queue size {_hdQueue.Count})", false);
                TryDrainHalfDuplex_NoLock();
            }
        }

        /// <summary>
        /// Receive-side hook. Call with frameComplete=false the moment bytes start
        /// arriving (bus busy) and frameComplete=true when a full '\r'-terminated frame
        /// has been received (the post-frame idle gap, and our ack signal).
        /// </summary>
        protected void NoteHalfDuplexReceive(bool frameComplete)
        {
            if (!UseHalfDuplexGatedSend) return;
            lock (_hdLock)
            {
                _hdLastRx = DateTime.Now;
                if (!frameComplete)
                {
                    _hdReceivingFrame = true;   // mid inbound frame — hold all transmits
                    return;
                }

                _hdReceivingFrame = false;
                _hdLastFrameWasEcho = false;
                // A complete inbound frame is the panel answering — treat it as the ack
                // for a command we've already written, and clear it.
                if (_hdInFlight != null && _hdAttempts > 0)
                {
                    _hdLastFrameWasEcho = true;
                    _hdInFlight = null;
                    _hdAwaitingAck = false;
                    this.NotifyClient("Half-duplex: command acknowledged by panel", false);
                }
                // Post-frame idle gap: good moment to send the next queued command
                // (mirrors the VB EOM handler enabling tmrNotifierSend).
                TryDrainHalfDuplex_NoLock();
            }
        }

        private void TryDrainHalfDuplex_NoLock()
        {
            if (serialport?.IsOpen != true) return;

            // A write is already outstanding — let the ack (NoteHalfDuplexReceive) or the
            // timeout drive the next action. Without this guard, enqueuing a second
            // command (bulk multi-device) would re-send the in-flight one immediately.
            if (_hdAwaitingAck) return;

            if (_hdInFlight == null)
            {
                if (_hdQueue.Count == 0) return;
                _hdInFlight = _hdQueue.Dequeue();
                _hdAttempts = 0;
            }

            // Gate 1: never transmit while a frame is arriving.
            if (_hdReceivingFrame) { ArmHalfDuplexTimer(kHalfDuplexAckTimeout); return; }

            // Gate 2: wait out the inter-frame quiet gap.
            TimeSpan sinceRx = DateTime.Now - _hdLastRx;
            if (sinceRx < kHalfDuplexQuietGap)
            {
                ArmHalfDuplexTimer(kHalfDuplexQuietGap - sinceRx + TimeSpan.FromMilliseconds(5));
                return;
            }

            if (_hdAttempts >= kHalfDuplexMaxAttempts)
            {
                this.NotifyClient($"Half-duplex: no ack after {_hdAttempts} attempts, dropping command", false);
                _hdInFlight = null;
                TryDrainHalfDuplex_NoLock();   // move on to the next queued command
                return;
            }

            try
            {
                serialport.Write(_hdInFlight, 0, _hdInFlight.Length);
                _hdAttempts++;
                _hdAwaitingAck = true;
                string text = Encoding.ASCII.GetString(_hdInFlight).Replace("\r", "");
                this.NotifyClient($"{text} Sent to panel (attempt {_hdAttempts})", false);
            }
            catch (Exception ex)
            {
                this.NotifyClient("Half-duplex send failed: " + ex.Message, false);
            }
            ArmHalfDuplexTimer(kHalfDuplexAckTimeout);   // wait for the panel to answer
        }

        private void ArmHalfDuplexTimer(TimeSpan due)
        {
            // On fire, clear the awaiting-ack guard so TryDrain can resend the in-flight
            // command (if attempts remain) or write the next queued one. Harmless when the
            // timer was armed only as a quiet-gap re-check (guard is already false).
            _hdTimer ??= new Timer(_ =>
            {
                lock (_hdLock) { _hdAwaitingAck = false; TryDrainHalfDuplex_NoLock(); }
            });
            _hdTimer.Change(due, Timeout.InfiniteTimeSpan);
        }
        #endregion

        protected void serialsend(string toSend)
        {
            serialsend(Encoding.ASCII.GetBytes(toSend));
        }

        protected void send_response_amx(int evnum, string message1, string message2, string message3 = "")
        {
            string friendlymessage = message2 + (message3.Length > 0 ? (" " + message3) : "");
            this.NotifyClient(friendlymessage, false);
            CSAMXSingleton.CS.SendAlarmToAMX(evnum, message1, message2, message3);
            CSAMXSingleton.CS.FlushMessages();
        }

        protected void send_response_amx_disable(int evnum, string message1, string message2, string message3, bool on)
        {
            string friendlymessage = message2 + (message3.Length > 0 ? (" " + message3) : "");
            this.NotifyClient(friendlymessage, false);
            CSAMXSingleton.CS.SendAlarmToAMX_disable(evnum, message1, message2, message3, on);
            CSAMXSingleton.CS.FlushMessages();
        }

        // CSV passedvalues format is "node,loop,zone,device". Defaults match the
        // historic init values (node=1, others=0) so missing slots behave as before.
        protected static void ParsePassedValues(string passedvalues, out int node, out int loop, out int zone, out int device)
        {
            node = 1; loop = 0; zone = 0; device = 0;
            string[] parts = (passedvalues ?? "").Split(',');
            if (parts.Length > 0) int.TryParse(parts[0], out node);
            if (parts.Length > 1) int.TryParse(parts[1], out loop);
            if (parts.Length > 2) int.TryParse(parts[2], out zone);
            if (parts.Length > 3) int.TryParse(parts[3], out device);
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
                    if (serialport != null)
                    {
                        serialport.Open();
                    }
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
