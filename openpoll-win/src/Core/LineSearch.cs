using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Threading;

namespace MultiPollStudio
{
    public sealed class LineHit
    {
        public int Baud;
        public Parity Parity;
        public StopBits StopBits;
        public int SlaveId;
        /// <summary>"data" or the exception text; both prove a device is there.</summary>
        public string Reply = "";
        public double RttMs;
        /// <summary>Profile names whose identify register matched, or "" when not checked.</summary>
        public string Identified = "";

        public string Line
        {
            get
            {
                return Baud + " " + LineSearch.ParityLetter(Parity) + (StopBits == StopBits.Two ? "2" : "1") + "  ID " + SlaveId
                       + "  " + Reply + "  " + RttMs.ToString("0") + " ms" + (Identified.Length > 0 ? "  → " + Identified : "");
            }
        }
    }

    public sealed class LineSearchOptions
    {
        public static readonly int[] CommonBauds = { 9600, 4800, 19200, 2400, 38400, 115200, 57600, 1200 };

        public int[] Bauds = CommonBauds;
        public Parity[] Parities = { Parity.None, Parity.Even, Parity.Odd };
        /// <summary>Also try 8N2 when parity is None (some devices send 2 stop bits).</summary>
        public bool TryTwoStopBits;
        public int FromId = 1, ToId = 247;
        /// <summary>IDs tried first at every line setting (factory defaults); the rest of the range follows.</summary>
        public int[] QuickIds = { 1, 2, 3, 247, 254 };
        public int Function = 3, Address;
        /// <summary>Per-probe timeout; 0 = derived from the baud rate.</summary>
        public int TimeoutMs;
        /// <summary>Stop after the first device is found.</summary>
        public bool StopAtFirst = true;
        /// <summary>When false only <see cref="QuickIds"/> are tried at each setting (fast).</summary>
        public bool FullRange = true;
    }

    /// <summary>Read-only search for a serial device whose baud rate, parity and slave ID are unknown. Tries the common
    /// defaults first: for each line setting it probes the quick IDs, then (optionally) the full range. Any reply,
    /// including an exception, proves the line setting and the ID. Only FC01–04 single reads are sent.</summary>
    public static class LineSearch
    {
        public static string ParityLetter(Parity p) { return p == Parity.Even ? "E" : p == Parity.Odd ? "O" : "N"; }

        /// <summary>Probe timeout: wire time of a 1-register read plus 120 ms device turnaround, at least 150 ms.</summary>
        public static int ProbeTimeoutMs(int baud)
        {
            return Math.Max(150, (int)Math.Ceiling(SerialTiming.TransactionMs(baud, 3, 1, SerialMode.Rtu) + 120));
        }

        /// <summary>Line settings in search order (baud × parity × stop bits).</summary>
        public static List<Tuple<int, Parity, StopBits>> Settings(LineSearchOptions o)
        {
            var list = new List<Tuple<int, Parity, StopBits>>();
            foreach (int b in o.Bauds)
                foreach (Parity p in o.Parities)
                {
                    list.Add(Tuple.Create(b, p, StopBits.One));
                    if (o.TryTwoStopBits && p == Parity.None) list.Add(Tuple.Create(b, p, StopBits.Two));
                }
            return list;
        }

        /// <summary>IDs in probe order for one line setting.</summary>
        public static List<int> Ids(LineSearchOptions o)
        {
            var ids = o.QuickIds.Where(i => i >= o.FromId && i <= o.ToId).ToList();
            if (o.FullRange)
                for (int i = o.FromId; i <= o.ToId; i++)
                    if (!ids.Contains(i)) ids.Add(i);
            return ids;
        }

        /// <summary>Worst-case duration when nothing answers.</summary>
        public static TimeSpan Estimate(LineSearchOptions o)
        {
            double ms = 0;
            int ids = Ids(o).Count;
            foreach (var s in Settings(o)) ms += ids * ((o.TimeoutMs > 0 ? o.TimeoutMs : ProbeTimeoutMs(s.Item1)) + 5);
            return TimeSpan.FromMilliseconds(ms);
        }

        /// <param name="baseSettings">Port and adapter options (RTS toggle, echo); baud/parity/stop are replaced. Serial
        /// normally; RTU over TCP is accepted so the search can be exercised against a simulator (the line settings are
        /// then only labels).</param>
        /// <param name="progress">Called with a status line before each line setting and every 10 IDs.</param>
        /// <param name="found">Called for each hit as it is found.</param>
        public static List<LineHit> Run(ConnectionSettings baseSettings, LineSearchOptions o, Action<string> progress,
                                        Action<LineHit> found, CancellationToken cancel, Action<string, byte[]> traffic = null)
        {
            if (baseSettings.Kind != ConnectionKind.Serial && baseSettings.Kind != ConnectionKind.SerialOverTcp)
                throw new ArgumentException("the line search needs a serial port");
            if (!Pdu.IsRead(o.Function)) throw new ArgumentException("the line search uses read functions 01..04 only");
            if (o.FromId < 1 || o.ToId > 255 || o.FromId > o.ToId) throw new ArgumentException("ID range must be within 1..255");
            var hits = new List<LineHit>();
            byte[] request = Pdu.ReadRequest(o.Function, o.Address, 1);
            List<int> ids = Ids(o);
            foreach (var line in Settings(o))
            {
                if (cancel.IsCancellationRequested) break;
                ConnectionSettings s = baseSettings.Clone();
                s.BaudRate = line.Item1;
                s.Parity = line.Item2;
                s.StopBits = line.Item3;
                s.DataBits = 8;
                s.ResponseTimeoutMs = o.TimeoutMs > 0 ? o.TimeoutMs : ProbeTimeoutMs(s.BaudRate);
                string label = s.BaudRate + " " + ParityLetter(s.Parity) + (s.StopBits == StopBits.Two ? "2" : "1");
                if (progress != null) progress("Trying " + label + " …");
                using (var m = new ModbusMaster(s) { OnTraffic = traffic })
                {
                    m.Connect(); // throws ModbusException(NoConnection) when the port is missing or busy: no point going on
                    int n = 0;
                    foreach (int id in ids)
                    {
                        if (cancel.IsCancellationRequested) break;
                        if (progress != null && ++n % 10 == 0) progress("Trying " + label + "  ID " + id + " (" + n + "/" + ids.Count + ")");
                        var sw = Stopwatch.StartNew();
                        string reply = null;
                        try
                        {
                            m.Transact(id, request);
                            reply = "data";
                        }
                        catch (ModbusException ex)
                        {
                            if ((int)ex.Status >= 0x80) reply = ex.Status.Text();
                            else if (ex.Status == Status.NoConnection) throw;
                        }
                        if (reply == null) continue;
                        var hit = new LineHit { Baud = s.BaudRate, Parity = s.Parity, StopBits = s.StopBits, SlaveId = id, Reply = reply, RttMs = sw.Elapsed.TotalMilliseconds };
                        hits.Add(hit);
                        if (found != null) found(hit);
                        if (o.StopAtFirst) return hits;
                        Thread.Sleep(20); // line turnaround before the next ID
                    }
                }
                if (hits.Count > 0 && o.StopAtFirst) break;
            }
            return hits;
        }
    }
}
