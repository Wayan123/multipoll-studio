using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace MultiPollStudio
{
    public static class Traffic
    {
        /// <summary>Format a numbered traffic line with a normalized direction and hexadecimal bytes.</summary>
        public static string Line(int seq, string direction, byte[] data)
        {
            return (seq % 1000000).ToString("D6") + " | " + direction.ToUpperInvariant() + " | " + Hex.Format(data);
        }
    }

    public enum CheckKind { None, Crc, Lrc }

    public static class TestCenter
    {
        public const string Header = "MultiPoll Studio Frame List";

        /// <summary>Append CRC (RTU) or LRC + CR LF (ASCII; LRC over the hex-decoded body after ':').</summary>
        public static byte[] AddCheck(byte[] data, CheckKind kind)
        {
            if (kind == CheckKind.Crc)
            {
                int crc = Framing.Crc16(data);
                return data.Concat(new[] { (byte)crc, (byte)(crc >> 8) }).ToArray();
            }
            if (kind == CheckKind.Lrc)
            {
                if (data.Length == 0 || data[0] != (byte)':') throw new FormatException("ASCII frames must start with 3A (':')");
                byte[] body = Hex.Parse(Encoding.ASCII.GetString(data, 1, data.Length - 1));
                string lrc = Framing.Lrc(body, 0, body.Length).ToString("X2");
                return data.Concat(Encoding.ASCII.GetBytes(lrc + "\r\n")).ToArray();
            }
            return data;
        }

        public static void Save(string path, IEnumerable<string> entries)
        {
            File.WriteAllLines(path, new[] { Header }.Concat(entries));
        }

        public static List<string> Load(string path)
        {
            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0 || lines[0].Trim() != Header) throw new FormatException("first line must be '" + Header + "'");
            return lines.Skip(1).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        }
    }

    public static class Render
    {
        /// <summary>(offset, text) for every value cell. Multi-register values sit on their first offset; the
        /// following offsets get "" as in the original grid. A trailing register without a partner falls back to hex.</summary>
        public static List<KeyValuePair<int, string>> Values(PollDefinition d, int[] values, ViewOptions view)
        {
            var output = new List<KeyValuePair<int, string>>();
            if (values == null) return output;
            if (d.IsBits)
            {
                for (int i = 0; i < values.Length; i++) output.Add(new KeyValuePair<int, string>(i, values[i].ToString()));
                return output;
            }
            int k = 0;
            while (k < values.Length)
            {
                DisplayFormat fmt = view.FormatAt(k);
                int w = fmt.Width;
                if (w > 1 && k + w <= values.Length)
                {
                    output.Add(new KeyValuePair<int, string>(k, Formats.Format(values, k, fmt)));
                    for (int j = 1; j < w; j++) output.Add(new KeyValuePair<int, string>(k + j, ""));
                    k += w;
                    continue;
                }
                if (w > 1) fmt = DisplayFormat.Hex;
                output.Add(new KeyValuePair<int, string>(k, Formats.Format(values, k, fmt)));
                k++;
            }
            return output;
        }
    }

    public enum Delimiter { Fixed, Comma, Tab }

    public enum LogTime { LocalWithOffset, Utc }

    public enum LogRotation { None, Daily, Size }

    public sealed class LogSettings
    {
        public string Path = "logfile.csv";
        public bool EachPoll = true;
        public int RateSeconds = 1;
        public Delimiter Delimiter = Delimiter.Comma;
        public bool LogErrors = true;
        /// <summary>Only log when a value changed by more than <see cref="Deadband"/> (raw units) or the status changed.</summary>
        public bool ChangedOnly;
        public double Deadband;
        /// <summary>With ChangedOnly, still log at least every N seconds (0 = never forced).</summary>
        public int HeartbeatSeconds;
        public LogTime Time = LogTime.LocalWithOffset;
        public LogRotation Rotation = LogRotation.None;
        public int RotateSizeMb = 10;
        /// <summary>Keep this many rotated files (0 = keep all).</summary>
        public int KeepFiles;
        /// <summary>Write raw register values instead of scaled engineering values.</summary>
        public bool RawValues;
    }

    /// <summary>One line per logged poll with ISO-8601 timestamps (milliseconds, explicit UTC "Z" or numeric offset),
    /// a header row per file, a status and RTT column, and optional daily or size rotation. Lines of failed polls keep the
    /// row with empty values and the status text, so gaps in the data are visible. Call from one thread.</summary>
    public sealed class DataLogger : IDisposable
    {
        readonly LogSettings _s;
        readonly ViewOptions _view;
        readonly Func<int, Tag> _tagAt;
        StreamWriter _w;
        string _currentPath;
        DateTime _currentDay;
        DateTime _lastLogged = DateTime.MinValue;
        int[] _lastValues;
        Status _lastStatus = Status.NotInitialized;
        PollDefinition _headerFor;
        public int LinesWritten;
        public string CurrentPath { get { return _currentPath; } }

        public DataLogger(LogSettings settings, ViewOptions view, Func<int, Tag> tagAt = null)
        {
            if (settings.Rotation == LogRotation.Size && settings.RotateSizeMb < 1) throw new ArgumentException("rotation size must be ≥ 1 MB");
            _s = settings;
            _view = view;
            _tagAt = tagAt ?? (o => null);
            Open(DateTime.UtcNow);
        }

        public void Dispose()
        {
            if (_w != null) _w.Dispose();
            _w = null;
        }

        DateTime Local(DateTime utc) { return _s.Time == LogTime.Utc ? utc : utc.ToLocalTime(); }

        public static string Stamp(DateTime utc, LogTime time)
        {
            if (time == LogTime.Utc) return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            DateTimeOffset local = new DateTimeOffset(utc, TimeSpan.Zero).ToLocalTime();
            return local.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
        }

        /// <summary>For daily rotation the date goes into the name (log.csv → log_2026-10-29.csv).</summary>
        string PathFor(DateTime utc)
        {
            if (_s.Rotation != LogRotation.Daily) return _s.Path;
            string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_s.Path));
            string name = System.IO.Path.GetFileNameWithoutExtension(_s.Path);
            string ext = System.IO.Path.GetExtension(_s.Path);
            return System.IO.Path.Combine(dir, name + "_" + Local(utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ext);
        }

        void Open(DateTime utc)
        {
            Dispose();
            _currentPath = PathFor(utc);
            _currentDay = Local(utc).Date;
            bool fresh = !File.Exists(_currentPath) || new FileInfo(_currentPath).Length == 0;
            _w = new StreamWriter(_currentPath, true, new UTF8Encoding(false)) { AutoFlush = true };
            if (fresh) _headerFor = null; // a new file always starts with a header row
        }

        void RotateIfNeeded(DateTime utc)
        {
            if (_s.Rotation == LogRotation.Daily && Local(utc).Date != _currentDay)
            {
                Open(utc);
                Prune();
            }
            else if (_s.Rotation == LogRotation.Size && _w.BaseStream.Length >= (long)_s.RotateSizeMb * 1024 * 1024)
            {
                Dispose();
                string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_s.Path));
                string name = System.IO.Path.GetFileNameWithoutExtension(_s.Path);
                string ext = System.IO.Path.GetExtension(_s.Path);
                string archived = System.IO.Path.Combine(dir, name + "_" + utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + ext);
                File.Move(_s.Path, archived);
                _headerFor = null;
                Open(utc);
                Prune();
            }
        }

        /// <summary>Delete the oldest rotated files beyond <see cref="LogSettings.KeepFiles"/>. Only files whose name is the
        /// log name plus "_" and a date/time suffix are considered.</summary>
        void Prune()
        {
            if (_s.KeepFiles <= 0) return;
            string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_s.Path));
            string name = System.IO.Path.GetFileNameWithoutExtension(_s.Path);
            string ext = System.IO.Path.GetExtension(_s.Path);
            var rotated = Directory.GetFiles(dir, name + "_*" + ext)
                .Where(f => System.Text.RegularExpressions.Regex.IsMatch(System.IO.Path.GetFileNameWithoutExtension(f),
                    "^" + System.Text.RegularExpressions.Regex.Escape(name) + @"_\d{4}-?\d{2}-?\d{2}(T\d{6}Z)?$"))
                .Where(f => !string.Equals(System.IO.Path.GetFullPath(f), System.IO.Path.GetFullPath(_currentPath), StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.Ordinal).ToList();
            int keep = _s.Rotation == LogRotation.Daily ? _s.KeepFiles - 1 : _s.KeepFiles; // daily: current file counts
            for (int i = 0; i < rotated.Count - Math.Max(0, keep); i++)
                try { File.Delete(rotated[i]); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        bool Changed(PollResult r)
        {
            if (r.Status != _lastStatus) return true;
            if (r.Status != Status.Ok || _lastValues == null || _lastValues.Length != r.Values.Length) return true;
            for (int i = 0; i < r.Values.Length; i++)
                if (Math.Abs(r.Values[i] - _lastValues[i]) > _s.Deadband) return true;
            return false;
        }

        List<string> Cells(PollResult r)
        {
            var d = r.Definition;
            if (r.Status != Status.Ok)
            {
                int count = Render.Values(d, new int[d.Quantity], _view).Count(kv => kv.Value.Length > 0 || d.IsBits);
                return Enumerable.Repeat("", count).ToList();
            }
            var output = new List<string>();
            foreach (var kv in Render.Values(d, r.Values, _view))
            {
                if (kv.Value.Length == 0 && !d.IsBits) continue;
                Tag t = _tagAt(kv.Key);
                DisplayFormat fmt = _view.FormatAt(kv.Key);
                double? number = !_s.RawValues && t != null && t.IsScaled && !d.IsBits && fmt.Scalable && kv.Key + fmt.Width <= r.Values.Length
                    ? Formats.Number(r.Values, kv.Key, fmt) : null;
                if (number.HasValue)
                {
                    double eng = t.ToEngineering(number.Value);
                    output.Add(t.Decimals >= 0 ? eng.ToString("F" + t.Decimals, CultureInfo.InvariantCulture) : eng.ToString("G7", CultureInfo.InvariantCulture));
                }
                else output.Add(kv.Value);
            }
            return output;
        }

        List<string> HeaderCells(PollDefinition d)
        {
            var names = new List<string> { "timestamp", "status", "rtt_ms" };
            foreach (var kv in Render.Values(d, new int[d.Quantity], _view))
            {
                if (kv.Value.Length == 0 && !d.IsBits) continue;
                Tag t = _tagAt(kv.Key);
                string addr = Addressing.Label(d.Function, d.Address + kv.Key, _view.PlcAddresses);
                string name = t != null && t.Name.Length > 0 ? t.Name : addr;
                if (!_s.RawValues && t != null && t.Unit.Length > 0) name += " [" + t.Unit + "]";
                names.Add(name);
            }
            return names;
        }

        string Join(IEnumerable<string> cells)
        {
            if (_s.Delimiter == Delimiter.Fixed) return string.Join(" ", cells.Select(c => c.PadLeft(9)));
            string sep = _s.Delimiter == Delimiter.Comma ? "," : "\t";
            return string.Join(sep, cells.Select(c => Csv(c, sep)));
        }

        public bool Log(PollResult r)
        {
            if (r.Status != Status.Ok && !_s.LogErrors) return false;
            bool heartbeatDue = _s.HeartbeatSeconds > 0 && (r.Timestamp - _lastLogged).TotalSeconds >= _s.HeartbeatSeconds;
            if (!_s.EachPoll && (r.Timestamp - _lastLogged).TotalSeconds < _s.RateSeconds) return false;
            if (_s.ChangedOnly && !Changed(r) && !heartbeatDue) return false;
            RotateIfNeeded(r.Timestamp);
            bool layoutChanged = _headerFor != null && (_headerFor.Function != r.Definition.Function
                || _headerFor.Address != r.Definition.Address || _headerFor.Quantity != r.Definition.Quantity);
            if (_headerFor == null || layoutChanged)
            {
                _w.WriteLine(Join(HeaderCells(r.Definition)));
                _headerFor = r.Definition;
            }
            var row = new List<string>
            {
                Stamp(r.Timestamp, _s.Time),
                r.Status == Status.Ok ? "OK" : r.Status.Text(),
                r.RttMs >= 0 ? r.RttMs.ToString("0.0", CultureInfo.InvariantCulture) : "",
            };
            row.AddRange(Cells(r));
            _w.WriteLine(Join(row));
            _lastLogged = r.Timestamp;
            _lastStatus = r.Status;
            if (r.Status == Status.Ok) _lastValues = (int[])r.Values.Clone();
            LinesWritten++;
            return true;
        }

        public void LogEvent(DateTime utc, string text)
        {
            RotateIfNeeded(utc);
            _w.WriteLine(Join(new[] { Stamp(utc, _s.Time), "EVENT", "", text }));
        }

        /// <summary>Numbers (including negatives and hex like 0x00FF) are written as-is; only text cells get the
        /// spreadsheet formula guard from <see cref="TagMapCsv.Quote"/>.</summary>
        static string Csv(string c, string sep)
        {
            double ignored;
            bool number = double.TryParse(c, NumberStyles.Float, CultureInfo.InvariantCulture, out ignored);
            if (!number) c = TagMapCsv.Quote(c);
            return sep != "," && c.Contains(sep) ? "\"" + c.Replace("\"", "\"\"") + "\"" : c;
        }
    }

    /// <summary>Append-only audit trail of every write attempt (JSON Lines). Each record carries the SHA-256 of the
    /// previous line, so deleting or editing a line breaks the chain and <see cref="Verify"/> reports where. This detects
    /// casual edits; it is not protection against a determined attacker with file access.</summary>
    public sealed class AuditLog
    {
        readonly string _path;
        readonly object _lock = new object();
        string _lastHash;

        public string Path { get { return _path; } }

        public AuditLog(string path)
        {
            _path = path;
            _lastHash = LastHash(path);
        }

        public static string DefaultPath()
        {
            string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MultiPollStudio");
            Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "audit.jsonl");
        }

        static string Sha256(string s)
        {
            using (var h = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "").ToLowerInvariant();
        }

        static string LastHash(string path)
        {
            if (!File.Exists(path)) return new string('0', 64);
            string last = File.ReadLines(path).LastOrDefault(l => l.Trim().Length > 0);
            return last == null ? new string('0', 64) : Sha256(last);
        }

        /// <summary>Record one write attempt. <paramref name="result"/> is "OK", a status text, or "BLOCKED: reason".</summary>
        public void Record(string target, int slave, int function, int address, IList<int> values, IList<int> before, string result)
        {
            lock (_lock)
            {
                var rec = new Dictionary<string, object>
                {
                    { "ts", DataLogger.Stamp(DateTime.UtcNow, LogTime.Utc) },
                    { "user", Environment.UserDomainName + "\\" + Environment.UserName },
                    { "host", Environment.MachineName },
                    { "target", target },
                    { "slave", slave },
                    { "fc", function },
                    { "address", address },
                    { "values", values == null ? new int[0] : values.ToArray() },
                    { "before", before == null ? null : before.ToArray() },
                    { "result", result },
                    { "prev", _lastHash },
                };
                string line = Persistence.Serialize(rec);
                File.AppendAllText(_path, line + "\n", new UTF8Encoding(false));
                _lastHash = Sha256(line);
            }
        }

        /// <summary>Returns null when the chain is intact, otherwise a description of the first broken line.</summary>
        public static string Verify(string path)
        {
            if (!File.Exists(path)) return null;
            string prev = new string('0', 64);
            int n = 0;
            foreach (string line in File.ReadLines(path))
            {
                n++;
                if (line.Trim().Length == 0) continue;
                Dictionary<string, object> rec;
                try { rec = Persistence.Parse(line); }
                catch (ArgumentException) { return "line " + n + ": not valid JSON"; }
                catch (InvalidOperationException) { return "line " + n + ": not valid JSON"; }
                object p;
                if (!rec.TryGetValue("prev", out p) || (string)p != prev) return "line " + n + ": chain broken (previous line edited or removed)";
                prev = Sha256(line);
            }
            return null;
        }
    }

    /// <summary>Decode a Modbus write request PDU into (address, values) for confirmation and audit.</summary>
    public static class WriteInfo
    {
        public static bool IsWrite(byte[] pdu)
        {
            int fc = pdu[0];
            return fc == 5 || fc == 6 || fc == 15 || fc == 16 || fc == 22 || fc == 23;
        }

        /// <summary>For FC23 the address/values are the write part.</summary>
        public static void Decode(byte[] pdu, out int address, out int[] values, out string what)
        {
            int fc = pdu[0];
            switch (fc)
            {
                case 5:
                    address = Pdu.Get16(pdu, 1);
                    values = new[] { Pdu.Get16(pdu, 3) == 0xFF00 ? 1 : 0 };
                    what = "coil";
                    return;
                case 6:
                    address = Pdu.Get16(pdu, 1);
                    values = new[] { Pdu.Get16(pdu, 3) };
                    what = "register";
                    return;
                case 15:
                    address = Pdu.Get16(pdu, 1);
                    values = Pdu.UnpackBits(pdu, 6, Pdu.Get16(pdu, 3));
                    what = "coils";
                    return;
                case 16:
                {
                    address = Pdu.Get16(pdu, 1);
                    int n = Pdu.Get16(pdu, 3);
                    values = new int[n];
                    for (int i = 0; i < n; i++) values[i] = Pdu.Get16(pdu, 6 + 2 * i);
                    what = "registers";
                    return;
                }
                case 22:
                    address = Pdu.Get16(pdu, 1);
                    values = new[] { Pdu.Get16(pdu, 3), Pdu.Get16(pdu, 5) };
                    what = "AND/OR mask";
                    return;
                case 23:
                {
                    address = Pdu.Get16(pdu, 5);
                    int n = Pdu.Get16(pdu, 7);
                    values = new int[n];
                    for (int i = 0; i < n; i++) values[i] = Pdu.Get16(pdu, 10 + 2 * i);
                    what = "registers (FC23 write part)";
                    return;
                }
                default:
                    throw new ArgumentException("not a write function");
            }
        }
    }

    /// <summary>JSON documents (.mpoll) and workspaces (.mpollw), compatible with the Python MultiPoll Studio.</summary>
    public static class Persistence
    {
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public static Dictionary<string, object> ToDict(PollDocument doc)
        {
            var d = doc.Definition;
            var v = doc.View;
            var r = new Dictionary<string, object>
            {
                { "format", "multipoll-document" },
                { "version", 1 },
                { "definition", new Dictionary<string, object>
                    {
                        { "slave_id", d.SlaveId }, { "function", d.Function }, { "address", d.Address },
                        { "quantity", d.Quantity }, { "scan_rate_ms", d.ScanRateMs }, { "enabled", d.Enabled },
                    } },
                { "view", new Dictionary<string, object>
                    {
                        { "display", v.Display.Key }, { "plc_addresses", v.PlcAddresses }, { "rows", v.Rows },
                        { "hide_alias", v.HideAlias }, { "address_in_cell", v.AddressInCell },
                        { "cell_formats", v.CellFormats.ToDictionary(kv => kv.Key.ToString(CultureInfo.InvariantCulture), kv => (object)kv.Value.Key) },
                    } },
                { "aliases", doc.Aliases.Where(kv => !string.IsNullOrEmpty(kv.Value))
                                        .ToDictionary(kv => kv.Key.ToString(CultureInfo.InvariantCulture), kv => (object)kv.Value) },
                { "tags", doc.Tags.ToDictionary(kv => kv.Key.ToString(CultureInfo.InvariantCulture), kv => (object)TagToDict(kv.Value)) },
            };
            // optional keys, written only when used so files stay readable by older versions and the Python edition
            if (doc.Connection != null) r["connection"] = ToDict(doc.Connection);
            if (doc.ProfileId.Length > 0) r["profile"] = doc.ProfileId;
            if (doc.Title.Length > 0) r["title"] = doc.Title;
            return r;
        }

        static Dictionary<string, object> TagToDict(Tag t)
        {
            var d = new Dictionary<string, object>
            {
                { "name", t.Name }, { "scale", t.Scale }, { "offset", t.Offset }, { "unit", t.Unit }, { "decimals", t.Decimals },
                { "deadband", t.Deadband },
            };
            if (t.HighHigh.HasValue) d["hh"] = t.HighHigh.Value;
            if (t.High.HasValue) d["h"] = t.High.Value;
            if (t.Low.HasValue) d["l"] = t.Low.Value;
            if (t.LowLow.HasValue) d["ll"] = t.LowLow.Value;
            if (t.WriteMin.HasValue) d["write_min"] = t.WriteMin.Value;
            if (t.WriteMax.HasValue) d["write_max"] = t.WriteMax.Value;
            return d;
        }

        static double? GetOpt(Dictionary<string, object> d, string key)
        {
            object o;
            return d.TryGetValue(key, out o) && o != null ? Convert.ToDouble(o, CultureInfo.InvariantCulture) : (double?)null;
        }

        static Tag TagFromDict(Dictionary<string, object> d)
        {
            var t = new Tag
            {
                Name = Get(d, "name", ""), Scale = Get(d, "scale", 1.0), Offset = Get(d, "offset", 0.0), Unit = Get(d, "unit", ""),
                Decimals = Get(d, "decimals", -1), Deadband = Get(d, "deadband", 0.0),
                HighHigh = GetOpt(d, "hh"), High = GetOpt(d, "h"), Low = GetOpt(d, "l"), LowLow = GetOpt(d, "ll"),
                WriteMin = GetOpt(d, "write_min"), WriteMax = GetOpt(d, "write_max"),
            };
            t.Validate();
            return t;
        }

        static T Get<T>(Dictionary<string, object> d, string key, T fallback)
        {
            object o;
            if (d == null || !d.TryGetValue(key, out o) || o == null) return fallback;
            return (T)Convert.ChangeType(o, typeof(T), CultureInfo.InvariantCulture);
        }

        static Dictionary<string, object> Sub(Dictionary<string, object> d, string key)
        {
            object o;
            return d != null && d.TryGetValue(key, out o) ? o as Dictionary<string, object> : null;
        }

        public static PollDocument FromDict(Dictionary<string, object> data)
        {
            if (Get(data, "format", "") != "multipoll-document") throw new FormatException("not a MultiPoll Studio document");
            var doc = new PollDocument();
            var dd = Sub(data, "definition");
            var d = doc.Definition;
            d.SlaveId = Get(dd, "slave_id", d.SlaveId);
            d.Function = Get(dd, "function", d.Function);
            d.Address = Get(dd, "address", d.Address);
            d.Quantity = Get(dd, "quantity", d.Quantity);
            d.ScanRateMs = Get(dd, "scan_rate_ms", d.ScanRateMs);
            d.Enabled = Get(dd, "enabled", d.Enabled);
            d.Validate();
            var vd = Sub(data, "view");
            var v = doc.View;
            v.Display = DisplayFormat.FromKey(Get(vd, "display", "signed"));
            v.PlcAddresses = Get(vd, "plc_addresses", false);
            v.Rows = Get(vd, "rows", 10);
            if (Array.IndexOf(ViewOptions.RowChoices, v.Rows) < 0) v.Rows = 10;
            v.HideAlias = Get(vd, "hide_alias", false);
            v.AddressInCell = Get(vd, "address_in_cell", false);
            var cf = Sub(vd, "cell_formats");
            if (cf != null)
                foreach (var kv in cf) v.CellFormats[int.Parse(kv.Key, CultureInfo.InvariantCulture)] = DisplayFormat.FromKey(Convert.ToString(kv.Value));
            var al = Sub(data, "aliases");
            if (al != null)
                foreach (var kv in al) doc.Aliases[int.Parse(kv.Key, CultureInfo.InvariantCulture)] = Convert.ToString(kv.Value);
            var tg = Sub(data, "tags");
            if (tg != null)
                foreach (var kv in tg)
                {
                    int off = int.Parse(kv.Key, CultureInfo.InvariantCulture);
                    var td = kv.Value as Dictionary<string, object>;
                    if (off < 0 || off >= d.Quantity || td == null) throw new FormatException("tag " + kv.Key + " is invalid");
                    doc.Tags[off] = TagFromDict(td);
                }
            var conn = Sub(data, "connection");
            if (conn != null) doc.Connection = ConnectionFromDict(conn);
            doc.ProfileId = Get(data, "profile", "");
            doc.Title = Get(data, "title", "");
            if (doc.Title.Length > 200) doc.Title = doc.Title.Substring(0, 200);
            return doc;
        }

        public static void SaveDocument(string path, PollDocument doc) { File.WriteAllText(path, Json.Serialize(ToDict(doc))); }

        public static PollDocument LoadDocument(string path)
        {
            return FromDict(Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path)));
        }

        public static string Serialize(object o) { return Json.Serialize(o); }

        public static Dictionary<string, object> Parse(string text) { return Json.Deserialize<Dictionary<string, object>>(text); }

        // -- connection settings (same keys as the Python version) --

        static readonly string[] ParityKeys = { "N", "O", "E", "M", "S" };

        static readonly string[] KindKeys = { "serial", "tcp", "udp", "serial_over_tcp", "serial_over_udp" };

        public static Dictionary<string, object> ToDict(ConnectionSettings s)
        {
            return new Dictionary<string, object>
            {
                { "kind", KindKeys[(int)s.Kind] }, { "serial_mode", s.Mode == SerialMode.Rtu ? "rtu" : "ascii" },
                { "port", s.Port }, { "baudrate", s.BaudRate }, { "databits", s.DataBits },
                { "parity", ParityKeys[(int)s.Parity] }, { "stopbits", s.StopBits == System.IO.Ports.StopBits.Two ? 2 : 1 },
                { "host", s.Host }, { "tcp_port", s.TcpPort }, { "connect_timeout_ms", s.ConnectTimeoutMs },
                { "response_timeout_ms", s.ResponseTimeoutMs }, { "delay_between_polls_ms", s.DelayBetweenPollsMs },
                { "rts_toggle", s.RtsToggle }, { "rts_disable_delay_ms", s.RtsDisableDelayMs },
                { "dsr_flow", s.DsrFlow }, { "cts_flow", s.CtsFlow }, { "remove_echo", s.RemoveEcho },
            };
        }

        public static ConnectionSettings ConnectionFromDict(Dictionary<string, object> d)
        {
            var s = new ConnectionSettings();
            int kind = Array.IndexOf(KindKeys, Get(d, "kind", "tcp"));
            s.Kind = kind < 0 ? ConnectionKind.Tcp : (ConnectionKind)kind;
            s.Mode = Get(d, "serial_mode", "rtu") == "ascii" ? SerialMode.Ascii : SerialMode.Rtu;
            s.Port = Get(d, "port", s.Port);
            s.BaudRate = Get(d, "baudrate", s.BaudRate);
            s.DataBits = Get(d, "databits", s.DataBits);
            int parity = Array.IndexOf(ParityKeys, Get(d, "parity", "N"));
            s.Parity = parity < 0 ? System.IO.Ports.Parity.None : (System.IO.Ports.Parity)parity;
            s.StopBits = Get(d, "stopbits", 1) == 2 ? System.IO.Ports.StopBits.Two : System.IO.Ports.StopBits.One;
            s.Host = Get(d, "host", s.Host);
            s.TcpPort = Get(d, "tcp_port", s.TcpPort);
            s.ConnectTimeoutMs = Get(d, "connect_timeout_ms", s.ConnectTimeoutMs);
            s.ResponseTimeoutMs = Get(d, "response_timeout_ms", s.ResponseTimeoutMs);
            s.DelayBetweenPollsMs = Get(d, "delay_between_polls_ms", s.DelayBetweenPollsMs);
            s.RtsToggle = Get(d, "rts_toggle", false);
            s.RtsDisableDelayMs = Get(d, "rts_disable_delay_ms", 0);
            s.DsrFlow = Get(d, "dsr_flow", false);
            s.CtsFlow = Get(d, "cts_flow", false);
            s.RemoveEcho = Get(d, "remove_echo", false);
            return s;
        }
    }
}
