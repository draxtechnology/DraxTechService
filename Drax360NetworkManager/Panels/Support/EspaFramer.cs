using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DraxTechnology.Panels
{
    internal class EspaFramer
    {
        private const byte SOH = 0x01;
        private const byte STX = 0x02;
        private const byte ETX = 0x03;
        private const byte EOT = 0x04;
        private const byte ENQ = 0x05;
        private const byte ACK = 0x06;
        private const byte NAK = 0x15;
        private const byte US = 0x1F;
        private const byte RS = 0x1E;
        private const int OurDeviceNumber = 2;

        public event Action<EspaRecord> FrameReceived;
        public event Action<string> Log;

        private enum State { Idle, Selected, InFrame }

        private State _state = State.Idle;
        private List<byte> _frame = new List<byte>();
        private List<byte> _line = new List<byte>();

        // SELECT detection: panel sends 1<ENQ> immediately followed by 2<ENQ> with no EOT between
        private bool _lastWasInterface1Poll = false;
        private DateTime _lastInterface1PollTime = DateTime.MinValue;

        private readonly Action<byte[], int, int> _write;

        public EspaFramer(Action<byte[], int, int> writeAction)
        {
            _write = writeAction ?? throw new ArgumentNullException(nameof(writeAction));
        }

        public EspaFramer(Action<byte[]> writeAction)
            : this((buf, off, len) =>
            {
                var slice = new byte[len];
                Array.Copy(buf, off, slice, 0, len);
                writeAction(slice);
            })
        { }

        public int Feed(byte[] data)
        {
            int i = 0;
            while (i < data.Length)
            {
                byte b = data[i];

                switch (_state)
                {
                    case State.Idle:

                        if (b == SOH)
                        {
                            // Start of an ESPA frame
                            _line.Clear();
                            _frame.Clear();
                            _frame.Add(b);
                            _state = State.InFrame;
                            i++;
                            break;
                        }

                        if (b == ENQ)
                        {
                            // End of poll line — process it
                            _line.Add(b);
                            string pollLine = Encoding.ASCII.GetString(_line.ToArray());
                            _line.Clear();
                            i++;
                            HandlePollLine(pollLine);
                            break;
                        }

                        if (b == EOT)
                        {
                            // End of transaction — reset
                            _line.Clear();
                            _lastWasInterface1Poll = false;
                            _state = State.Idle;
                            i++;
                            break;
                        }

                        // Printable ASCII and ESPA separator bytes belong to poll line
                        if (b >= 0x20 || b == 0x0D || b == 0x0A ||
                            b == RS || b == US || b == STX)
                        {
                            _line.Add(b);
                            i++;
                            break;
                        }

                        // Unrecognised control byte — stop consuming
                        return i;

                    case State.Selected:

                        if (b == SOH)
                        {
                            LogMsg("SOH received in Selected → switching to InFrame");
                            _line.Clear();
                            _frame.Clear();
                            _frame.Add(b);
                            _state = State.InFrame;
                            i++;
                            break;
                        }

                        if (b == EOT)
                        {
                            LogMsg("EOT in Selected state — panel abandoned transaction");
                            _line.Clear();
                            _lastWasInterface1Poll = false;
                            _state = State.Idle;
                            i++;
                            break;
                        }

                        // Unexpected byte — back to idle without consuming
                        LogMsg($"Unexpected byte 0x{b:X2} in Selected → Idle");
                        _state = State.Idle;
                        break;

                    case State.InFrame:

                        _frame.Add(b);
                        i++;

                        // Byte after ETX is the BCC — frame complete
                        if (_frame.Count >= 2 &&
                            _frame[_frame.Count - 2] == ETX)
                        {
                            ProcessCompleteFrame();
                            _state = State.Idle;
                        }
                        break;
                }
            }
            return i;
        }

        private void HandlePollLine(string line)
        {
            string content = line.TrimEnd((char)ENQ).Trim();
            string hex = BitConverter.ToString(
                Encoding.ASCII.GetBytes(line)).Replace("-", " ");
            LogMsg("Poll HEX: " + hex + " | '" + content + "'");

            // Interface 1 poll — send nothing, set SELECT flag with timestamp
            if (content == "1")
            {
                LogMsg("← POLL+ENQ (ESPA interface 1) → no response");
                _lastWasInterface1Poll = true;
                _lastInterface1PollTime = DateTime.Now;
                return;
            }

            if (content == OurDeviceNumber.ToString())
            {
                // SELECT: interface 1 poll followed within 500ms by our poll, with no EOT between
                bool isSelect = _lastWasInterface1Poll &&
                                (DateTime.Now - _lastInterface1PollTime).TotalMilliseconds < 500;

                _lastWasInterface1Poll = false;

                if (isSelect)
                {
                    LogMsg("← SELECT+ENQ (after interface 1 poll) → ACK");
                    Send(ACK);
                    _state = State.Selected;
                    return;
                }

                LogMsg("← POLL+ENQ (our address, idle) → EOT");
                Send(EOT);
                _state = State.Idle;
                return;
            }

            // Full text fallback (pager tool output)
            string norm = Regex.Replace(content, @"\s+", " ").ToUpperInvariant();
            if (norm.Contains("SELECT") && norm.Contains(OurDeviceNumber.ToString()))
            {
                LogMsg("← SELECT+ENQ (text) → ACK");
                Send(ACK);
                _state = State.Selected;
            }
            else if (norm.Contains("POLL") && norm.Contains(OurDeviceNumber.ToString()))
            {
                LogMsg("← POLL+ENQ (text) → EOT");
                Send(EOT);
                _state = State.Idle;
            }
            else
            {
                LogMsg("← UNKNOWN poll: '" + content + "'");
            }

            _lastWasInterface1Poll = false;
        }

        private void ProcessCompleteFrame()
        {
            if (_frame.Count < 4)
            {
                LogMsg("Frame too short (" + _frame.Count + " bytes) → NAK");
                Send(NAK);
                return;
            }

            // ETX is second-to-last byte, BCC is last
            int etxPos = _frame.Count - 2;
            if (_frame[etxPos] != ETX)
            {
                etxPos = -1;
                for (int i = _frame.Count - 2; i >= 0; i--)
                {
                    if (_frame[i] == ETX) { etxPos = i; break; }
                }
            }

            if (etxPos < 0)
            {
                LogMsg("No ETX found in frame → NAK");
                Send(NAK);
                return;
            }

            // BCC = XOR of all bytes AFTER SOH through ETX inclusive
            // Autronica excludes SOH from the BCC calculation
            byte calculated = 0;
            for (int i = 1; i <= etxPos; i++)
                calculated ^= _frame[i];

            byte received = _frame[etxPos + 1];

            if (calculated != received)
            {
                LogMsg($"BCC mismatch: calc=0x{calculated:X2} recv=0x{received:X2} → NAK");
                Send(NAK);
                return;
            }

            Send(ACK);
            LogMsg("← Frame OK (BCC 0x" + received.ToString("X2") + ") → ACK");

            var record = ParseFrame(_frame, etxPos);
            if (record != null)
            {
                LogMsg("ESPA addr=" + record.PagerAddress + " | " + record.DisplayText);
                FrameReceived?.Invoke(record);
            }
        }

        private EspaRecord ParseFrame(List<byte> frame, int etxPos)
        {
            try
            {
                byte[] body = frame.Take(etxPos + 1).ToArray();
                string raw = Encoding.ASCII.GetString(body);

                var rec = new EspaRecord
                {
                    RawBytes = frame.ToArray(),
                    Timestamp = DateTime.Now
                };

                string[] fields = raw.Split((char)RS);

                foreach (string field in fields)
                {
                    int usIdx = field.IndexOf((char)US);
                    if (usIdx < 0) continue;

                    string tag = field.Substring(0, usIdx)
                                      .TrimStart((char)SOH, (char)STX, ' ')
                                      .Trim();

                    string val = field.Substring(usIdx + 1)
                                      .TrimEnd((char)ETX)
                                      .Trim();

                    switch (tag)
                    {
                        case "1": rec.PagerAddress = val; break;
                        case "2":
                            string[] parts = val.Split((char)US);
                            rec.Line1 = parts.Length > 0
                                ? Regex.Replace(parts[0], @"\s+", " ").Trim()
                                : string.Empty;
                            rec.Line2 = parts.Length > 1
                                ? Regex.Replace(parts[1], @"\s+", " ").Trim()
                                : string.Empty;
                            rec.DisplayText = (rec.Line1 + " " + rec.Line2).Trim();
                            break;
                        case "3": rec.Beeps = val; break;
                        case "4": rec.MessageType = val; break;
                        case "5": rec.Transmission = val; break;
                        case "6": rec.Priority = val; break;
                    }
                }

                return rec;
            }
            catch (Exception ex)
            {
                LogMsg("ParseFrame error: " + ex.Message);
                return null;
            }
        }

        private void Send(byte b) => _write(new[] { b }, 0, 1);

        private void LogMsg(string msg) => Log?.Invoke("[EspaFramer] " + msg);
    }
}
