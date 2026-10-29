using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace MultiPollStudio
{
    public sealed class ScanHit
    {
        public int SlaveId;
        /// <summary>"data" when the device answered with values, otherwise the exception text. Both prove a device is there.</summary>
        public string Reply;
        public double RttMs;
    }

    /// <summary>Read-only discovery. Modbus has no discovery protocol, so this is brute-force polling: one small read per
    /// unit ID. Any reply, data or exception, means a device is present. Silence does not prove absence: CRC or parity
    /// errors and a wrong baud rate also look like silence. Never writes.</summary>
    public static class Discovery
    {
        /// <summary>Scan unit IDs <paramref name="from"/>..<paramref name="to"/> with one read of 1 item.
        /// Runs on the calling thread through <paramref name="master"/>; use from inside an engine job.
        /// <paramref name="progress"/> gets each probed ID; set <paramref name="cancel"/> to stop between probes.</summary>
        public static List<ScanHit> ScanUnits(ModbusMaster master, int from, int to, int function, int address,
                                             int delayMs, Action<int> progress, CancellationToken cancel)
        {
            if (from < 1 || to > 247 || from > to) throw new ArgumentException("unit ID range must be within 1..247");
            if (!Pdu.IsRead(function)) throw new ArgumentException("scan uses read functions 01..04 only");
            var hits = new List<ScanHit>();
            byte[] request = Pdu.ReadRequest(function, address, 1);
            for (int id = from; id <= to; id++)
            {
                if (cancel.IsCancellationRequested) break;
                if (progress != null) progress(id);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    master.Transact(id, request);
                    hits.Add(new ScanHit { SlaveId = id, Reply = "data", RttMs = sw.Elapsed.TotalMilliseconds });
                }
                catch (ModbusException ex)
                {
                    if (ex.Status >= Status.UnknownException)
                        hits.Add(new ScanHit { SlaveId = id, Reply = ex.Status.Text(), RttMs = sw.Elapsed.TotalMilliseconds });
                    else if (ex.Status == Status.NoConnection) throw;
                    // timeout, CRC, response from another slave: treat as not found
                }
                if (delayMs > 0) Thread.Sleep(delayMs);
            }
            return hits;
        }

        static readonly string[] ObjectNames =
        {
            "VendorName", "ProductCode", "MajorMinorRevision", "VendorUrl", "ProductName", "ModelName", "UserApplicationName",
        };

        public static string ObjectName(int id)
        {
            return id < ObjectNames.Length ? ObjectNames[id] : "Object 0x" + id.ToString("X2");
        }

        /// <summary>FC43 / MEI 14 Read Device Identification, basic stream (objects 0x00..0x02) or regular (0x00..0x06),
        /// following the "more follows" continuation until all objects are read.</summary>
        public static List<KeyValuePair<int, string>> ReadDeviceId(ModbusMaster master, int slave, bool regular)
        {
            var objects = new List<KeyValuePair<int, string>>();
            int next = 0;
            for (int round = 0; round < 32; round++)
            {
                byte[] request = { 0x2B, 0x0E, (byte)(regular ? 2 : 1), (byte)next };
                byte[] response = master.Execute(slave, request);
                if (response == null) throw new ModbusException(Status.ResponseError, "no response");
                if (response[0] == 0xAB)
                {
                    var st = response.Length >= 2 ? (Status)(0x80 | response[1]) : Status.UnknownException;
                    if (!Enum.IsDefined(typeof(Status), st)) st = Status.UnknownException;
                    throw new ModbusException(st, "FC43/14 exception code " + (response.Length >= 2 ? response[1].ToString("X2") : "?"));
                }
                if (response.Length < 7 || response[0] != 0x2B || response[1] != 0x0E)
                    throw new ModbusException(Status.ResponseError, "not a Read Device Identification response");
                bool more = response[4] == 0xFF;
                int nextId = response[5];
                int count = response[6];
                int pos = 7;
                for (int i = 0; i < count; i++)
                {
                    if (pos + 2 > response.Length) throw new ModbusException(Status.ByteCountError, "object list truncated");
                    int id = response[pos], len = response[pos + 1];
                    if (pos + 2 + len > response.Length) throw new ModbusException(Status.ByteCountError, "object " + id + " truncated");
                    objects.Add(new KeyValuePair<int, string>(id, Printable(Encoding.ASCII.GetString(response, pos + 2, len))));
                    pos += 2 + len;
                }
                if (!more) break;
                if (nextId <= next && round > 0) throw new ModbusException(Status.ResponseError, "device repeats object ids");
                next = nextId;
            }
            return objects;
        }

        static string Printable(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) sb.Append(c >= 0x20 && c < 0x7F ? c : '?');
            return sb.ToString();
        }
    }
}
