using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MultiPollStudio
{
    /// <summary>Per-window counters and link KPIs: request/error counts, response times, rolling success rate.</summary>
    public sealed class PollStats
    {
        public const int Window = 100;

        public int Tx;
        public int Errors;
        public Dictionary<Status, int> ByStatus = new Dictionary<Status, int>();
        /// <summary>Response time of the last successful transaction, ms; -1 before the first.</summary>
        public double LastRttMs = -1;
        public double MaxRttMs;
        double _rttSum;
        int _rttCount;
        /// <summary>Outcome of the last <see cref="Window"/> polls (true = ok), oldest first.</summary>
        Queue<bool> _recent = new Queue<bool>();
        int _recentOk;
        /// <summary>Consecutive failed polls since the last success.</summary>
        public int ConsecutiveErrors;
        public DateTime LastOkUtc = DateTime.MinValue;

        public double AvgRttMs { get { return _rttCount == 0 ? -1 : _rttSum / _rttCount; } }
        public int RecentCount { get { return _recent.Count; } }
        /// <summary>Success rate over the last <see cref="Window"/> polls, 0..100; -1 before the first poll.</summary>
        public double SuccessPercent { get { return _recent.Count == 0 ? -1 : 100.0 * _recentOk / _recent.Count; } }

        public void Record(Status s, double rttMs = -1)
        {
            Tx++;
            bool ok = s == Status.Ok;
            _recent.Enqueue(ok);
            if (ok) _recentOk++;
            if (_recent.Count > Window && _recent.Dequeue()) _recentOk--;
            if (ok)
            {
                ConsecutiveErrors = 0;
                LastOkUtc = DateTime.UtcNow;
                if (rttMs >= 0)
                {
                    LastRttMs = rttMs;
                    if (rttMs > MaxRttMs) MaxRttMs = rttMs;
                    _rttSum += rttMs;
                    _rttCount++;
                }
                return;
            }
            ConsecutiveErrors++;
            Errors++;
            int n;
            ByStatus.TryGetValue(s, out n);
            ByStatus[s] = n + 1;
        }

        public void Reset()
        {
            Tx = 0;
            Errors = 0;
            ByStatus.Clear();
            LastRttMs = -1;
            MaxRttMs = 0;
            _rttSum = 0;
            _rttCount = 0;
            _recent.Clear();
            _recentOk = 0;
            ConsecutiveErrors = 0;
        }

        public PollStats Copy()
        {
            var c = (PollStats)MemberwiseClone();
            c.ByStatus = new Dictionary<Status, int>(ByStatus);
            c._recent = new Queue<bool>(_recent);
            return c;
        }
    }

    public sealed class PollResult
    {
        public int PollId;
        public PollDefinition Definition;
        public Status Status;
        public int[] Values;
        public string Detail = "";
        /// <summary>UTC time the response (or failure) was observed.</summary>
        public DateTime Timestamp;
        /// <summary>Request-to-response time in ms; -1 when no response arrived.</summary>
        public double RttMs = -1;
    }

    /// <summary>One worker thread owns the ModbusMaster and serializes every bus transaction. Queued one-shot jobs
    /// (writes, Frame Console, read-once) run before periodic polls; each window keeps its own scan rate; the connection's
    /// minimum delay between polls is enforced. Callbacks run on the worker thread; GUI code must marshal them.</summary>
    public sealed class PollEngine
    {
        /// <summary>Reconnect back-off: starts here, doubles per failed attempt up to the cap, ±10 % jitter.</summary>
        public const int ReconnectInitialMs = 2000;
        public const int ReconnectMaxMs = 30000;

        sealed class Poll
        {
            public int Id;
            public PollDefinition Definition;
            public Action<PollResult> Callback;
            public readonly PollStats Stats = new PollStats();
            public long NextDue;
            public bool Once;
        }

        sealed class Job
        {
            public Func<ModbusMaster, object> Work;
            public TaskCompletionSource<object> Completion;
        }

        readonly object _lock = new object();
        readonly Dictionary<int, Poll> _polls = new Dictionary<int, Poll>();
        readonly Queue<Job> _jobs = new Queue<Job>();
        ModbusMaster _master;
        Thread _thread;
        volatile bool _running;
        int _nextId = 1;
        long _lastTx;
        long _lastConnectAttempt;
        int _reconnectDelayMs = ReconnectInitialMs;
        readonly Random _jitter = new Random();

        /// <summary>Delay before the next reconnect attempt, for tests and status display.</summary>
        public int ReconnectDelayMs { get { return _reconnectDelayMs; } }

        public ConnectionSettings Settings { get; private set; }
        public Action<string, byte[]> OnTraffic;
        /// <summary>(connected, text) on connect, disconnect and link loss/recovery.</summary>
        public Action<bool, string> OnConnection;

        public bool Running { get { return _running; } }

        /// <summary>Number of registered polls (windows); idle per-window engines are released when it drops to 0.</summary>
        public int PollCount { get { lock (_lock) return _polls.Count; } }

        static long Now() { return Environment.TickCount; }

        // -- lifecycle ----------------------------------------------------------------

        /// <summary>Open the connection synchronously (throws ModbusException on failure), then start polling.</summary>
        public void Start(ConnectionSettings settings)
        {
            Stop();
            var master = new ModbusMaster(settings.Clone()) { OnTraffic = (d, b) => { var h = OnTraffic; if (h != null) h(d, b); } };
            master.Connect();
            lock (_lock)
            {
                Settings = master.Settings;
                _master = master;
                _running = true;
                foreach (Poll p in _polls.Values) p.NextDue = 0;
                _reconnectDelayMs = ReconnectInitialMs;
            }
            _thread = new Thread(Run) { IsBackground = true, Name = "multipoll-engine" };
            _thread.Start();
            Notify(true, Settings.Describe());
        }

        public void Stop()
        {
            bool was;
            lock (_lock)
            {
                was = _running;
                _running = false;
                Monitor.PulseAll(_lock);
            }
            if (_thread != null)
            {
                _thread.Join(5000);
                _thread = null;
            }
            if (_master != null)
            {
                _master.Close();
                _master = null;
            }
            lock (_lock)
            {
                while (_jobs.Count > 0) _jobs.Dequeue().Completion.TrySetException(new ModbusException(Status.NoConnection));
            }
            if (was) Notify(false, "Disconnected");
        }

        void Notify(bool up, string text)
        {
            var h = OnConnection;
            if (h != null) h(up, text);
        }

        // -- polls --------------------------------------------------------------------

        public int AddPoll(PollDefinition definition, Action<PollResult> callback)
        {
            lock (_lock)
            {
                int id = _nextId++;
                _polls[id] = new Poll { Id = id, Definition = definition, Callback = callback };
                Monitor.PulseAll(_lock);
                return id;
            }
        }

        public void UpdatePoll(int id, PollDefinition definition)
        {
            lock (_lock)
            {
                Poll p = _polls[id];
                p.Definition = definition;
                p.NextDue = 0;
                Monitor.PulseAll(_lock);
            }
        }

        public void RemovePoll(int id)
        {
            lock (_lock) _polls.Remove(id);
        }

        public void ReadOnce(int id)
        {
            lock (_lock)
            {
                Poll p;
                if (!_polls.TryGetValue(id, out p)) return;
                p.Once = true;
                Monitor.PulseAll(_lock);
            }
        }

        /// <summary>Snapshot of the counters (copied under the engine lock).</summary>
        public PollStats Stats(int id)
        {
            lock (_lock) return _polls[id].Stats.Copy();
        }

        public void ResetStats(int id)
        {
            lock (_lock) _polls[id].Stats.Reset();
        }

        // -- one-shot jobs ------------------------------------------------------------

        public Task<object> Submit(Func<ModbusMaster, object> work)
        {
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                if (!_running)
                {
                    tcs.SetException(new ModbusException(Status.NoConnection));
                    return tcs.Task;
                }
                _jobs.Enqueue(new Job { Work = work, Completion = tcs });
                Monitor.PulseAll(_lock);
            }
            return tcs.Task;
        }

        // -- write safeguards ------------------------------------------------------------

        /// <summary>When true, every write (FC05/06/15/16/22/23, broadcast, and Frame Console raw frames that carry a
        /// write function) is refused before it reaches the bus. This protects against operator error; it is not a
        /// security control.</summary>
        public volatile bool WriteProtected;

        /// <summary>Audit trail for write attempts; null disables auditing.</summary>
        public AuditLog Audit;

        string Target { get { return Settings == null ? "" : Settings.Describe(); } }

        /// <summary>Send a write request. Refused with a <see cref="WriteBlockedException"/> while write protection is on.
        /// Every attempt, blocked or sent, is recorded in the audit log with the value read just before the write
        /// (for FC06/16/23 and FC05/15 when readable).</summary>
        public Task<object> Write(int slave, byte[] pdu)
        {
            int address;
            int[] values;
            string what;
            bool isWrite = WriteInfo.IsWrite(pdu);
            if (isWrite) WriteInfo.Decode(pdu, out address, out values, out what);
            else { address = 0; values = null; }
            if (isWrite && WriteProtected)
            {
                if (Audit != null) Audit.Record(Target, slave, pdu[0], address, values, null, "BLOCKED: write protection on");
                var tcs = new TaskCompletionSource<object>();
                tcs.SetException(new WriteBlockedException());
                return tcs.Task;
            }
            return Submit(m =>
            {
                int[] before = isWrite && Audit != null && slave != 0 ? TryReadBefore(m, slave, pdu[0], address, values.Length) : null;
                try
                {
                    object r = m.Transact(slave, pdu);
                    if (isWrite && Audit != null) Audit.Record(Target, slave, pdu[0], address, values, before, slave == 0 ? "SENT (broadcast)" : "OK");
                    return r;
                }
                catch (ModbusException ex)
                {
                    if (isWrite && Audit != null) Audit.Record(Target, slave, pdu[0], address, values, before, ex.Describe());
                    throw;
                }
            });
        }

        /// <summary>Best-effort read of the current value(s) before a write, for the audit record.</summary>
        static int[] TryReadBefore(ModbusMaster m, int slave, int fc, int address, int count)
        {
            int readFc = fc == 5 || fc == 15 ? 1 : 3;
            if (fc == 22) count = 1;
            if (count < 1 || count > (readFc == 1 ? Pdu.MaxReadBits : Pdu.MaxReadRegisters)) return null;
            try { return m.Transact(slave, Pdu.ReadRequest(readFc, address, count)); }
            catch (ModbusException) { return null; }
            catch (ArgumentException) { return null; }
        }

        /// <summary>Raw Frame Console frame. Under write protection only frames that are known to be read-only are sent
        /// (allow-list, see <see cref="IsReadOnlyRaw"/>); anything else, including unparseable frames, broadcasts and
        /// FC08 sub-functions that restart the port or force listen-only mode, is refused. Non-read frames are audited.</summary>
        public Task<object> Raw(byte[] data)
        {
            ConnectionKind kind = Settings == null ? ConnectionKind.Tcp : Settings.Kind;
            SerialMode mode = Settings == null ? SerialMode.Rtu : Settings.Mode;
            bool readOnly = IsReadOnlyRaw(data, kind, mode);
            int fc = RawFunction(data, kind, mode);
            if (!readOnly && WriteProtected)
            {
                if (Audit != null) Audit.Record(Target, -1, fc, -1, null, null, "BLOCKED: write protection on (raw " + Hex.Format(data) + ")");
                var tcs = new TaskCompletionSource<object>();
                tcs.SetException(new WriteBlockedException());
                return tcs.Task;
            }
            return Submit(m =>
            {
                object r = m.RawExchange(data);
                if (!readOnly && Audit != null) Audit.Record(Target, -1, fc, -1, null, null, "RAW SENT " + Hex.Format(data));
                return r;
            });
        }

        /// <summary>Unit/slave byte, function byte and the two bytes after it, located by framing (MBAP: bytes 6/7,
        /// RTU: bytes 0/1, ASCII: hex pairs after ':'). Returns false when the frame is too short or not hex.</summary>
        static bool RawHeader(byte[] data, ConnectionKind kind, SerialMode mode, out int unit, out int fc, out int sub)
        {
            unit = fc = sub = -1;
            byte[] body;
            if (kind.UsesMbap())
            {
                if (data.Length < 8) return false;
                body = new byte[data.Length - 6];
                Buffer.BlockCopy(data, 6, body, 0, body.Length);
            }
            else if (mode == SerialMode.Ascii)
            {
                if (data.Length < 5 || data[0] != (byte)':') return false;
                string hex = System.Text.Encoding.ASCII.GetString(data, 1, data.Length - 1).TrimEnd('\r', '\n');
                if (hex.Length % 2 != 0) hex = hex.Substring(0, hex.Length - 1);
                try { body = Hex.Parse(hex); }
                catch (FormatException) { return false; }
            }
            else
            {
                if (data.Length < 2) return false;
                body = data;
            }
            if (body.Length < 2) return false;
            unit = body[0];
            fc = body[1];
            sub = body.Length >= 4 ? (body[2] << 8) | body[3] : -1;
            return true;
        }

        /// <summary>Function code of a raw frame, or -1 when it cannot be determined.</summary>
        public static int RawFunction(byte[] data, ConnectionKind kind, SerialMode mode)
        {
            int unit, fc, sub;
            return RawHeader(data, kind, mode, out unit, out fc, out sub) ? fc : -1;
        }

        /// <summary>True only for frames that cannot change device state: FC01–04, 07, 11, 17, 43 (MEI 14 only), and FC08
        /// sub-functions 00 (echo) and 0x0B–0x12 (read counters). Serial broadcasts (unit 0) are never read-only.
        /// FC08 sub-functions 01 (restart), 04 (force listen-only), 0A (clear counters) and 0x14 change state
        /// (Modbus Application Protocol V1.1b3 §6.8).</summary>
        public static bool IsReadOnlyRaw(byte[] data, ConnectionKind kind, SerialMode mode)
        {
            int unit, fc, sub;
            if (!RawHeader(data, kind, mode, out unit, out fc, out sub)) return false;
            if (unit == 0 && !kind.UsesMbap()) return false;
            switch (fc)
            {
                case 1: case 2: case 3: case 4: case 7: case 11: case 17:
                    return true;
                case 8:
                    return sub == 0x0000 || (sub >= 0x000B && sub <= 0x0012);
                case 43:
                    return sub >= 0 && (sub >> 8) == 14; // MEI type 14 = Read Device Identification
                default:
                    return false;
            }
        }

        // -- worker -------------------------------------------------------------------

        void RespectMinDelay()
        {
            long wait = _lastTx + Settings.DelayBetweenPollsMs - Now();
            if (wait > 0) Thread.Sleep((int)wait);
        }

        /// <summary>Reconnect a dropped network link. Attempts back off exponentially (2 s → 30 s, ±10 % jitter)
        /// so a dead device does not get hammered, and reset to 2 s after a successful connect.</summary>
        bool EnsureLink()
        {
            if (_master.Connected) return true;
            long now = Now();
            if (now - _lastConnectAttempt < _reconnectDelayMs) return false;
            _lastConnectAttempt = now;
            try
            {
                _master.Connect();
            }
            catch (ModbusException ex)
            {
                int next = Math.Min(ReconnectMaxMs, _reconnectDelayMs * 2);
                _reconnectDelayMs = (int)(next * (0.9 + 0.2 * _jitter.NextDouble()));
                Notify(false, ex.Describe() + " — retry in " + (_reconnectDelayMs / 1000.0).ToString("0.0") + " s");
                return false;
            }
            _reconnectDelayMs = ReconnectInitialMs;
            Notify(true, Settings.Describe());
            return true;
        }

        void Run()
        {
            while (true)
            {
                Job job = null;
                Poll poll = null;
                lock (_lock)
                {
                    if (!_running) return;
                    if (_jobs.Count > 0)
                    {
                        job = _jobs.Dequeue();
                    }
                    else
                    {
                        long now = Now();
                        poll = PickPoll(now);
                        if (poll == null)
                        {
                            long nearest = long.MaxValue;
                            foreach (Poll p in _polls.Values)
                                if (p.Definition.Enabled && p.NextDue < nearest) nearest = p.NextDue;
                            int timeout = nearest == long.MaxValue ? 500 : (int)Math.Min(500, Math.Max(1, nearest - now));
                            Monitor.Wait(_lock, timeout);
                            continue;
                        }
                    }
                }
                if (job != null) RunJob(job);
                else RunPoll(poll);
            }
        }

        /// <summary>Read-once requests first, then the enabled poll that has been due the longest. Caller holds the lock.</summary>
        Poll PickPoll(long now)
        {
            Poll best = null;
            foreach (Poll p in _polls.Values)
            {
                bool ready = p.Once || (p.Definition.Enabled && p.NextDue <= now);
                if (!ready) continue;
                if (best == null || (p.Once && !best.Once) || (p.Once == best.Once && p.NextDue < best.NextDue)) best = p;
            }
            return best;
        }

        void RunJob(Job job)
        {
            RespectMinDelay();
            try
            {
                if (!EnsureLink()) throw new ModbusException(Status.NoConnection);
                job.Completion.TrySetResult(job.Work(_master));
            }
            catch (Exception ex)
            {
                job.Completion.TrySetException(ex);
            }
            finally
            {
                _lastTx = Now();
            }
        }

        void RunPoll(Poll poll)
        {
            PollDefinition d;
            lock (_lock)
            {
                d = poll.Definition;
                poll.Once = false;
                poll.NextDue = Now() + d.ScanRateMs;
            }
            RespectMinDelay();
            var result = new PollResult { PollId = poll.Id, Definition = d };
            var sw = new System.Diagnostics.Stopwatch();
            try
            {
                if (!EnsureLink()) throw new ModbusException(Status.NoConnection);
                byte[] request = Pdu.ReadRequest(d.Function, d.Address, d.Quantity);
                sw.Start();
                result.Values = _master.Transact(d.SlaveId, request);
                result.RttMs = sw.Elapsed.TotalMilliseconds;
                result.Status = Status.Ok;
            }
            catch (ModbusException ex)
            {
                result.Status = ex.Status;
                result.Detail = ex.Detail;
            }
            catch (ArgumentException ex)
            {
                result.Status = Status.ResponseError;
                result.Detail = ex.Message;
            }
            finally
            {
                _lastTx = Now();
            }
            result.Timestamp = DateTime.UtcNow;
            lock (_lock)
            {
                if (!_polls.ContainsKey(poll.Id)) return;
                poll.Stats.Record(result.Status, result.RttMs);
            }
            poll.Callback(result);
        }
    }
}
