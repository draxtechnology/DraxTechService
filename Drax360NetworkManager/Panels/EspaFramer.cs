using System;
using System.Collections.Generic;

namespace DraxTechnology.Panels
{
    /// <summary>
    /// Receive-side ESPA 4.4.4 framer. Acts as the Called Unit (CdU) — the pager
    /// terminal that the fire panel (Calling Unit) talks to. Drives a small state
    /// machine over a byte stream from the serial port:
    ///
    ///     Idle           &lt;ENQ&gt; → reply &lt;ACK&gt;, → AwaitData
    ///     AwaitData      &lt;SOH&gt; → start collecting frame, → ReadingFrame
    ///                    &lt;EOT&gt; → back to Idle
    ///     ReadingFrame   accumulate bytes; on &lt;ETX&gt; expect 1 BCC byte to finish.
    ///                    Validate BCC (XOR of bytes from STX through ETX inclusive).
    ///                    On good: raise FrameReceived(record), reply &lt;ACK&gt;.
    ///                    On bad : reply &lt;NAK&gt;.
    ///                    → AwaitData
    ///
    /// The framer does not own the serial port. It receives bytes via Feed and
    /// emits replies via the writeOut callback supplied at construction. That
    /// keeps PanelEspa in charge of the SerialPort lifecycle while making the
    /// framer independently testable with a string→byte stub.
    /// </summary>
    internal sealed class EspaFramer
    {
        private const byte SOH = 0x01;
        private const byte STX = 0x02;
        private const byte ETX = 0x03;
        private const byte EOT = 0x04;
        private const byte ENQ = 0x05;
        private const byte ACK = 0x06;
        private const byte NAK = 0x15;

        private enum State { Idle, AwaitData, ReadingFrame, AwaitBcc }

        private readonly Action<byte[]> _writeOut;
        private readonly List<byte> _frame = new List<byte>(256);
        private State _state = State.Idle;
        private byte _bccAccum;
        private bool _bccActive;

        public event Action<EspaRecord> FrameReceived;
        public event Action<string> Log;

        public EspaFramer(Action<byte[]> writeOut)
        {
            _writeOut = writeOut ?? throw new ArgumentNullException(nameof(writeOut));
        }

        /// <summary>
        /// Feed a chunk of incoming bytes from the serial port. Returns the
        /// number of bytes consumed by the framer; any leading bytes that the
        /// framer didn't recognise as ESPA control characters are returned as
        /// 0-consumed so the caller can route them to the legacy log-scrape
        /// path. Once a session has started (ENQ seen) the framer consumes
        /// every byte until the next EOT.
        /// </summary>
        public int Feed(byte[] data)
        {
            if (data == null || data.Length == 0) return 0;
            int consumed = 0;
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];

                if (_state == State.Idle)
                {
                    if (b == ENQ)
                    {
                        _writeOut(new[] { ACK });
                        _state = State.AwaitData;
                        EmitLog("ESPA: ENQ → ACK");
                        consumed++;
                        continue;
                    }
                    // Not an ESPA control byte while idle — let the caller deal.
                    return consumed;
                }

                consumed++;

                switch (_state)
                {
                    case State.AwaitData:
                        if (b == SOH)
                        {
                            // ESPA 4.4.4 BCC: XOR of every byte after SOH, up to
                            // and including ETX (the BCC byte itself excluded).
                            // Activate accumulation here; the byte between SOH
                            // and STX (block number) gets folded in too.
                            _frame.Clear();
                            _bccAccum = 0;
                            _bccActive = true;
                            _state = State.ReadingFrame;
                        }
                        else if (b == EOT)
                        {
                            _state = State.Idle;
                            EmitLog("ESPA: EOT → Idle");
                        }
                        // anything else (including stray ENQ) ignored.
                        break;

                    case State.ReadingFrame:
                        if (b == ETX)
                        {
                            _bccAccum ^= b;       // ETX is included in BCC
                            _bccActive = false;    // BCC byte itself is not
                            _state = State.AwaitBcc;
                        }
                        else
                        {
                            if (_bccActive) _bccAccum ^= b;
                            if (b == STX)
                            {
                                // Discard the SOH-block-number prefix; payload
                                // starts after STX.
                                _frame.Clear();
                            }
                            else if (b == EOT)
                            {
                                // Unexpected EOT mid-frame; reset cleanly.
                                _frame.Clear();
                                _bccActive = false;
                                _state = State.Idle;
                                EmitLog("ESPA: EOT mid-frame → Idle");
                            }
                            else
                            {
                                _frame.Add(b);
                            }
                        }
                        break;

                    case State.AwaitBcc:
                        bool ok = (b == _bccAccum);
                        if (ok)
                        {
                            try
                            {
                                var rec = EspaRecord.Parse(_frame.ToArray());
                                _writeOut(new[] { ACK });
                                EmitLog("ESPA: frame OK (" + _frame.Count + "B) → ACK; " + rec);
                                FrameReceived?.Invoke(rec);
                            }
                            catch (Exception ex)
                            {
                                _writeOut(new[] { NAK });
                                EmitLog("ESPA: frame parse error → NAK; " + ex.Message);
                            }
                        }
                        else
                        {
                            _writeOut(new[] { NAK });
                            EmitLog("ESPA: BCC mismatch (got 0x" + b.ToString("X2") +
                                    ", expected 0x" + _bccAccum.ToString("X2") + ") → NAK");
                        }
                        _frame.Clear();
                        _bccActive = false;
                        _state = State.AwaitData;
                        break;
                }
            }
            return consumed;
        }

        public void Reset()
        {
            _state = State.Idle;
            _frame.Clear();
            _bccAccum = 0;
            _bccActive = false;
        }

        private void EmitLog(string msg)
        {
            Log?.Invoke(msg);
        }
    }
}
