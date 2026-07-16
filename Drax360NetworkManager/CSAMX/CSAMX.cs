using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Timers;


namespace DraxTechnology
{
    public class CSAMX
    {
        #region constants

        public event EventHandler OutsideEvents;

        private const int kmaxfilenumber = 1000000;
        private const string csamxfolder = "Temp";
        #endregion

        #region private variables
        private int filenumber = 0;

        private string logfiles = "";
        private string extension = "";
        private List<NVM> nvms = new List<NVM>();
        private readonly object _nvmsLock = new object();
        private readonly ConcurrentDictionary<string, DateTime> _pendingDelete =
            new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        // Tracks when each orphaned file was last re-sent to prevent queue flooding.
        private readonly ConcurrentDictionary<string, DateTime> _resentAt =
            new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private System.Timers.Timer _cleanupTimer;
        #endregion

        #region constructor
        public CSAMX()
        {
        }

        #endregion

        #region public methods
        public void Startup(string logfiles, string extension)
        {
            this.extension = extension;
            this.logfiles = Path.Combine(logfiles, csamxfolder);
            // check if the temp directory exists

            if (!Directory.Exists(this.logfiles))
            {
                Directory.CreateDirectory(this.logfiles);
            }

            // Option 3: delete leftover files from any previous run before resuming.
            foreach (var f in Directory.GetFiles(this.logfiles, "*." + extension))
            {
      //          try { File.Delete(f); } catch { }
            }

            determinelastfilenumber();

            _cleanupTimer = new System.Timers.Timer(10_000) { AutoReset = true };
            _cleanupTimer.Elapsed += CleanupTimerElapsed;
            _cleanupTimer.Enabled = true;
        }

        // Single enqueue chokepoint for outbound NVMs. Adds under _nvmsLock — the
        // same lock FlushMessages snapshots/clears under, so a concurrent panel-
        // thread add can't corrupt the list mid-flush — then mirrors the event to
        // the MQTT sink. The publish is outside the lock, is a no-op unless
        // MqttEnabled is true, and can never throw, so it cannot disturb the AMX path.
        private void AddNvm(NVM ournvm)
        {
            lock (_nvmsLock)
            {
                nvms.Add(ournvm);
            }
            MqttTransfer.Instance.PublishEvent(ournvm, extension);
        }

        public int IncrementInputNumber(int inputNumber)
        {
            return (int)(inputNumber + 0x80000000);
        }
        public int MakeInputNumber(int node, int loop, int inputn, int inputtype, bool on = true)
        {

            int no = inputn + (loop * 0x100) + (node * 0x10000) + (inputtype * 0x8000000);
            if (on)
            {
                no |= unchecked((int)0x80000000);
            }

            return no;
        }

        public void WriteData(NwmData eventtype, int eventnumber,
             string textparameter, string textparameter2, string textparameter3, bool on = true)
        {
            NVM ournvm = new NVM();
            ournvm.OurType = Convert.ToInt32(eventtype);
            ournvm.OurEvent = eventnumber;
            ournvm.On = on ? 1 : 0;

            ournvm.Text = textparameter;
            ournvm.Text2 = textparameter2;
            ournvm.Text3 = textparameter3;

            AddNvm(ournvm);
        }


        public void SendAlarmToAMX(int eventnumber, string dtext = "", string dtext2 = "", string dtext3 = "")
        {
            sendalarmorreset(eventnumber, dtext, dtext2, dtext3, true);
        }
        public void SendAlarmToAMX_disable(int eventnumber, string dtext, string dtext2, string dtext3, bool on)
        {
            sendalarmorreset_disable(eventnumber, dtext, dtext2, dtext3, on);
        }

        public void SendResetToAMX(int eventnumber, string dtext = "", string dtext2 = "", string dtext3 = "")
        {
            sendalarmorreset(eventnumber, dtext, dtext2, dtext3, false);
        }

        // Modern equivalent of legacy NwmForceEvmAttribute (Gen_Netman.dll). Queues
        // a type-17 (ForceEVMAttrToAmx) NVM that AMX uses to set EVM attributes —
        // most commonly attribute 13 = 1 to mark momentary events (Fire Reset,
        // Alarms Silenced/Sounded, Cancel Buzzer) as one-shot so AMX auto-clears.
        public void ForceEvmAttribute(int eventnumber, int attributeBit, int onOff)
        {
            NVM ournvm = new NVM();
            ournvm.OurType = (int)NwmData.ForceEVMAttrToAmx;
            ournvm.OurEvent = eventnumber;
            ournvm.Value = attributeBit;
            ournvm.On = onOff;
            AddNvm(ournvm);
        }

        public void LogMessage(int eventtype, int eventnumber, string text, int ophandle)
        {
            NVM ournvm = new NVM();
            ournvm.OurType = eventtype;
            ournvm.OurEvent = eventnumber;
            ournvm.Text = text;
            ournvm.Spare[0] = ophandle;
            AddNvm(ournvm);
        }

        public void ScheduleDelete(string filename)
        {
            if (!string.IsNullOrEmpty(filename))
            {
                _pendingDelete[filename] = DateTime.UtcNow;
                _lastMakAt = DateTime.UtcNow;
                Notify("MAK received, scheduled delete: " + Path.GetFileName(filename));
            }
        }

        // When the last MAK arrived. A slow AMX still acknowledges — just late —
        // and re-sending files it merely hasn't reached yet doubles its workload
        // exactly when it is struggling (seen live 2026-07-16: 20–60s MAK lag,
        // orphan re-sends compounding the backlog).
        private DateTime _lastMakAt = DateTime.MinValue;

        // Diagnostic surface for the delete/orphan-resend paths. Routes through
        // the same OutsideEvents channel AMXTransfer.NotifyClient uses so the
        // file lifecycle (MAK -> scheduled -> deleted, or orphan -> re-sent)
        // is visible in the log without changing any timing.
        private void Notify(string message)
        {
            OutsideEvents?.Invoke(this, new CustomEventArgs(message, false));
        }

        private void CleanupTimerElapsed(object sender, ElapsedEventArgs e)
        {
            foreach (var kv in _pendingDelete)
            {
                if ((DateTime.UtcNow - kv.Value).TotalSeconds > 1)
                {
                    try
                    {
                        File.Delete(kv.Key);
                        _pendingDelete.TryRemove(kv.Key, out _);
                        _resentAt.TryRemove(kv.Key, out _);
                        Notify("Cleanup deleted (post-MAK): " + Path.GetFileName(kv.Key));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("File Delete Error: " + ex.Message);
                    }
                }
            }

            // Re-send NTX for files that were never MAK'd (written when disconnected,
            // or whose MAK timed out). Any file older than 30 s that isn't already
            // pending deletion is orphaned. Throttle to once per 60 s per file so
            // we don't flood the sender queue on a prolonged outage.
            if (!AMXTransfer.Instance.IsConnected) return;
            if (string.IsNullOrEmpty(logfiles) || string.IsNullOrEmpty(extension)) return;

            // If AMX has acknowledged anything recently it is alive and working
            // through its queue — hold the re-sends rather than double its load.
            // Re-sends resume only after a genuine silence.
            if ((DateTime.UtcNow - _lastMakAt).TotalSeconds < 60) return;

            try
            {
                foreach (var file in Directory.GetFiles(logfiles, "*." + extension))
                {
                    if (_pendingDelete.ContainsKey(file)) continue;

                    var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(file);
                    if (age.TotalSeconds < 30) continue;

                    if (_resentAt.TryGetValue(file, out DateTime lastSent) &&
                        (DateTime.UtcNow - lastSent).TotalSeconds < 60) continue;

                    _resentAt[file] = DateTime.UtcNow;
                    AMXTransfer.Instance.SendMessage("NTX:" + file);
                    Notify($"Orphan re-send (age {(int)age.TotalSeconds}s): " + Path.GetFileName(file));
                }

                // Prune _resentAt for files that have already been deleted.
                foreach (var key in _resentAt.Keys.ToList())
                {
                    if (!File.Exists(key)) _resentAt.TryRemove(key, out _);
                }
            }
            catch { }
        }

        public void FlushMessages()
        {
            List<NVM> toProcess;
            lock (_nvmsLock)
            {
                if (nvms.Count == 0) return;
                toProcess = new List<NVM>(nvms);
                nvms.Clear();
            }

            List<byte> contents = new List<byte>();
            foreach (NVM ournvm in toProcess)
            {
                contents.AddRange(ournvm.RenderBytes());
            }

            // safety check
            if (contents.Count == 0)
            {
                return;
            }

            filenumber++;
            if (filenumber > kmaxfilenumber) filenumber = 1;

            string filename = filenumber.ToString() + "." + extension;
            string fullfilename = Path.Combine(logfiles, filename);

            // Open the file in write mode, changed from append as we need to create a new file on flush

            if (System.IO.File.Exists(fullfilename))
            {
                System.IO.File.Delete(fullfilename);
            }

            using (FileStream fileStream = new FileStream(fullfilename, FileMode.CreateNew, FileAccess.Write))
            {
                byte[] ourbytes = contents.ToArray();
                fileStream.Write(ourbytes, 0, ourbytes.Length);
                fileStream.Close();
                if (AMXTransfer.Instance.IsConnected)
                {
                    AMXTransfer.Instance.SendMessage($"NTX:" + fullfilename);
                }
            }
            //File.Delete(fullfilename);  // If I delete the file straight away then nothing appears on AMX
            var files = Directory.GetFiles(this.logfiles, "*." + extension);
            foreach (var file in files)
            {
                try
                {
                    if (!file.Equals(fullfilename, StringComparison.OrdinalIgnoreCase))
                    {
                        var fileAge = DateTime.Now - File.GetLastWriteTime(file);
                        if (fileAge.TotalMinutes > 5)  //  last resort but should rarely (if ever) fire now.
                        {
                            File.Delete(file);
                        }
                    }
                }
                catch
                { }
            }

        }
        #endregion
        #region private methods
        /// <summary>
        /// gets the last file in the amx folder and increments file number by 1;
        /// </summary>
        private void determinelastfilenumber()
        {
            var dirInfo = new DirectoryInfo(logfiles);
            var allFiles = dirInfo.GetFiles("*." + extension, SearchOption.TopDirectoryOnly);
            FileInfo lastmodifiedfile = allFiles.OrderBy(fi => fi.LastWriteTime).LastOrDefault();
            if (lastmodifiedfile == null) return;

            string[] splits = lastmodifiedfile.Name.Split('.');

            if (splits.Length != 2) return;

            filenumber = Convert.ToInt32(splits[0]);
        }

        public void sendalarmorreset(int eventnumber, string dtext, string dtext2, string dtext3, bool on)
        {
            NVM ournvm = new NVM();
            ournvm.OurType = 1;
            ournvm.OurEvent = eventnumber;
            ournvm.On = on ? 65535 : 0;
            ournvm.Text = dtext;
            ournvm.Text2 = dtext2;
            ournvm.Text3 = dtext3;

            // Called from panel parser threads; AddNvm takes _nvmsLock (the lock
            // FlushMessages snapshots/clears under) so a concurrent add can't
            // corrupt the list mid-flush and drop an alarm/reset event.
            AddNvm(ournvm);
        }

        public void sendalarmorreset_disable(int eventnumber, string dtext, string dtext2, string dtext3, bool on)
        {
            NVM ournvm = new NVM();
            ournvm.OurType = 2;
            ournvm.OurEvent = eventnumber;
            ournvm.On = on ? 65535 : 0;
            ournvm.Text = dtext;
            ournvm.Text2 = dtext2;
            ournvm.Text3 = dtext3;

            // Same lock discipline as sendalarmorreset (AddNvm) — concurrent add vs flush.
            AddNvm(ournvm);
        }
        #endregion
    }
}
