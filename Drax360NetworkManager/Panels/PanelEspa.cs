
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Timers;
using System.Xml.Linq;
using static DraxTechnology.Panels.PanelTaktis;

namespace DraxTechnology.Panels
{
    internal class PanelEspa : AbstractPanel
    {
        #region constants

        const int MAXINPUTSTRINGS = 5;
        const byte kheartbeatdelayseconds = 1;

        #endregion

        public string[] Ip = new string[MAXINPUTSTRINGS];
        public string[] UserMessages = new string[16];
        public int[] UserTypes = new int[16];
        public int giZoneNumber = 0;
        public int giDeviceSubAddress = 0;
        public string gsTextField = "";
        public string gsDeviceText = "";
        public string gsZoneText = "";
        public int giDeviceAddress = 0;
        public int giLoopNumber = 0;
        public bool LocalInputUnit = false;
        public int KSFUseLoop = 0;
        public int index = 0;
        public int giAnalogRequestLoop = 0;

        public override string FakeString
        {
            get
            {
                // two messages are sent, so we return the same message twice
                string msg = "   Pager Text    : |Fire Alarm -ZONE 1 -MAINBUILDING    A1005 -     |" + (char)13 + (char)10;
                msg += "Pager Text    : | AutroGuard  SD |" + (char)13 + (char)10;
                msg += "Pager Ctrl: Beeps = 5, Type = 3, Trans = 3, Pri = 1, " + (char)13 + (char)10;
                msg += "-----------------: 2026 - 04 - 16 13:34:58.152" + (char)13 + (char)10;
                msg += "Pager Address : 999" + (char)13 + (char)10;
                msg += " Pager Text    : | Fire Alarm Fault -AutroGuard  CO_Sounder |" + (char)13 + (char)10;
                msg += " Pager Text: | Missing     addon board                         |" + (char)13 + (char)10;
                msg += " Pager Ctrl: Beeps = 2, Type = 3, Trans = 2, Pri = 3, " + (char)13 + (char)10;
                msg += "-----------------: 2026 - 04 - 16 13:04:11.319" + (char)13 + (char)10;
                return msg;
            }
        }

        public PanelEspa(string baselogfolder, string identifier) : base(baselogfolder, identifier, "KsfMan", "ESPA")
        {
            if (!String.IsNullOrEmpty(identifier))
            {
                heartbeat_timer = new System.Threading.Timer(heartbeat_timer_callback, this.Identifier, 500, kheartbeatdelayseconds * 1000);
                this.Offset = base.GetSetting<int>(ksettingsetupsection, "giAmx1Offset");
                KSFUseLoop = base.GetSetting<int>(ksettingsetupsection, "UseLoop");
            }
        }

        public override void Parse(byte[] buffer)
        {
            base.Parse(buffer);
            int bufferlength = buffer.Length;
            string result = Encoding.UTF8.GetString(buffer);

            if (bufferlength > 0)
            {
                processmessage(result);
            }
        }
        private bool processmessage(string result)
        {
            gsDeviceText = "";
            int giNodeNumber = 1;
            bool on = true;
            int tIpType = 0;
            int p1 = 0;
            int evnum = 0;
            string gAlarmType = "";

            string[] lines = result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                string lineLower = line.ToLower();
                if (lineLower.Contains("fire alarm fault"))
                {
                    tIpType = 8;
                    gsTextField = line.Substring(lineLower.IndexOf("fire alarm fault") + 17);
                    gsTextField = gsTextField.Replace("|", "").Trim();
                }
                else if (lineLower.Contains("fire pre alarm"))
                {
                    tIpType = 2;
                    gsTextField = line.Substring(lineLower.IndexOf("fire pre fault") + 15);
                    gsTextField = gsTextField.Replace("|", "").Trim();
                }
                else if (lineLower.Contains("fire alarm"))
                {
                    tIpType = 0;
                    gsTextField = line.Substring(lineLower.IndexOf("fire alarm") + 12);
                    gsTextField = gsTextField.Replace("|", "").Trim();
                }

                if (gsTextField.Length > 0)
                {
                    try
                    {
                        enmNotAlarmType enumValue = (enmNotAlarmType)Enum.Parse(typeof(enmNotAlarmType), tIpType.ToString());
                        p1 = (int)(enumValue);
                    }
                    catch (Exception ex)
                    {
                        this.NotifyClient("gAlarmType " + gAlarmType + " " + ex.Message, false);
                    }
                    giNodeNumber = 0;
                    giLoopNumber = 0;
                    giDeviceAddress = 0;

                    using var connection = new SqliteConnection("Data Source=events.db");
                    connection.Open();

                    using var command = connection.CreateCommand();
                    command.CommandText = "CREATE TABLE IF NOT EXISTS Events (Id INTEGER PRIMARY KEY AUTOINCREMENT,Node, Loop, Device, Name TEXT UNIQUE)";
                    command.ExecuteNonQuery();


                    // Strip last word from gsTextField to get device text

                    string devicetext = gsTextField.Replace("-", "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";

                    //  Search to see if the device text already exists in the database, if not insert it

                    var selectCmd = connection.CreateCommand();
                    selectCmd.CommandText = @"SELECT Id FROM Events WHERE Name = $name;";
                    selectCmd.Parameters.AddWithValue("$name", devicetext);

                    int id = 0;
                    id = Convert.ToInt32(selectCmd.ExecuteScalar());

                    if (id == 0)
                    {
                        int node = 1;
                        int loop = 1;
                        int device = 0;

                        using var cmd = connection.CreateCommand();
                        cmd.CommandText = @"SELECT node, [loop], device FROM Events ORDER BY node DESC, [loop] DESC, device DESC LIMIT 1;";

                        using var reader = cmd.ExecuteReader();

                        if (reader.Read())
                        {
                            node = reader.GetInt32(0);
                            loop = reader.GetInt32(1);
                            device = reader.GetInt32(2);
                        }

                        // Increment logic
                        device++;

                        if (device > 254)
                        {
                            device = 1;
                            loop++;

                            if (loop > 254)
                            {
                                loop = 1;
                                node++;

                                if (node > 254)
                                {
                                    throw new Exception("Maximum node/loop/device limit reached");
                                }
                            }
                        }

                        giNodeNumber = node;
                        giLoopNumber = loop;
                        giDeviceAddress = device;
                        CreateEventId(connection, giNodeNumber.ToString(), giLoopNumber.ToString(), giDeviceAddress.ToString(), devicetext);
                    }
                    else
                    {
                        selectCmd = connection.CreateCommand();
                        selectCmd.CommandText = @"SELECT node, [loop], device FROM Events WHERE id = $id;";

                        selectCmd.Parameters.AddWithValue("$id", id);

                        using var reader = selectCmd.ExecuteReader();

                        if (reader.Read())
                        {
                            giNodeNumber = reader.GetInt32(0);
                            giLoopNumber = reader.GetInt32(1);
                            giDeviceAddress = reader.GetInt32(2);
                        }
                    }
                    connection.Close();

                    evnum = CSAMXSingleton.CS.MakeInputNumber(giNodeNumber, giLoopNumber, giDeviceAddress, p1, on);

                    base.NotifyClient("Send to AMX: Node = " + (giNodeNumber + this.Offset) + " Loop = " + giLoopNumber + " Address = " + giDeviceAddress);
                    send_response_amx_and_serial(evnum, gsTextField, "", gsDeviceText);
                    Thread.Sleep(1000); // wait for 1 second before processing the next line
                }
            }
            return true;
        }
        public bool CreateEventId(SqliteConnection conn, string node, string loop, string device ,string name)
        {
            // Try insert (ignore if exists)
            var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = "INSERT OR IGNORE INTO Events(node, loop, device, Name) VALUES($node, $loop, $device, $name);";
            insertCmd.Parameters.Add("$node", SqliteType.Text).Value = node;
            insertCmd.Parameters.Add("$loop", SqliteType.Text).Value = loop;
            insertCmd.Parameters.Add("$device", SqliteType.Text).Value = device;
            insertCmd.Parameters.AddWithValue("$name", name);
            insertCmd.ExecuteNonQuery();
            return true;
        }

        private void send_response_amx_and_serial(int evnum, string message1, string message2, string message3 = "")
        {
            string friendlymessage = message2 + (message3.Length > 0 ? (" " + message3) : "");

            // Signal the event back to the main service, so that it can be logged
            this.NotifyClient(friendlymessage, false);

            CSAMXSingleton.CS.SendAlarmToAMX(evnum, message1, message2, message3);
            CSAMXSingleton.CS.FlushMessages();
        }

        protected override void heartbeat_timer_callback(object sender)
        {
            //base.heartbeat_timer_callback(sender);

            // send_message(ActionType.KHandShake, NwmData.AlarmToAmx, "0,0,0,0");
        }

        public override void StartUp(int fakemode)
        {
            int setttingbaudrate = base.GetSetting<int>(ksettingsyncrosection, "BaudRate");
            string settingparity = base.GetSetting<string>(ksettingsyncrosection, "Parity");
            int settingdatabits = base.GetSetting<int>(ksettingsyncrosection, "DataBits");
            int settingstopbits = base.GetSetting<int>(ksettingsyncrosection, "StopBits");

            if (fakemode > 0)
            {
                return;
            }

            // we are a real serial port 
            serialport = new SerialPort(this.Identifier);
            serialport.BaudRate = setttingbaudrate;

            Parity parity = Parity.None;
            string friendlyparity = settingparity.Substring(0, 1).ToUpper();
            if (friendlyparity == "E")
                parity = Parity.Even;
            if (friendlyparity == "O")
                parity = Parity.Odd;

            serialport.Parity = parity;

            serialport.DataBits = settingdatabits;
            serialport.StopBits = (StopBits)settingstopbits;
            serialport.Handshake = Handshake.None;
            serialport.DataReceived += SerialPort_Datareceived;
            if (serialport.IsOpen)
            {
                serialport.Close();
            }
            base.NotifyClient("Attempting Open " + serialport.PortName, false);
            serialport.Encoding = System.Text.Encoding.ASCII;
            serialport.DtrEnable = true;

            serialport.ReadBufferSize = 8000;

            serialport.ReadTimeout = 500;
            serialport.ParityReplace = (byte)0;
            serialport.ReceivedBytesThreshold = 8;
            try
            {
                serialport.Open();
            }
            catch (Exception e)
            {
                base.NotifyClient("Failed To Open " + serialport.PortName + " " + e.ToString(), false);
            }

            if (serialport.IsOpen)
            {
                serialport.DiscardInBuffer();
                serialport.DiscardOutBuffer();
            }
        }
        public override void Evacuate(string passedvalues)
        {
        }
        public override void Alert(string passedvalues)
        {
        }
        public override void EvacuateNetwork(string passedvalues)
        {
        }
        public override void Silence(string passedvalues)
        {
        }
        public override void MuteBuzzers(string passedvalues)
        {
        }
        public override void Reset(string passedvalues)
        {
        }
        public override void DisableDevice(string passedvalues)
        {
        }
        public override void EnableDevice(string passedvalues)
        {
        }
        public override void DisableZone(string passedvalues)
        {
        }
        public override void EnableZone(string passedvalues)
        {
        }
        public override void Analogue(string passedvalues)
        {
        }

        private readonly List<byte> _buffer = new List<byte>();
        private readonly byte[] _terminator = { 0x0D, 0x0A, 0x0D, 0x0A }; // \r\n\r\n

        public override void SerialPort_Datareceived(object sender, SerialDataReceivedEventArgs e)
        {
            Thread.Sleep(500); // wait for more data
            int bytesToRead = serialport.BytesToRead;
            if (bytesToRead <= 0) return;

            byte[] incoming = new byte[bytesToRead];
            int read = serialport.Read(incoming, 0, bytesToRead);
            if (read <= 0) return;

            lock (_buffer)
            {
                _buffer.AddRange(incoming);
                ExtractMessages();
            }
        }

        private void ExtractMessages()
        {
            while (true)
            {
                int pos = FindPattern(_buffer, _terminator);
                if (pos == -1)
                {
                    // Now deal with specific message types
                    if (_buffer.Count >= 4 && _buffer[3].ToString() == "68")
                    {
                        int DeviceAnalogueValue = _buffer[7];
                        int deviceNode = _buffer[2];
                        int DeviceLoop = giAnalogRequestLoop + 1;
                        base.NotifyClient("Analogue Node Received: " + deviceNode, false);
                        base.NotifyClient("Analogue Address Received: " + _buffer[6], false);
                        base.NotifyClient("Analogue Value Received: " + DeviceAnalogueValue, false);
                        //string sLavFileName = GetAnalogStoreName(deviceNode, DeviceLoop);
                    }
                    else
                    {
                        if (_buffer.Count >= 5 && _buffer[4].ToString() == "68")
                        {
                            int DeviceAnalogueValue = _buffer[8];
                            int deviceNode = _buffer[2];
                            int DeviceLoop = giAnalogRequestLoop + 1;
                            base.NotifyClient("Analogue Node Received: " + _buffer[3], false);
                            base.NotifyClient("Analogue Address Received: " + _buffer[7], false);
                            base.NotifyClient("Analogue Value Received: " + DeviceAnalogueValue, false);
                            //string sLavFileName = GetAnalogStoreName(deviceNode, DeviceLoop);
                        }
                        else
                        {
                            return;  // no complete message yet
                        }
                    }
                }

                int end = pos + _terminator.Length;
                byte[] message = _buffer.Take(end).ToArray();

                _buffer.RemoveRange(0, end);
                Parse(message);
            }
        }

        private int FindPattern(List<byte> buffer, byte[] pattern)
        {
            for (int i = 0; i <= buffer.Count - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (buffer[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }
    }
}