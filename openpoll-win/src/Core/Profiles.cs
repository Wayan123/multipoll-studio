using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MultiPollStudio
{
    public sealed class ProfilePoint
    {
        public int Offset;
        public string Name = "";
        public DisplayFormat Format = DisplayFormat.Unsigned;
        public double Scale = 1, OffsetEu;
        public string Unit = "";
        public int Decimals = -1;
        public double? WriteMin, WriteMax;
    }

    public sealed class ProfileBlock
    {
        public string Name = "";
        public int Function = 3, Address, Quantity = 1, ScanRateMs = 1000;
        public List<ProfilePoint> Points = new List<ProfilePoint>();
    }

    public sealed class SettingChoice
    {
        public double Value;
        public string Label = "";
        /// <summary>Serial line after this choice takes effect; null when the choice does not change it.</summary>
        public int? Baud;
        public Parity? Parity;
        public StopBits? StopBits;
        public override string ToString() { return Label; }
    }

    public enum SettingRole { SlaveId, Baud, Parity, Other }
    public enum SettingApply { Immediate, AutoRestart, PowerCycle }

    public sealed class ProfileSetting
    {
        public string Key = "", Name = "";
        public SettingRole Role;
        public int ReadFunction, WriteFunction, Register;
        /// <summary>"u16" (one register) or "f32" (two registers, IEEE float, most significant register first).</summary>
        public string Encoding = "u16";
        public double? Min, Max;
        public List<SettingChoice> Choices = new List<SettingChoice>();
        public SettingApply Apply;
        public bool Verified;
        public string Note = "";

        public int Width { get { return Encoding == "f32" ? 2 : 1; } }

        public double Decode(int[] regs)
        {
            return Encoding == "f32" ? Formats.Number(regs, 0, DisplayFormat.Float).Value : regs[0];
        }

        /// <summary>Validate and encode a new value into registers.</summary>
        public int[] Encode(double value)
        {
            if (Choices.Count > 0 && !Choices.Any(c => c.Value == value)) throw new FormatException(Name + ": " + value + " is not one of the allowed values");
            if (Min.HasValue && value < Min.Value || Max.HasValue && value > Max.Value)
                throw new FormatException(Name + " must be " + Min + ".." + Max);
            return Encoding == "f32" ? Formats.Encode(value, DisplayFormat.Float) : Formats.Encode(value, DisplayFormat.Unsigned);
        }

        public byte[] WritePdu(double value)
        {
            int[] regs = Encode(value);
            if (WriteFunction == 6)
            {
                if (regs.Length != 1) throw new FormatException(Name + ": FC06 writes one register");
                return Pdu.WriteSingleRegister(Register, regs[0]);
            }
            if (WriteFunction == 16) return Pdu.WriteMultipleRegisters(Register, regs);
            throw new FormatException(Name + " is read-only");
        }

        public string Describe(double value)
        {
            SettingChoice c = Choices.FirstOrDefault(x => x.Value == value);
            return c != null ? c.Label : value.ToString("G7", CultureInfo.InvariantCulture);
        }
    }

    public sealed class ActionStep
    {
        public int Function, Register;
        public int[] Values = new int[0];
        /// <summary>Request PDU in hex for non-standard functions (e.g. "42" = PZEM reset energy); null otherwise.</summary>
        public byte[] RawPdu;

        public byte[] Pdu()
        {
            if (RawPdu != null) return RawPdu;
            return Function == 6 ? MultiPollStudio.Pdu.WriteSingleRegister(Register, Values[0]) : MultiPollStudio.Pdu.WriteMultipleRegisters(Register, Values);
        }
    }

    public sealed class ProfileAction
    {
        public string Key = "", Name = "", Confirm = "";
        public List<ActionStep> Steps = new List<ActionStep>();
        public bool Verified;
    }

    public sealed class ProfileRecovery
    {
        public int Address, Function = 3, Register, Count = 1;
        /// <summary>True: the ID is the address the reply comes from; false: the ID is the register value.</summary>
        public bool IdFromReply;
        public bool Verified;
        public string Note = "";
    }

    /// <summary>A device profile: register map (opened as poll windows with tags) plus settings the Device Settings
    /// panel reads and writes. Loaded from JSON (format "multipoll-profile", version 1); unknown keys are errors so
    /// typos do not silently drop a setting.</summary>
    public sealed class DeviceProfile
    {
        public string Id = "", Name = "", Vendor = "", Category = "", Status = "", Notes = "", BeforeWrite = "";
        public List<KeyValuePair<string, string>> Sources = new List<KeyValuePair<string, string>>();
        public int Baud = 9600, DataBits = 8;
        public Parity Parity = Parity.None;
        public StopBits StopBits = StopBits.One;
        public bool BaudFixed;
        public int SlaveId = 1;
        public int IdentifyFunction, IdentifyRegister;
        public Dictionary<int, string> IdentifyValues = new Dictionary<int, string>();
        public List<ProfileBlock> Blocks = new List<ProfileBlock>();
        public List<ProfileSetting> Settings = new List<ProfileSetting>();
        public List<ProfileAction> Actions = new List<ProfileAction>();
        public ProfileRecovery Recovery;
        /// <summary>Where it was loaded from ("built-in" or a file path).</summary>
        public string Origin = "";

        public string Title { get { return Name + (Vendor.Length > 0 ? " (" + Vendor + ")" : ""); } }
        public string LineText { get { return Baud + " " + DataBits + LineSearch.ParityLetter(Parity) + (StopBits == StopBits.Two ? "2" : "1"); } }
        public ProfileSetting Setting(SettingRole role) { return Settings.FirstOrDefault(s => s.Role == role); }
        public override string ToString() { return Category + " · " + Title; }

        /// <summary>Poll documents for every block, with tags, aliases and cell formats; the profile ID is recorded so
        /// the window can open Device Settings later.</summary>
        public List<PollDocument> Documents(int slave)
        {
            var docs = new List<PollDocument>();
            foreach (ProfileBlock b in Blocks)
            {
                var doc = new PollDocument
                {
                    Definition = new PollDefinition { SlaveId = slave, Function = b.Function, Address = b.Address, Quantity = b.Quantity, ScanRateMs = b.ScanRateMs },
                    View = new ViewOptions { PlcAddresses = false, Rows = b.Quantity <= 10 ? 10 : b.Quantity <= 20 ? 20 : 50 },
                    ProfileId = Id,
                    Title = Name + " ID " + slave + " · " + b.Name,
                };
                foreach (ProfilePoint p in b.Points)
                {
                    doc.Aliases[p.Offset] = p.Name;
                    if (p.Format != DisplayFormat.Unsigned) doc.View.CellFormats[p.Offset] = p.Format;
                    var t = new Tag { Name = p.Name, Scale = p.Scale, Offset = p.OffsetEu, Unit = p.Unit, Decimals = p.Decimals, WriteMin = p.WriteMin, WriteMax = p.WriteMax };
                    if (!t.IsDefault) doc.Tags[p.Offset] = t;
                }
                docs.Add(doc);
            }
            return docs;
        }
    }

    public static class Profiles
    {
        static readonly Dictionary<string, Parity> ParityKeys = new Dictionary<string, Parity> { { "N", Parity.None }, { "E", Parity.Even }, { "O", Parity.Odd } };

        /// <summary>User profiles: %LOCALAPPDATA%\MultiPollStudio\profiles\*.json (override built-ins with the same id).</summary>
        public static string UserFolder()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MultiPollStudio", "profiles");
        }

        /// <summary>Built-in profiles (embedded resources "profile.*.json"), then a "profiles" folder next to the EXE,
        /// then the user folder. Later sources replace earlier ones with the same id. Broken files are reported in
        /// <paramref name="errors"/> and skipped.</summary>
        public static List<DeviceProfile> LoadAll(out List<string> errors, string extraFolder = null)
        {
            errors = new List<string>();
            var byId = new Dictionary<string, DeviceProfile>();
            Assembly asm = typeof(Profiles).Assembly;
            foreach (string res in asm.GetManifestResourceNames().Where(n => n.StartsWith("profile.") && n.EndsWith(".json")).OrderBy(n => n))
            {
                try
                {
                    using (var s = new StreamReader(asm.GetManifestResourceStream(res), Encoding.UTF8))
                    {
                        DeviceProfile p = Parse(s.ReadToEnd());
                        p.Origin = "built-in";
                        byId[p.Id] = p;
                    }
                }
                catch (FormatException ex) { errors.Add(res + ": " + ex.Message); }
            }
            string exeDir = Path.GetDirectoryName(asm.Location) ?? "";
            foreach (string dir in new[] { Path.Combine(exeDir, "profiles"), UserFolder(), extraFolder })
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                foreach (string f in Directory.GetFiles(dir, "*.json").OrderBy(f => f))
                {
                    try
                    {
                        if (new FileInfo(f).Length > 1024 * 1024) throw new FormatException("larger than 1 MB");
                        DeviceProfile p = Parse(File.ReadAllText(f, Encoding.UTF8));
                        p.Origin = f;
                        byId[p.Id] = p;
                    }
                    catch (Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException) { errors.Add(f + ": " + ex.Message); }
                }
            }
            return byId.Values.OrderBy(p => p.Category).ThenBy(p => p.Name).ToList();
        }

        // ---------------------------------------------------------------- strict JSON reading

        sealed class Obj
        {
            readonly Dictionary<string, object> _d;
            readonly string _path;
            readonly HashSet<string> _used = new HashSet<string>();

            public Obj(object o, string path)
            {
                _d = o as Dictionary<string, object>;
                _path = path;
                if (_d == null) throw new FormatException(path + ": expected an object");
            }

            public bool Has(string k) { return _d.ContainsKey(k) && _d[k] != null; }

            object Raw(string k, bool required)
            {
                _used.Add(k);
                object v;
                if (_d.TryGetValue(k, out v) && v != null) return v;
                if (required) throw new FormatException(_path + "." + k + " is required");
                return null;
            }

            public string Str(string k, string fallback = null)
            {
                object v = Raw(k, fallback == null);
                if (v == null) return fallback;
                var s = v as string;
                if (s == null) throw new FormatException(_path + "." + k + " must be text");
                if (s.Length > 2000) throw new FormatException(_path + "." + k + " is too long");
                return s;
            }

            public double Num(string k, double? fallback = null)
            {
                object v = Raw(k, !fallback.HasValue);
                if (v == null) return fallback.Value;
                if (v is string || v is bool || v is IDictionary || v is IList) throw new FormatException(_path + "." + k + " must be a number");
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d)) throw new FormatException(_path + "." + k + " must be finite");
                return d;
            }

            public double? OptNum(string k) { return Has(k) ? Num(k) : (double?)null; }

            public int Int(string k, int low, int high, int? fallback = null)
            {
                double d = Num(k, fallback);
                if (d != Math.Floor(d) || d < low || d > high) throw new FormatException(_path + "." + k + " must be an integer " + low + ".." + high);
                return (int)d;
            }

            public bool Bool(string k, bool? fallback = null)
            {
                object v = Raw(k, !fallback.HasValue);
                if (v == null) return fallback.Value;
                if (!(v is bool)) throw new FormatException(_path + "." + k + " must be true or false");
                return (bool)v;
            }

            public List<Obj> List(string k, bool required = false)
            {
                object v = Raw(k, required);
                if (v == null) return new List<Obj>();
                var list = v as IList;
                if (list == null) throw new FormatException(_path + "." + k + " must be a list");
                var r = new List<Obj>();
                for (int i = 0; i < list.Count; i++) r.Add(new Obj(list[i], _path + "." + k + "[" + i + "]"));
                return r;
            }

            public List<double> Nums(string k)
            {
                var list = Raw(k, true) as IList;
                if (list == null) throw new FormatException(_path + "." + k + " must be a list of numbers");
                return list.Cast<object>().Select(o => Convert.ToDouble(o, CultureInfo.InvariantCulture)).ToList();
            }

            public Obj Sub(string k) { object v = Raw(k, false); return v == null ? null : new Obj(v, _path + "." + k); }

            public void Done()
            {
                string extra = _d.Keys.FirstOrDefault(k => !_used.Contains(k));
                if (extra != null) throw new FormatException(_path + ": unknown key '" + extra + "'");
            }
        }

        static T OneOf<T>(Dictionary<string, T> map, string value, string what)
        {
            T r;
            if (!map.TryGetValue(value, out r)) throw new FormatException(what + " must be one of " + string.Join(", ", map.Keys) + ", got '" + value + "'");
            return r;
        }

        public static DeviceProfile Parse(string json)
        {
            Dictionary<string, object> root;
            try { root = Persistence.Parse(json); }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { throw new FormatException("not valid JSON: " + ex.Message); }
            var o = new Obj(root, "profile");
            if (o.Str("format") != "multipoll-profile") throw new FormatException("format must be 'multipoll-profile'");
            if (o.Int("version", 1, 1) != 1) throw new FormatException("version must be 1");
            var p = new DeviceProfile
            {
                Id = o.Str("id"), Name = o.Str("name"), Vendor = o.Str("vendor", ""), Category = o.Str("category", "Other"),
                Status = o.Str("status", "partial"), Notes = o.Str("notes", ""), BeforeWrite = o.Str("before_write", ""),
                SlaveId = o.Int("slave_id", 1, 255, 1),
            };
            if (!System.Text.RegularExpressions.Regex.IsMatch(p.Id, "^[a-z0-9][a-z0-9-]{0,63}$")) throw new FormatException("id must be lowercase kebab-case");
            if (p.Status != "observed" && p.Status != "partial") throw new FormatException("status must be observed or partial");
            foreach (Obj s in o.List("sources"))
            {
                p.Sources.Add(new KeyValuePair<string, string>(s.Str("title"), s.Str("url") + " [" + s.Str("status", "unverified") + "]"));
                s.Done();
            }
            Obj serial = o.Sub("serial");
            if (serial != null)
            {
                p.Baud = serial.Int("baud", 300, 1000000, 9600);
                p.Parity = OneOf(ParityKeys, serial.Str("parity", "N"), "serial.parity");
                p.DataBits = serial.Int("data_bits", 7, 8, 8);
                p.StopBits = serial.Int("stop_bits", 1, 2, 1) == 2 ? StopBits.Two : StopBits.One;
                p.BaudFixed = serial.Bool("baud_fixed", false);
                serial.Done();
            }
            Obj id = o.Sub("identify");
            if (id != null)
            {
                p.IdentifyFunction = id.Int("function", 3, 4);
                p.IdentifyRegister = id.Int("register", 0, 65535);
                foreach (Obj v in id.List("values", true)) { p.IdentifyValues[v.Int("value", 0, 65535)] = v.Str("label"); v.Done(); }
                id.Done();
            }
            foreach (Obj b in o.List("blocks", true))
            {
                var block = new ProfileBlock
                {
                    Name = b.Str("name"), Function = b.Int("function", 1, 4), Address = b.Int("address", 0, 65535),
                    Quantity = b.Int("quantity", 1, Pdu.MaxReadRegisters), ScanRateMs = b.Int("scan_rate_ms", 50, 3600000, 1000),
                };
                if (block.Address + block.Quantity > 0x10000) throw new FormatException("block '" + block.Name + "' runs past address 65535");
                foreach (Obj pt in b.List("points"))
                {
                    var point = new ProfilePoint
                    {
                        Offset = pt.Int("offset", 0, block.Quantity - 1), Name = pt.Str("name"),
                        Format = DisplayFormat.FromKey(pt.Str("format", "u16")), Scale = pt.Num("scale", 1), OffsetEu = pt.Num("offset_eu", 0),
                        Unit = pt.Str("unit", ""), Decimals = pt.Int("decimals", -1, 10, -1), WriteMin = pt.OptNum("write_min"), WriteMax = pt.OptNum("write_max"),
                    };
                    if (point.Offset + point.Format.Width > block.Quantity)
                        throw new FormatException("point '" + point.Name + "' (" + point.Format.Key + ") runs past the end of block '" + block.Name + "'");
                    if (point.Scale == 0) throw new FormatException("point '" + point.Name + "': scale must not be 0");
                    pt.Done();
                    block.Points.Add(point);
                }
                b.Done();
                p.Blocks.Add(block);
            }
            if (p.Blocks.Count == 0) throw new FormatException("a profile needs at least one block");
            var roles = new Dictionary<string, SettingRole> { { "slave_id", SettingRole.SlaveId }, { "baud", SettingRole.Baud }, { "parity", SettingRole.Parity }, { "other", SettingRole.Other } };
            var applies = new Dictionary<string, SettingApply> { { "immediate", SettingApply.Immediate }, { "auto_restart", SettingApply.AutoRestart }, { "power_cycle", SettingApply.PowerCycle } };
            foreach (Obj s in o.List("settings"))
            {
                var st = new ProfileSetting
                {
                    Key = s.Str("key"), Name = s.Str("name"), Role = OneOf(roles, s.Str("role", "other"), "role"),
                    ReadFunction = s.Int("read_function", 0, 4, 3), WriteFunction = s.Int("write_function", 0, 16, 6), Register = s.Int("register", 0, 65535),
                    Encoding = s.Str("encoding", "u16"), Min = s.OptNum("min"), Max = s.OptNum("max"),
                    Apply = OneOf(applies, s.Str("apply", "immediate"), "apply"), Verified = s.Bool("verified", false), Note = s.Str("note", ""),
                };
                if (st.Encoding != "u16" && st.Encoding != "f32") throw new FormatException("setting '" + st.Key + "': encoding must be u16 or f32");
                if (st.WriteFunction != 0 && st.WriteFunction != 6 && st.WriteFunction != 16) throw new FormatException("setting '" + st.Key + "': write_function must be 0, 6 or 16");
                if (st.ReadFunction == 1 || st.ReadFunction == 2) throw new FormatException("setting '" + st.Key + "': read_function must be 0, 3 or 4");
                if (st.Encoding == "f32" && st.WriteFunction == 6) throw new FormatException("setting '" + st.Key + "': a float needs FC16");
                foreach (Obj c in s.List("choices"))
                {
                    var ch = new SettingChoice { Value = c.Num("value"), Label = c.Str("label") };
                    if (c.Has("baud")) ch.Baud = c.Int("baud", 300, 1000000);
                    if (c.Has("parity")) ch.Parity = OneOf(ParityKeys, c.Str("parity"), "choice.parity");
                    if (c.Has("stop_bits")) ch.StopBits = c.Int("stop_bits", 1, 2) == 2 ? StopBits.Two : StopBits.One;
                    c.Done();
                    st.Choices.Add(ch);
                }
                if (st.Choices.Count == 0 && st.WriteFunction != 0 && (!st.Min.HasValue || !st.Max.HasValue))
                    throw new FormatException("setting '" + st.Key + "' needs choices or min and max");
                s.Done();
                p.Settings.Add(st);
            }
            foreach (Obj a in o.List("actions"))
            {
                var act = new ProfileAction { Key = a.Str("key"), Name = a.Str("name"), Confirm = a.Str("confirm", ""), Verified = a.Bool("verified", false) };
                foreach (Obj step in a.List("steps", true))
                {
                    var st = new ActionStep();
                    if (step.Has("raw_pdu"))
                    {
                        st.RawPdu = Hex.Parse(step.Str("raw_pdu"));
                        if (st.RawPdu.Length == 0 || st.RawPdu.Length > 253) throw new FormatException("action '" + act.Key + "': raw_pdu must be 1..253 bytes");
                    }
                    else
                    {
                        st.Function = step.Int("function", 6, 16);
                        if (st.Function != 6 && st.Function != 16) throw new FormatException("action '" + act.Key + "': function must be 6 or 16");
                        st.Register = step.Int("register", 0, 65535);
                        st.Values = step.Has("value") ? new[] { step.Int("value", 0, 65535) } : step.Nums("values").Select(v => (int)v).ToArray();
                        if (st.Values.Length == 0 || st.Function == 6 && st.Values.Length != 1) throw new FormatException("action '" + act.Key + "': bad values");
                    }
                    step.Done();
                    act.Steps.Add(st);
                }
                a.Done();
                p.Actions.Add(act);
            }
            Obj r = o.Sub("recovery");
            if (r != null)
            {
                p.Recovery = new ProfileRecovery
                {
                    Address = r.Int("address", 1, 255), Function = r.Int("function", 3, 4), Register = r.Int("register", 0, 65535), Count = r.Int("count", 1, 4, 1),
                    Verified = r.Bool("verified", false), Note = r.Str("note", ""),
                };
                string from = r.Str("id_from", "reply");
                if (from != "reply" && from != "register") throw new FormatException("recovery.id_from must be reply or register");
                p.Recovery.IdFromReply = from == "reply";
                r.Done();
            }
            o.Done();
            return p;
        }
    }

    /// <summary>Device Settings operations that run inside an engine job (on the engine thread, with the bus to
    /// themselves). Writes go through <see cref="PollEngine.Write"/> so protection, confirmation and audit apply.</summary>
    public static class DeviceOps
    {
        /// <summary>Current value of a readable setting.</summary>
        public static double Read(ModbusMaster m, int slave, ProfileSetting s)
        {
            if (s.ReadFunction == 0) throw new FormatException(s.Name + " cannot be read back");
            return s.Decode(m.Transact(slave, Pdu.ReadRequest(s.ReadFunction, s.Register, s.Width)));
        }

        /// <summary>Find a forgotten slave ID with the profile's recovery query (one device on the bus). Read-only.</summary>
        public static int Recover(ModbusMaster m, ProfileRecovery r)
        {
            int responder;
            byte[] request = Pdu.ReadRequest(r.Function, r.Register, r.Count);
            byte[] response = m.Execute(r.Address, request, true, out responder);
            int[] values = Pdu.ParseResponse(request, response);
            return r.IdFromReply ? responder : values[0];
        }

        /// <summary>Execute a non-standard request PDU (profile actions such as PZEM 0x42) and check the reply function.</summary>
        public static byte[] Custom(ModbusMaster m, int slave, byte[] pdu)
        {
            byte[] response = m.Execute(slave, pdu);
            if (response == null) return null;
            if (response.Length >= 2 && response[0] == (pdu[0] | 0x80))
            {
                var status = (Status)(0x80 | response[1]);
                if (!Enum.IsDefined(typeof(Status), status)) status = Status.UnknownException;
                throw new ModbusException(status, "exception code " + response[1].ToString("X2"));
            }
            if (response[0] != pdu[0]) throw new ModbusException(Status.ResponseError, "function " + response[0].ToString("X2") + " in reply to " + pdu[0].ToString("X2"));
            return response;
        }
    }
}
