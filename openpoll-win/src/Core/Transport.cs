using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace MultiPollStudio
{
    public enum ConnectionKind { Serial, Tcp, Udp, SerialOverTcp, SerialOverUdp }

    public enum SerialMode { Rtu, Ascii }

    public static class ConnectionKindInfo
    {
        public static readonly string[] Labels =
        {
            "Serial Port", "Modbus TCP/IP", "Modbus UDP/IP", "Modbus RTU/ASCII Over TCP/IP", "Modbus RTU/ASCII Over UDP/IP",
        };

        public static string Label(this ConnectionKind k) { return Labels[(int)k]; }
        public static bool IsNetwork(this ConnectionKind k) { return k != ConnectionKind.Serial; }
        public static bool UsesMbap(this ConnectionKind k) { return k == ConnectionKind.Tcp || k == ConnectionKind.Udp; }
        public static bool IsUdp(this ConnectionKind k) { return k == ConnectionKind.Udp || k == ConnectionKind.SerialOverUdp; }
    }

    public sealed class ConnectionSettings
    {
        public ConnectionKind Kind = ConnectionKind.Tcp;
        public SerialMode Mode = SerialMode.Rtu;
        public string Port = "COM1";
        public int BaudRate = 9600;
        public int DataBits = 8;
        public Parity Parity = Parity.None;
        public StopBits StopBits = StopBits.One;
        public string Host = "127.0.0.1";
        public int TcpPort = 502;
        public int ConnectTimeoutMs = 3000;
        public int ResponseTimeoutMs = 1000;
        public int DelayBetweenPollsMs = 20;
        public bool RtsToggle;
        public int RtsDisableDelayMs;
        public bool DsrFlow;
        public bool CtsFlow;
        public bool RemoveEcho;

        public ConnectionSettings Clone() { return (ConnectionSettings)MemberwiseClone(); }

        public string Describe()
        {
            if (Kind == ConnectionKind.Serial)
            {
                string parity = Parity == Parity.None ? "N" : Parity == Parity.Even ? "E" : Parity == Parity.Odd ? "O" : Parity.ToString();
                string stop = StopBits == StopBits.Two ? "2" : "1";
                return Port + ": " + BaudRate + "-" + DataBits + "-" + parity + "-" + stop + " " + Mode.ToString().ToUpperInvariant();
            }
            string suffix = Kind.UsesMbap() ? "" : " (" + Mode.ToString().ToUpperInvariant() + ")";
            return Kind.Label() + " " + Host + ":" + TcpPort + suffix;
        }
    }

    /// <summary>Serial line timing from the Modbus over Serial Line spec V1.02 §2.5.1.1: 11 bits per character,
    /// t3.5 strict up to 19200 baud and fixed at 1.750 ms above.</summary>
    public static class SerialTiming
    {
        public static double CharMs(int baud) { return 11000.0 / baud; }

        public static double T35Ms(int baud) { return baud > 19200 ? 1.75 : 3.5 * CharMs(baud); }

        /// <summary>Bytes in the largest response to a read: FC01/02 bit count or FC03/04 registers.</summary>
        public static int ResponseBytes(int function, int quantity, SerialMode mode)
        {
            int data = Pdu.IsBits(function) ? (quantity + 7) / 8 : 2 * quantity;
            int rtu = 1 + 1 + 1 + data + 2;  // slave, fc, byte count, data, CRC
            return mode == SerialMode.Rtu ? rtu : 1 + 2 * (rtu - 1) + 2;  // ':' + hex(body + LRC) + CR LF
        }

        /// <summary>Wire time for request + response at the given baud, in ms.</summary>
        public static double TransactionMs(int baud, int function, int quantity, SerialMode mode)
        {
            int request = mode == SerialMode.Rtu ? 8 : 17;
            return (request + ResponseBytes(function, quantity, mode)) * CharMs(baud) + 2 * T35Ms(baud);
        }

        /// <summary>Suggested response timeout: wire time plus 200 ms device turnaround, doubled for USB adapter and
        /// scheduling latency, rounded up to 100 ms, never below 500 ms.</summary>
        public static int SuggestTimeoutMs(int baud, int function, int quantity, SerialMode mode)
        {
            double ms = 2 * (TransactionMs(baud, function, quantity, mode) + 200);
            return Math.Max(500, (int)(Math.Ceiling(ms / 100) * 100));
        }
    }

    /// <summary>Byte stream abstraction over a serial port or socket.</summary>
    interface IStream : IDisposable
    {
        void Write(byte[] data);
        /// <summary>Returns received bytes, or an empty array when nothing arrived within the timeout.</summary>
        byte[] ReadSome(int timeoutMs);
        void DiscardInput();
    }

    sealed class SerialStream : IStream
    {
        readonly SerialPort _port;
        readonly ConnectionSettings _s;
        readonly byte[] _buf = new byte[512];

        public SerialStream(ConnectionSettings s)
        {
            _s = s;
            _port = new SerialPort(s.Port, s.BaudRate, s.Parity, s.DataBits, s.StopBits)
            {
                Handshake = s.CtsFlow ? Handshake.RequestToSend : Handshake.None,
                DtrEnable = true,
                RtsEnable = false,
                ReadBufferSize = 8192,
            };
            try
            {
                _port.Open();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException
                                       || ex is InvalidOperationException)
            {
                _port.Dispose();
                throw new ModbusException(Status.NoConnection, "Port " + s.Port + " not available: " + ex.Message);
            }
        }

        public void Write(byte[] data)
        {
            try
            {
                if (_s.DsrFlow) WaitDsr();
                if (_s.RtsToggle) _port.RtsEnable = true;
                _port.Write(data, 0, data.Length);
                if (_s.RtsToggle)
                {
                    // SerialPort.Write returns once bytes are queued; wait for the UART to drain them.
                    var sw = Stopwatch.StartNew();
                    while (_port.BytesToWrite > 0 && sw.ElapsedMilliseconds < 2000) Thread.Sleep(1);
                    double charMs = 11000.0 / _s.BaudRate;
                    Thread.Sleep((int)Math.Ceiling(charMs) + _s.RtsDisableDelayMs);
                    _port.RtsEnable = false;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is TimeoutException || ex is InvalidOperationException)
            {
                throw new ModbusException(Status.WriteError, ex.Message);
            }
        }

        void WaitDsr()
        {
            var sw = Stopwatch.StartNew();
            while (!_port.DsrHolding)
            {
                if (sw.ElapsedMilliseconds > _s.ResponseTimeoutMs) throw new ModbusException(Status.WriteError, "DSR not asserted");
                Thread.Sleep(1);
            }
        }

        public byte[] ReadSome(int timeoutMs)
        {
            try
            {
                _port.ReadTimeout = Math.Max(1, timeoutMs);
                int n = _port.Read(_buf, 0, _buf.Length);
                var r = new byte[n];
                Buffer.BlockCopy(_buf, 0, r, 0, n);
                return r;
            }
            catch (TimeoutException)
            {
                return new byte[0];
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException)
            {
                throw new ModbusException(Status.ReadError, ex.Message);
            }
        }

        public void DiscardInput()
        {
            try { _port.DiscardInBuffer(); }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException) { }
        }

        public void Dispose() { _port.Dispose(); }
    }

    sealed class SocketStream : IStream
    {
        readonly Socket _sock;
        readonly bool _udp;
        readonly byte[] _buf = new byte[65536];

        public SocketStream(ConnectionSettings s)
        {
            _udp = s.Kind.IsUdp();
            IPAddress address;
            try
            {
                if (!IPAddress.TryParse(s.Host, out address))
                {
                    IPAddress[] list = Dns.GetHostAddresses(s.Host);
                    address = Array.Find(list, a => a.AddressFamily == AddressFamily.InterNetwork) ?? list[0];
                }
            }
            catch (Exception ex) when (ex is SocketException || ex is ArgumentException || ex is IndexOutOfRangeException)
            {
                throw new ModbusException(Status.NoConnection, "Modbus Connection Failed. Invalid IP Address: " + s.Host);
            }
            _sock = _udp
                ? new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp)
                : new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                if (_udp)
                {
                    _sock.Connect(address, s.TcpPort);
                }
                else
                {
                    IAsyncResult ar = _sock.BeginConnect(address, s.TcpPort, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(s.ConnectTimeoutMs))
                    {
                        _sock.Close();
                        throw new ModbusException(Status.NoConnection, "Modbus Connection Failed. Connect Timeout");
                    }
                    _sock.EndConnect(ar);
                }
            }
            catch (SocketException ex)
            {
                _sock.Close();
                throw new ModbusException(Status.NoConnection, "Modbus Connection Failed. " + ex.Message);
            }
        }

        public void Write(byte[] data)
        {
            try { _sock.Send(data); }
            catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException)
            {
                throw new ModbusException(Status.NoConnection, ex.Message);
            }
        }

        public byte[] ReadSome(int timeoutMs)
        {
            try
            {
                if (!_sock.Poll(Math.Max(1, timeoutMs) * 1000, SelectMode.SelectRead)) return new byte[0];
                int n = _sock.Receive(_buf);
                if (n == 0 && !_udp) throw new ModbusException(Status.NoConnection, "connection closed by remote host");
                var r = new byte[n];
                Buffer.BlockCopy(_buf, 0, r, 0, n);
                return r;
            }
            catch (SocketException ex)
            {
                // UDP: ICMP port unreachable surfaces as ConnectionReset.
                throw new ModbusException(Status.NoConnection, ex.Message);
            }
            catch (ObjectDisposedException ex)
            {
                throw new ModbusException(Status.NoConnection, ex.Message);
            }
        }

        public void DiscardInput()
        {
            try
            {
                while (_sock.Available > 0) _sock.Receive(_buf);
            }
            catch (SocketException) { }
        }

        public void Dispose() { _sock.Close(); }
    }

    /// <summary>Blocking request/response engine. Not thread-safe; one transaction at a time.</summary>
    public sealed class ModbusMaster : IDisposable
    {
        const int BroadcastTurnaroundMs = 100;
        const int RawSilenceMs = 50;

        public ConnectionSettings Settings { get; }
        /// <summary>Raw traffic callback: ("Tx" | "Rx", bytes).</summary>
        public Action<string, byte[]> OnTraffic;

        IStream _stream;
        readonly MemoryStream _rx = new MemoryStream();
        int _transaction;

        public ModbusMaster(ConnectionSettings settings) { Settings = settings; }

        public bool Connected { get { return _stream != null; } }

        public void Connect()
        {
            Close();
            _stream = Settings.Kind == ConnectionKind.Serial ? (IStream)new SerialStream(Settings) : new SocketStream(Settings);
        }

        public void Close()
        {
            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }
            _rx.SetLength(0);
        }

        public void Dispose() { Close(); }

        void Trace(string direction, byte[] data)
        {
            if (data.Length > 0 && OnTraffic != null) OnTraffic(direction, data);
        }

        IStream Stream
        {
            get
            {
                if (_stream == null) throw new ModbusException(Status.NoConnection);
                return _stream;
            }
        }

        void Send(byte[] adu)
        {
            IStream s = Stream;
            s.DiscardInput();
            _rx.SetLength(0);
            Trace("Tx", adu);
            try
            {
                s.Write(adu);
            }
            catch (ModbusException)
            {
                if (Settings.Kind.IsNetwork()) Close();
                throw;
            }
            if (Settings.Kind == ConnectionKind.Serial && Settings.RemoveEcho)
            {
                var echo = new MemoryStream();
                Take(adu.Length, Deadline(), echo);
            }
        }

        long Deadline() { return Environment.TickCount + (long)Settings.ResponseTimeoutMs; }

        static int Remaining(long deadline) { return (int)Math.Max(0, deadline - Environment.TickCount); }

        bool Fill(long deadline)
        {
            int remaining = Remaining(deadline);
            if (remaining <= 0) return false;
            byte[] chunk;
            try
            {
                chunk = Stream.ReadSome(remaining);
            }
            catch (ModbusException ex)
            {
                if (ex.Status == Status.NoConnection) Close();
                throw;
            }
            _rx.Write(chunk, 0, chunk.Length);
            return chunk.Length > 0;
        }

        byte[] Take(int n, long deadline, MemoryStream received)
        {
            while (_rx.Length < n)
            {
                if (!Fill(deadline) && Remaining(deadline) <= 0)
                {
                    byte[] got = Join(received, _rx);
                    Trace("Rx", got);
                    if (got.Length > 0)
                        throw new ModbusException(Status.BytesMissing, "received " + got.Length + " bytes, frame incomplete");
                    throw new ModbusException(Status.Timeout);
                }
            }
            byte[] all = _rx.ToArray();
            var output = new byte[n];
            Buffer.BlockCopy(all, 0, output, 0, n);
            _rx.SetLength(0);
            _rx.Write(all, n, all.Length - n);
            received.Write(output, 0, n);
            return output;
        }

        static byte[] Join(MemoryStream a, MemoryStream b)
        {
            var r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a.ToArray(), 0, r, 0, (int)a.Length);
            Buffer.BlockCopy(b.ToArray(), 0, r, (int)a.Length, (int)b.Length);
            return r;
        }

        byte[] ReceiveRtu(long deadline)
        {
            var received = new MemoryStream();
            byte[] header = Take(3, deadline, received);
            int total;
            try
            {
                total = Framing.RtuResponseLength(header, 3);
            }
            catch (ModbusException)
            {
                Trace("Rx", Join(received, _rx));
                throw;
            }
            if (total == 0) return ReceiveUntilSilence(deadline, received);
            Take(total - 3, deadline, received);
            byte[] frame = received.ToArray();
            Trace("Rx", frame);
            return frame;
        }

        /// <summary>For RTU responses without a length field (FC43): collect bytes until the line has been silent for
        /// t3.5 (at least 20 ms, to absorb USB adapter latency) or the deadline passes.</summary>
        byte[] ReceiveUntilSilence(long deadline, MemoryStream received)
        {
            int silence = Math.Max(20, (int)Math.Ceiling(SerialTiming.T35Ms(Settings.BaudRate)));
            byte[] rest = _rx.ToArray();
            received.Write(rest, 0, rest.Length);
            _rx.SetLength(0);
            while (Remaining(deadline) > 0)
            {
                long gap = Math.Min(deadline, Environment.TickCount + (long)silence);
                if (!Fill(gap)) break;
                byte[] chunk = _rx.ToArray();
                received.Write(chunk, 0, chunk.Length);
                _rx.SetLength(0);
                if (received.Length > 256) break;
            }
            byte[] frame = received.ToArray();
            Trace("Rx", frame);
            return frame;
        }

        byte[] ReceiveAscii(long deadline)
        {
            var received = new MemoryStream();
            while (true)
            {
                byte[] one = Take(1, deadline, new MemoryStream());
                if (received.Length == 0 && one[0] != (byte)':') continue; // skip noise before ':'
                received.WriteByte(one[0]);
                long n = received.Length;
                if (n >= 2 && received.GetBuffer()[n - 2] == '\r' && received.GetBuffer()[n - 1] == '\n') break;
            }
            byte[] frame = received.ToArray();
            Trace("Rx", frame);
            return frame;
        }

        byte[] ReceiveMbap(long deadline)
        {
            var received = new MemoryStream();
            byte[] header = Take(Framing.MbapLength, deadline, received);
            int length = Pdu.Get16(header, 4);
            if (length < 2 || length > 254)
            {
                Trace("Rx", Join(received, _rx));
                throw new ModbusException(Status.ResponseError, "invalid MBAP length " + length);
            }
            Take(length - 1, deadline, received);
            byte[] frame = received.ToArray();
            Trace("Rx", frame);
            return frame;
        }

        /// <summary>Send one request PDU and return the response PDU; null for a serial broadcast.</summary>
        public byte[] Execute(int slave, byte[] pdu)
        {
            int ignored;
            return Execute(slave, pdu, false, out ignored);
        }

        /// <summary>Like <see cref="Execute(int, byte[])"/>; with <paramref name="anyResponder"/> the reply may come from
        /// another address (address-recovery queries to 0xF8/0xFE), reported in <paramref name="responder"/>.</summary>
        public byte[] Execute(int slave, byte[] pdu, bool anyResponder, out int responder)
        {
            if (slave < 0 || slave > 255) throw new ArgumentException("slave id must be 0..255");
            bool mbap = Settings.Kind.UsesMbap();
            if (slave == 0 && !mbap && !Pdu.IsBroadcastable(pdu))
                throw new ArgumentException("broadcast (slave 0) is only allowed for write functions");
            responder = slave;

            byte[] adu;
            if (mbap)
            {
                _transaction = (_transaction + 1) & 0xFFFF;
                adu = Framing.MbapFrame(_transaction, slave, pdu);
            }
            else
            {
                adu = Settings.Mode == SerialMode.Rtu ? Framing.RtuFrame(slave, pdu) : Framing.AsciiFrame(slave, pdu);
            }

            Send(adu);
            if (slave == 0 && !mbap)
            {
                Thread.Sleep(BroadcastTurnaroundMs);
                return null;
            }
            long deadline = Deadline();
            int expect = anyResponder ? -1 : slave;
            byte[] frame;
            if (mbap)
            {
                frame = ReceiveMbap(deadline);
                responder = frame.Length > 6 ? frame[6] : slave;
                return Framing.MbapUnframe(frame, _transaction, expect);
            }
            if (Settings.Mode == SerialMode.Rtu)
            {
                frame = ReceiveRtu(deadline);
                responder = frame.Length > 0 ? frame[0] : slave;
                return Framing.RtuUnframe(frame, expect);
            }
            frame = ReceiveAscii(deadline);
            byte[] pduOut = Framing.AsciiUnframe(frame, expect);
            string text = System.Text.Encoding.ASCII.GetString(frame).Trim();
            responder = text.Length >= 3 ? Convert.ToInt32(text.Substring(1, 2), 16) : slave;
            return pduOut;
        }

        /// <summary>Execute and validate. Returns read values, or null for writes and broadcasts.</summary>
        public int[] Transact(int slave, byte[] pdu)
        {
            byte[] response = Execute(slave, pdu);
            return response == null ? null : Pdu.ParseResponse(pdu, response);
        }

        /// <summary>Send arbitrary bytes and collect whatever arrives (Frame Console). Stops at the response timeout,
        /// or after RawSilenceMs of silence once the first byte arrived.</summary>
        public byte[] RawExchange(byte[] data)
        {
            Send(data);
            long deadline = Deadline();
            while (Remaining(deadline) > 0)
            {
                long gap = _rx.Length > 0 ? Math.Min(deadline, Environment.TickCount + (long)RawSilenceMs) : deadline;
                if (!Fill(gap) && _rx.Length > 0) break;
            }
            byte[] output = _rx.ToArray();
            _rx.SetLength(0);
            Trace("Rx", output);
            return output;
        }
    }
}
