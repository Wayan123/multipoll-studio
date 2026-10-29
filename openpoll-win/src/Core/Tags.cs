using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MultiPollStudio
{
    /// <summary>Engineering metadata for one address of a poll window: name, linear scaling, unit, decimals and
    /// optional HH/H/L/LL alarm limits. Raw values stay untouched; scaling is applied for display, logging and alarms.
    /// Fields follow the per-point definitions common to Telegraf, Neuron and ThingsBoard (name, scale, offset, unit).</summary>
    public sealed class Tag
    {
        public string Name = "";
        public double Scale = 1;
        public double Offset;
        public string Unit = "";
        /// <summary>Decimals shown for scaled values; -1 = automatic.</summary>
        public int Decimals = -1;
        public double? HighHigh, High, Low, LowLow;
        /// <summary>Alarm deadband in engineering units: an active limit clears only after the value is back inside
        /// the limit by more than this amount (OPC UA LimitAlarmType semantics).</summary>
        public double Deadband;
        /// <summary>Block writes outside [WriteMin, WriteMax] in engineering units.</summary>
        public double? WriteMin, WriteMax;

        public bool IsScaled { get { return Scale != 1 || Offset != 0; } }
        public bool HasAlarms { get { return HighHigh.HasValue || High.HasValue || Low.HasValue || LowLow.HasValue; } }

        public double ToEngineering(double raw) { return raw * Scale + Offset; }

        public double ToRaw(double engineering)
        {
            if (Scale == 0) throw new FormatException("Tag scale is 0; the raw value cannot be computed");
            return (engineering - Offset) / Scale;
        }

        public string Format(double engineering)
        {
            string s = Decimals >= 0
                ? engineering.ToString("F" + Decimals, CultureInfo.InvariantCulture)
                : engineering.ToString("G7", CultureInfo.InvariantCulture);
            return Unit.Length > 0 ? s + " " + Unit : s;
        }

        /// <summary>Limits must satisfy HighHigh ≥ High > Low ≥ LowLow where present (OPC UA Part 9 inequality).</summary>
        public void Validate()
        {
            if (double.IsNaN(Scale) || double.IsInfinity(Scale) || Scale == 0) throw new FormatException("Scale must be a non-zero number");
            if (Deadband < 0) throw new FormatException("Deadband must not be negative");
            var ordered = new[] { HighHigh, High, Low, LowLow }.Where(v => v.HasValue).Select(v => v.Value).ToList();
            for (int i = 1; i < ordered.Count; i++)
                if (ordered[i] > ordered[i - 1]) throw new FormatException("Alarm limits must satisfy HH ≥ H ≥ L ≥ LL");
            if (WriteMin.HasValue && WriteMax.HasValue && WriteMin > WriteMax) throw new FormatException("Write min must be ≤ write max");
        }

        public Tag Clone() { return (Tag)MemberwiseClone(); }

        /// <summary>No name, scaling, unit, decimals, alarms or write limits.</summary>
        public bool IsDefault
        {
            get
            {
                return Name.Length == 0 && !IsScaled && Unit.Length == 0 && Decimals < 0 && !HasAlarms && Deadband == 0
                       && !WriteMin.HasValue && !WriteMax.HasValue;
            }
        }
    }

    public enum AlarmLevel { None = 0, Low = 1, High = 2, LowLow = 3, HighHigh = 4 }

    public static class AlarmLevelInfo
    {
        public static string Code(this AlarmLevel a)
        {
            switch (a)
            {
                case AlarmLevel.HighHigh: return "HH";
                case AlarmLevel.High: return "H";
                case AlarmLevel.Low: return "L";
                case AlarmLevel.LowLow: return "LL";
                default: return "";
            }
        }
    }

    public sealed class AlarmEvent
    {
        public DateTime TimestampUtc;
        public int Offset;
        public string TagName;
        public AlarmLevel From, To;
        public double Value;

        public string Describe()
        {
            string name = string.IsNullOrEmpty(TagName) ? "offset " + Offset : TagName;
            return To == AlarmLevel.None
                ? name + " back to normal (" + Value.ToString("G7", CultureInfo.InvariantCulture) + ")"
                : name + " " + To.Code() + " alarm (" + Value.ToString("G7", CultureInfo.InvariantCulture) + ")";
        }
    }

    /// <summary>Evaluates limit alarms with per-limit deadband. Entering a more severe level is immediate; leaving a
    /// level requires the value to return past the limit by the deadband (e.g. H=20, deadband=1 stays active until
    /// the value drops below 19).</summary>
    public static class AlarmEvaluator
    {
        public static AlarmLevel Evaluate(Tag t, double v, AlarmLevel current)
        {
            if (!t.HasAlarms || double.IsNaN(v)) return AlarmLevel.None;
            double db = t.Deadband;
            if (t.HighHigh.HasValue && v >= t.HighHigh.Value) return AlarmLevel.HighHigh;
            if (t.LowLow.HasValue && v <= t.LowLow.Value) return AlarmLevel.LowLow;
            // hold the current level until the value is past its limit by the deadband
            if (current == AlarmLevel.HighHigh && t.HighHigh.HasValue && v > t.HighHigh.Value - db) return AlarmLevel.HighHigh;
            if (current == AlarmLevel.LowLow && t.LowLow.HasValue && v < t.LowLow.Value + db) return AlarmLevel.LowLow;
            if (t.High.HasValue && v >= t.High.Value) return AlarmLevel.High;
            if (t.Low.HasValue && v <= t.Low.Value) return AlarmLevel.Low;
            if ((current == AlarmLevel.High || current == AlarmLevel.HighHigh) && t.High.HasValue && v > t.High.Value - db) return AlarmLevel.High;
            if ((current == AlarmLevel.Low || current == AlarmLevel.LowLow) && t.Low.HasValue && v < t.Low.Value + db) return AlarmLevel.Low;
            return AlarmLevel.None;
        }
    }

    /// <summary>Tag map CSV: one row per offset. Columns: offset,name,scale,offset_eu,unit,decimals,hh,h,l,ll,deadband,
    /// write_min,write_max,format. Empty cells keep defaults; "format" is a display format key ("f32_cdab", "str8",
    /// "bits4.3") and empty means the window's format. Strict parsing; malformed rows are rejected with the line number.</summary>
    public static class TagMapCsv
    {
        public static readonly string[] Header =
        {
            "offset", "name", "scale", "offset_eu", "unit", "decimals", "hh", "h", "l", "ll", "deadband", "write_min", "write_max", "format",
        };

        const int MaxRows = 2000;
        const int MaxField = 200;

        public static void Save(string path, IDictionary<int, Tag> tags, IDictionary<int, DisplayFormat> formats = null)
        {
            formats = formats ?? new Dictionary<int, DisplayFormat>();
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", Header));
            foreach (int off in tags.Keys.Union(formats.Keys).OrderBy(k => k))
            {
                Tag t = tags.ContainsKey(off) ? tags[off] : new Tag();
                DisplayFormat f;
                formats.TryGetValue(off, out f);
                sb.AppendLine(string.Join(",", new[]
                {
                    off.ToString(CultureInfo.InvariantCulture), Quote(t.Name), Num(t.Scale), Num(t.Offset), Quote(t.Unit),
                    t.Decimals >= 0 ? t.Decimals.ToString(CultureInfo.InvariantCulture) : "",
                    Opt(t.HighHigh), Opt(t.High), Opt(t.Low), Opt(t.LowLow), Num(t.Deadband), Opt(t.WriteMin), Opt(t.WriteMax),
                    f != null ? f.Key : "",
                }));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        public static Dictionary<int, Tag> Load(string path, int quantity)
        {
            Dictionary<int, DisplayFormat> ignored;
            return Load(path, quantity, out ignored);
        }

        /// <summary>Rows with only offset and format (no tag data) yield a format and no tag.</summary>
        public static Dictionary<int, Tag> Load(string path, int quantity, out Dictionary<int, DisplayFormat> formats)
        {
            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0) throw new FormatException("empty tag map");
            List<string> head = Split(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
            if (head.Count == 0 || head[0] != "offset") throw new FormatException("line 1: header must start with 'offset'");
            var result = new Dictionary<int, Tag>();
            formats = new Dictionary<int, DisplayFormat>();
            int rows = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length == 0) continue;
                if (++rows > MaxRows) throw new FormatException("more than " + MaxRows + " rows");
                List<string> cells = Split(lines[i]);
                Func<string, string> get = col =>
                {
                    int idx = head.IndexOf(col);
                    string v = idx >= 0 && idx < cells.Count ? cells[idx].Trim() : "";
                    if (v.Length > MaxField) throw new FormatException("line " + (i + 1) + ": field '" + col + "' too long");
                    return v;
                };
                try
                {
                    int offset = int.Parse(get("offset"), NumberStyles.Integer, CultureInfo.InvariantCulture);
                    if (offset < 0 || offset >= quantity) throw new FormatException("offset " + offset + " outside 0.." + (quantity - 1));
                    string fmt = get("format");
                    if (fmt.Length > 0) formats[offset] = DisplayFormat.FromKey(fmt); // cut off at the window end: shown as hex, writes refused
                    var t = new Tag
                    {
                        Name = Unescape(get("name")),
                        Scale = ParseOr(get("scale"), 1),
                        Offset = ParseOr(get("offset_eu"), 0),
                        Unit = Unescape(get("unit")),
                        Decimals = get("decimals").Length == 0 ? -1 : int.Parse(get("decimals"), CultureInfo.InvariantCulture),
                        HighHigh = ParseOpt(get("hh")),
                        High = ParseOpt(get("h")),
                        Low = ParseOpt(get("l")),
                        LowLow = ParseOpt(get("ll")),
                        Deadband = ParseOr(get("deadband"), 0),
                        WriteMin = ParseOpt(get("write_min")),
                        WriteMax = ParseOpt(get("write_max")),
                    };
                    if (t.Decimals > 10) throw new FormatException("decimals must be 0..10");
                    t.Validate();
                    if (fmt.Length == 0 || !t.IsDefault) result[offset] = t;
                }
                catch (Exception ex) when (ex is FormatException || ex is OverflowException)
                {
                    throw new FormatException("line " + (i + 1) + ": " + ex.Message);
                }
            }
            return result;
        }

        /// <summary>Undo the formula-injection guard added by <see cref="Quote"/>.</summary>
        static string Unescape(string s)
        {
            return s.Length > 1 && s[0] == '\'' && "=+-@".IndexOf(s[1]) >= 0 ? s.Substring(1) : s;
        }

        static string Num(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }
        static string Opt(double? v) { return v.HasValue ? Num(v.Value) : ""; }
        static double ParseOr(string s, double fallback) { return s.Length == 0 ? fallback : ParseNum(s); }
        static double? ParseOpt(string s) { return s.Length == 0 ? (double?)null : ParseNum(s); }

        static double ParseNum(string s)
        {
            double v = double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new FormatException("'" + s + "' is not a finite number");
            return v;
        }

        public static string Quote(string s)
        {
            if (s == null) return "";
            // prevent spreadsheet formula injection when the CSV is opened in Excel
            if (s.Length > 0 && "=+-@".IndexOf(s[0]) >= 0) s = "'" + s;
            return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        /// <summary>RFC 4180 field split for one line (no embedded newlines).</summary>
        public static List<string> Split(string line)
        {
            var cells = new List<string>();
            var cur = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else cur.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { cells.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(c);
            }
            if (quoted) throw new FormatException("unterminated quote");
            cells.Add(cur.ToString());
            return cells;
        }
    }
}
