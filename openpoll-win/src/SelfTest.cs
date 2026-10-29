using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace MultiPollStudio
{
    /// <summary>"MultiPollStudio.exe --selftest [host port]": offline checks of framing/formats/addressing, plus live
    /// checks of every function code against a slave whose holding registers 0..3 hold 100, 65535, 0x3FC0, 0x0000
    /// (the repo's tools/demo_slave.py). Prints one line per check; exit code = number of failures.</summary>
    static class SelfTest
    {
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

        static int _failures;
        static TextWriter _out;

        static void Check(string name, bool ok, string detail = "")
        {
            _out.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok || detail.Length == 0 ? "" : "  -- " + detail));
            if (!ok) _failures++;
        }

        static void Expect(string name, Action body)
        {
            try { body(); Check(name, true); }
            catch (Exception ex) { Check(name, false, ex.GetType().Name + ": " + ex.Message); }
        }

        static void Throws<T>(string name, Action body, Func<T, bool> predicate = null) where T : Exception
        {
            try { body(); Check(name, false, "no exception"); }
            catch (T ex) { Check(name, predicate == null || predicate(ex), ex.Message); }
            catch (Exception ex) { Check(name, false, "wrong exception " + ex.GetType().Name + ": " + ex.Message); }
        }

        static void Eq<T>(string name, T expected, T actual)
        {
            var e = expected as System.Collections.IEnumerable;
            var a = actual as System.Collections.IEnumerable;
            if (e != null && !(expected is string))
            {
                var el = e.Cast<object>().ToList();
                var al = a == null ? null : a.Cast<object>().ToList();
                Check(name, al != null && el.SequenceEqual(al),
                      "expected [" + string.Join(",", el) + "] got [" + (al == null ? "null" : string.Join(",", al)) + "]");
                return;
            }
            Check(name, Equals(expected, actual), "expected " + expected + " got " + actual);
        }

        public static int Run(string[] args)
        {
            AttachConsole(-1);
            string outPath = args.FirstOrDefault(a => a.StartsWith("--out="));
            _out = outPath != null ? new StreamWriter(outPath.Substring(6)) { AutoFlush = true } : Console.Out;
            var positional = args.Skip(1).Where(a => !a.StartsWith("--")).ToArray();
            try
            {
                Offline();
                string serial = args.FirstOrDefault(a => a.StartsWith("--serial="));
                if (serial != null) Serial(serial.Substring(9), args);
                else if (positional.Length >= 2) Live(positional[0], int.Parse(positional[1], CultureInfo.InvariantCulture));
                _out.WriteLine(_failures == 0 ? "SELFTEST OK" : "SELFTEST FAILED: " + _failures);
            }
            finally
            {
                if (outPath != null) _out.Dispose();
            }
            return _failures;
        }

        static string Arg(string[] args, string name, string fallback)
        {
            string a = args.FirstOrDefault(x => x.StartsWith("--" + name + "="));
            return a == null ? fallback : a.Substring(name.Length + 3);
        }

        /// <summary>Read-only check against a real serial slave: FC03 only, never writes.
        /// --serial=COM26 [--baud=9600] [--slave=1] [--address=0] [--count=8] [--polls=5] [--rate=1000]</summary>
        static void Serial(string port, string[] args)
        {
            var s = new ConnectionSettings
            {
                Kind = ConnectionKind.Serial, Mode = SerialMode.Rtu, Port = port,
                BaudRate = int.Parse(Arg(args, "baud", "9600"), CultureInfo.InvariantCulture), ResponseTimeoutMs = 1000,
            };
            int slave = int.Parse(Arg(args, "slave", "1"), CultureInfo.InvariantCulture);
            int address = int.Parse(Arg(args, "address", "0"), CultureInfo.InvariantCulture);
            int count = int.Parse(Arg(args, "count", "8"), CultureInfo.InvariantCulture);
            int polls = int.Parse(Arg(args, "polls", "5"), CultureInfo.InvariantCulture);
            int rate = int.Parse(Arg(args, "rate", "1000"), CultureInfo.InvariantCulture);
            _out.WriteLine("serial " + s.Describe() + " slave " + slave + " FC03 address " + address + " count " + count);
            using (var m = new ModbusMaster(s) { OnTraffic = (dir, bytes) => _out.WriteLine("      " + Traffic.Line(0, dir, bytes)) })
            {
                try
                {
                    m.Connect();
                    Check("open " + port, true);
                }
                catch (ModbusException ex)
                {
                    Check("open " + port, false, ex.Describe());
                    return;
                }
                int ok = 0;
                for (int i = 0; i < polls; i++)
                {
                    try
                    {
                        int[] v = m.Transact(slave, Pdu.ReadRequest(3, address, count));
                        ok++;
                        _out.WriteLine("      poll " + (i + 1) + ": [" + string.Join(", ", v) + "]  signed[0]=" + (short)v[0]);
                    }
                    catch (ModbusException ex)
                    {
                        _out.WriteLine("      poll " + (i + 1) + ": " + ex.Describe());
                    }
                    System.Threading.Thread.Sleep(rate);
                }
                Check("serial FC03 responses " + ok + "/" + polls, ok == polls);
            }
        }

        static void Offline()
        {
            Eq("crc matches spec (01 03 00 00 00 0A -> C5 CD)", Hex.Parse("01030000000AC5CD"), Framing.RtuFrame(1, Hex.Parse("030000000A")));
            Check("lrc matches Modbus Poll help example (F2)",
                  System.Text.Encoding.ASCII.GetString(TestCenter.AddCheck(Hex.Parse("3A303130333030303030303041"), CheckKind.Lrc)).EndsWith("F2\r\n"));
            byte[] af = Framing.AsciiFrame(17, Hex.Parse("03006B0003"));
            Eq("ascii frame", ":1103006B00037E\r\n", System.Text.Encoding.ASCII.GetString(af));
            Eq("ascii unframe", Hex.Parse("03006B0003"), Framing.AsciiUnframe(af, 17));
            Throws<ModbusException>("ascii bad lrc", () => Framing.AsciiUnframe(System.Text.Encoding.ASCII.GetBytes(":1103006B00037F\r\n"), 17),
                                    e => e.Status == Status.CrcError);
            byte[] good = Framing.RtuFrame(1, Hex.Parse("03020007"));
            byte[] bad = (byte[])good.Clone();
            bad[bad.Length - 1] ^= 1;
            Throws<ModbusException>("rtu bad crc", () => Framing.RtuUnframe(bad, 1), e => e.Status == Status.CrcError);
            Throws<ModbusException>("rtu wrong slave", () => Framing.RtuUnframe(good, 2), e => e.Status == Status.ResponseError);
            Throws<ModbusException>("mbap transaction mismatch", () => Framing.MbapUnframe(Framing.MbapFrame(5, 1, Hex.Parse("03020001")), 6, 1),
                                    e => e.Status == Status.TransactionIdError);
            Eq("test center separators", Hex.Parse("01030000000A"), Hex.ParseSeparated("01,03.00 00\t00 0a"));
            Eq("traffic normalizes direction", "000001 | TX | 01 03", Traffic.Line(1, "Tx", new byte[] { 1, 3 }));
            Eq("traffic sequence wraps", "000000 | RX | FF", Traffic.Line(1000000, "rx", new byte[] { 255 }));
            string frames = Path.Combine(Path.GetTempPath(), "multipoll-frames-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                TestCenter.Save(frames, new[] { "01 03", "02 04" });
                Eq("frame list header", "MultiPoll Studio Frame List", File.ReadAllLines(frames)[0]);
                Eq("frame list round trip", new[] { "01 03", "02 04" }, TestCenter.Load(frames).ToArray());
                File.WriteAllLines(frames, new[] { "Test Center", "01 03" });
                Throws<FormatException>("old frame list header rejected", () => TestCenter.Load(frames));
                File.WriteAllText(frames, "");
                Throws<FormatException>("empty frame list rejected", () => TestCenter.Load(frames));
            }
            finally { if (File.Exists(frames)) File.Delete(frames); }
            Throws<ModbusException>("exception response -> status",
                                    () => Pdu.ParseResponse(Pdu.ReadRequest(3, 0, 1), new byte[] { 0x83, 0x02 }), e => e.Status == Status.IllegalDataAddress);
            Throws<ModbusException>("byte count mismatch",
                                    () => Pdu.ParseResponse(Pdu.ReadRequest(3, 0, 2), new byte[] { 3, 2, 0, 1 }), e => e.Status == Status.ByteCountError);
            Throws<ArgumentException>("quantity limit 126 registers", () => Pdu.ReadRequest(3, 0, 126));
            Throws<ArgumentException>("quantity limit 2001 coils", () => Pdu.ReadRequest(1, 0, 2001));
            Throws<ArgumentException>("address span cannot wrap", () => Pdu.ReadRequest(3, 65530, 10));

            var cases = new[]
            {
                Tuple.Create(DisplayFormat.Signed, "-2"), Tuple.Create(DisplayFormat.Unsigned, "65535"),
                Tuple.Create(DisplayFormat.Long, "-123456789"), Tuple.Create(DisplayFormat.LongInverse, "2147483647"),
                Tuple.Create(DisplayFormat.Float, "1.5"), Tuple.Create(DisplayFormat.FloatInverse, "-0.25"),
                Tuple.Create(DisplayFormat.Double, "3.14159265358979"), Tuple.Create(DisplayFormat.DoubleInverse, "-1E+300"),
            };
            foreach (var c in cases)
                Eq("round trip " + c.Item1.Label + " " + c.Item2, c.Item2, Formats.Format(Formats.Parse(c.Item2, c.Item1), 0, c.Item1));
            Eq("float word order", new[] { 0x3F80, 0 }, Formats.Encode(1.0, DisplayFormat.Float));
            Eq("float inverse word order", new[] { 0, 0x3F80 }, Formats.Encode(1.0, DisplayFormat.FloatInverse));
            Eq("long inverse word order", new[] { 0x5678, 0x1234 }, Formats.Encode(0x12345678, DisplayFormat.LongInverse));
            Throws<FormatException>("signed range", () => Formats.Encode(32768, DisplayFormat.Signed));
            Throws<FormatException>("unsigned range", () => Formats.Encode(-1, DisplayFormat.Unsigned));
            Throws<FormatException>("float range", () => Formats.Encode(1e40, DisplayFormat.Float));
            Eq("hex display", "0xBEEF", Formats.Format(new[] { 0xBEEF }, 0, DisplayFormat.Hex));
            Eq("binary display", "1010 0101 1111 0000", Formats.Format(new[] { 0xA5F0 }, 0, DisplayFormat.Binary));
            Eq("binary parse", new[] { 0xA5F0 }, Formats.Parse("1010 0101 1111 0000", DisplayFormat.Binary));
            Eq("float parse with comma", new[] { 0x4020, 0 }, Formats.Parse("2,5", DisplayFormat.Float));
            DataTypesOffline();
            DevicesOffline();
            Eq("plc label 40001", "40001", Addressing.Label(3, 0, true));
            Eq("plc label 30010", "30010", Addressing.Label(4, 9, true));
            Eq("plc label 00001", "00001", Addressing.Label(1, 0, true));
            Eq("plc label 110000", "110000", Addressing.Label(2, 9999, true));
            Eq("protocol label", "00000", Addressing.Label(3, 0, false));

            var doc = new PollDocument();
            doc.Definition.Address = 7;
            doc.View.Display = DisplayFormat.FloatInverse;
            doc.View.CellFormats[2] = DisplayFormat.Hex;
            doc.View.CellFormats[4] = new DisplayFormat(DataType.String, ByteOrder.BADC, 3);
            doc.Aliases[1] = "Setpoint";
            string tmp = Path.Combine(Path.GetTempPath(), "multipoll-selftest.mpoll");
            Persistence.SaveDocument(tmp, doc);
            PollDocument back = Persistence.LoadDocument(tmp);
            File.Delete(tmp);
            Check("document round trip", back.Definition.Address == 7 && back.View.Display == DisplayFormat.FloatInverse
                                         && back.View.CellFormats[2] == DisplayFormat.Hex && back.Aliases[1] == "Setpoint"
                                         && back.View.CellFormats[4] == new DisplayFormat(DataType.String, ByteOrder.BADC, 3));
            var viewDict = (Dictionary<string, object>)Persistence.ToDict(doc)["view"];
            Eq("document keeps Modbus Poll keys", "float_inverse", (string)viewDict["display"]);
            Wave1Offline();
        }

        /// <summary>Diagnosis table, line search ordering, profile loading and strict schema, frame handling.</summary>
        static void DevicesOffline()
        {
            var serial = new ConnectionSettings { Kind = ConnectionKind.Serial, Port = "COM9", BaudRate = 9600, ResponseTimeoutMs = 100 };
            var def = new PollDefinition { Function = 3, Address = 5, Quantity = 10 };
            Func<Status, string, ConnectionSettings, List<Advice>> dx = (s, d, c) => Diagnosis.For(s, d, c, def);
            Eq("dx timeout -> find device", FixKind.FindDevice, dx(Status.Timeout, "", serial)[0].Fix);
            Check("dx timeout short timeout -> raise", dx(Status.Timeout, "", serial).Any(a => a.Fix == FixKind.RaiseTimeout && a.FixValue >= 500));
            Check("dx timeout long timeout -> no raise", !dx(Status.Timeout, "", new ConnectionSettings { Kind = ConnectionKind.Serial, ResponseTimeoutMs = 5000 }).Any(a => a.Fix == FixKind.RaiseTimeout));
            Eq("dx illegal function FC03 -> switch", FixKind.SwitchFunction, dx(Status.IllegalFunction, "", serial)[0].Fix);
            Check("dx illegal function label", dx(Status.IllegalFunction, "", serial)[0].FixLabel.Contains("FC04"));
            Eq("dx illegal address -> probe", FixKind.ProbeRange, dx(Status.IllegalDataAddress, "", serial)[0].Fix);
            Check("dx illegal address -> shift -1", dx(Status.IllegalDataAddress, "", serial).Any(a => a.Fix == FixKind.ShiftAddress && a.FixValue == -1));
            Advice resp = dx(Status.ResponseError, "slave 7 answered, expected 1", serial)[0];
            Check("dx other slave -> use responder 7", resp.Fix == FixKind.UseResponder && resp.FixValue == 7, resp.Title);
            Eq("dx echo -> remove echo", FixKind.RemoveEcho, dx(Status.ResponseError, "write echo does not match request", serial)[0].Fix);
            Check("dx port busy title", dx(Status.NoConnection, "Port COM9 not available: Access to the port 'COM9' is denied.", serial)[0].Title.Contains("in use"));
            Check("dx port missing title", dx(Status.NoConnection, "Port COM9 not available: The port 'COM9' does not exist.", serial)[0].Title.Contains("not found"));
            var tcp = new ConnectionSettings { Kind = ConnectionKind.Tcp, Host = "10.0.0.5", TcpPort = 502 };
            Check("dx tcp refused mentions port", dx(Status.NoConnection, "No connection could be made because the target machine actively refused it", tcp)[0].Causes[0].Contains("502"));
            Eq("dx gateway target -> scan", FixKind.FindDevice, dx(Status.GatewayTargetFailed, "", tcp)[0].Fix);
            Check("dx every status has advice", Diagnosis.GuideStatuses.All(s => dx(s, "", serial).Count > 0 && dx(s, "", tcp).Count > 0));
            Check("troubleshooting guide text", Diagnosis.Guide().Contains("== Timeout Error ==") && Diagnosis.Guide().Contains("one-click fix"));

            var lo = new LineSearchOptions { Bauds = new[] { 9600, 4800 }, Parities = new[] { System.IO.Ports.Parity.None, System.IO.Ports.Parity.Even }, TryTwoStopBits = true, FromId = 1, ToId = 5, FullRange = false };
            Eq("line search order", "9600N1 9600N2 9600E1 4800N1 4800N2 4800E1",
               string.Join(" ", LineSearch.Settings(lo).Select(s => s.Item1 + LineSearch.ParityLetter(s.Item2) + (s.Item3 == System.IO.Ports.StopBits.Two ? "2" : "1"))));
            Eq("line search quick IDs in range", "1 2 3", string.Join(" ", LineSearch.Ids(lo)));
            lo.FullRange = true;
            Eq("line search full IDs, quick first", "1 2 3 4 5", string.Join(" ", LineSearch.Ids(lo)));
            Check("probe timeout at 1200 baud ≥ wire time", LineSearch.ProbeTimeoutMs(1200) > SerialTiming.TransactionMs(1200, 3, 1, SerialMode.Rtu));
            Eq("probe timeout floor", 150, LineSearch.ProbeTimeoutMs(115200));

            // frames: PZEM reset (FC 0x42) length, any-responder unframe (0xFE query answered by ID 4)
            Eq("rtu length FC42", 4, Framing.RtuResponseLength(new byte[] { 2, 0x42, 0 }, 3));
            byte[] fromFour = Framing.RtuFrame(4, Pdu.ReadRequest(3, 0, 1).Take(1).Concat(new byte[] { 2, 0, 7 }).ToArray());
            Eq("rtu any responder", new byte[] { 3, 2, 0, 7 }, Framing.RtuUnframe(fromFour, -1));
            Throws<ModbusException>("rtu fixed responder still checked", () => Framing.RtuUnframe(fromFour, 254), e => e.Status == Status.ResponseError);

            // built-in profiles
            List<string> errors;
            List<DeviceProfile> all = Profiles.LoadAll(out errors);
            Check("built-in profiles load without errors (" + all.Count + ")", all.Count >= 10 && errors.Count == 0, string.Join("; ", errors));
            DeviceProfile adm = all.FirstOrDefault(p => p.Id == "adm-4204v");
            Check("adm-4204v profile", adm != null && adm.Setting(SettingRole.SlaveId).Register == 0xA0 && adm.Setting(SettingRole.Baud).Choices.Count == 8
                                       && adm.Setting(SettingRole.Baud).Choices.First(c => c.Value == 4).Baud == 19200);
            if (adm != null)
            {
                Eq("adm set id 9 -> FC06 00A0 0009", new byte[] { 6, 0, 0xA0, 0, 9 }, adm.Setting(SettingRole.SlaveId).WritePdu(9));
                Throws<FormatException>("adm id 255 out of range", () => adm.Setting(SettingRole.SlaveId).WritePdu(255));
                Throws<FormatException>("adm baud code 8 not a choice", () => adm.Setting(SettingRole.Baud).WritePdu(8));
                Eq("adm factory reset steps", "06 00 A7 AA 55 | 06 00 A7 55 AA", string.Join(" | ", adm.Actions[0].Steps.Select(s => Hex.Format(s.Pdu()))));
                List<PollDocument> docs = adm.Documents(3);
                Check("adm documents", docs.Count == adm.Blocks.Count && docs[0].Definition.SlaveId == 3 && docs[0].Aliases[0] == "AO0"
                                       && docs[0].Tags[0].WriteMax == 10000 && docs[0].ProfileId == "adm-4204v", docs[0].Title);
            }
            DeviceProfile sdm = all.FirstOrDefault(p => p.Id == "sdm120-m");
            Check("sdm120 settings are FC16 float", sdm != null && sdm.Setting(SettingRole.SlaveId).WritePdu(5).SequenceEqual(new byte[] { 16, 0, 0x14, 0, 2, 4, 0x40, 0xA0, 0, 0 }));
            DeviceProfile pzem = all.FirstOrDefault(p => p.Id == "pzem-016");
            Check("pzem baud fixed, reset 0x42, recovery 0xF8", pzem != null && pzem.BaudFixed && pzem.Setting(SettingRole.Baud) == null
                                                                && pzem.Actions[0].Steps[0].RawPdu.SequenceEqual(new byte[] { 0x42 }) && pzem.Recovery.Address == 0xF8);
            Check("pzem current is u32 low word first", pzem != null && pzem.Blocks.SelectMany(b => b.Points).First(p => p.Name.StartsWith("Current")).Format.Key == "u32_cdab");
            DeviceProfile bgt = all.FirstOrDefault(p => p.Id == "bgt-smps");
            Check("bgt id register 0x30, recovery 0xFE from reply", bgt != null && bgt.Setting(SettingRole.SlaveId).Register == 0x30 && bgt.Recovery.Address == 0xFE && bgt.Recovery.IdFromReply);

            // strict schema
            string good = "{\"format\":\"multipoll-profile\",\"version\":1,\"id\":\"t\",\"name\":\"T\",\"blocks\":[{\"name\":\"b\",\"function\":3,\"address\":0,\"quantity\":2,"
                          + "\"points\":[{\"offset\":0,\"name\":\"v\",\"format\":\"f32\"}]}]}";
            Check("profile minimal parses", Profiles.Parse(good).Blocks[0].Points[0].Format == DisplayFormat.Float);
            Throws<FormatException>("profile unknown key", () => Profiles.Parse(good.Replace("\"name\":\"T\"", "\"name\":\"T\",\"nmae\":1")), e => e.Message.Contains("nmae"));
            Throws<FormatException>("profile point past block end", () => Profiles.Parse(good.Replace("\"quantity\":2", "\"quantity\":1")), e => e.Message.Contains("past the end"));
            Throws<FormatException>("profile bad id", () => Profiles.Parse(good.Replace("\"id\":\"t\"", "\"id\":\"Bad Id\"")));
            Throws<FormatException>("profile float setting via FC06", () => Profiles.Parse(good.Replace("]}]}", "]}],\"settings\":[{\"key\":\"x\",\"name\":\"x\",\"register\":1,\"encoding\":\"f32\",\"write_function\":6,\"min\":0,\"max\":1}]}")));
            Throws<FormatException>("profile not JSON", () => Profiles.Parse("{"));

            // documents keep connection, profile and title
            var dd = new PollDocument { ProfileId = "bgt-smps", Title = "Soil A", Connection = new ConnectionSettings { Kind = ConnectionKind.Serial, Port = "COM7", BaudRate = 4800 } };
            PollDocument back = Persistence.FromDict(Persistence.Parse(Persistence.Serialize(Persistence.ToDict(dd))));
            Check("document keeps own connection, profile, title", back.ProfileId == "bgt-smps" && back.Title == "Soil A" && back.Connection.Port == "COM7" && back.Connection.BaudRate == 4800);
            Check("document without connection has no key", !Persistence.ToDict(new PollDocument()).ContainsKey("connection"));
        }

        /// <summary>N1: every data type × byte order against hand-computed register vectors.</summary>
        static void DataTypesOffline()
        {
            Func<DataType, ByteOrder, DisplayFormat> F = (t, o) => new DisplayFormat(t, o);
            // float 1.5 = 0x3FC00000 in all four orders
            var f15 = new Dictionary<ByteOrder, int[]>
            {
                { ByteOrder.ABCD, new[] { 0x3FC0, 0x0000 } }, { ByteOrder.CDAB, new[] { 0x0000, 0x3FC0 } },
                { ByteOrder.BADC, new[] { 0xC03F, 0x0000 } }, { ByteOrder.DCBA, new[] { 0x0000, 0xC03F } },
            };
            foreach (var kv in f15)
            {
                Eq("f32 1.5 encode " + kv.Key, kv.Value, Formats.Parse("1.5", F(DataType.Float32, kv.Key)));
                Eq("f32 1.5 decode " + kv.Key, "1.5", Formats.Format(kv.Value, 0, F(DataType.Float32, kv.Key)));
            }
            Eq("i32 0x12345678 BADC", new[] { 0x3412, 0x7856 }, Formats.Parse("305419896", F(DataType.Int32, ByteOrder.BADC)));
            Eq("i32 0x12345678 DCBA", new[] { 0x7856, 0x3412 }, Formats.Parse("305419896", F(DataType.Int32, ByteOrder.DCBA)));
            Eq("i32 -2 CDAB", new[] { 0xFFFE, 0xFFFF }, Formats.Parse("-2", F(DataType.Int32, ByteOrder.CDAB)));
            Eq("u32 max", "4294967295", Formats.Format(new[] { 0xFFFF, 0xFFFF }, 0, F(DataType.UInt32, ByteOrder.ABCD)));
            Throws<FormatException>("i32 overflow rejected", () => Formats.Parse("2147483648", DisplayFormat.Long));
            Eq("u16 BA byte swap", "13330", Formats.Format(new[] { 0x1234 }, 0, F(DataType.UInt16, ByteOrder.BADC)));
            Check("u16 CDAB folds into ABCD", F(DataType.UInt16, ByteOrder.CDAB) == DisplayFormat.Unsigned);
            Eq("signed accepts raw pattern 0xFFFF", new[] { 0xFFFF }, Formats.Parse("0xFFFF", DisplayFormat.Signed));
            Throws<FormatException>("unsigned pattern too wide", () => Formats.Parse("0x10000", DisplayFormat.Unsigned));
            // 64-bit: exact text beyond 2^53
            Eq("i64 max exact", "9223372036854775807",
               Formats.Format(Formats.Parse("9223372036854775807", F(DataType.Int64, ByteOrder.ABCD)), 0, F(DataType.Int64, ByteOrder.ABCD)));
            Eq("i64 -1", new[] { 0xFFFF, 0xFFFF, 0xFFFF, 0xFFFF }, Formats.Parse("-1", F(DataType.Int64, ByteOrder.DCBA)));
            Eq("u64 max exact", "18446744073709551615",
               Formats.Format(Formats.Parse("18446744073709551615", F(DataType.UInt64, ByteOrder.CDAB)), 0, F(DataType.UInt64, ByteOrder.CDAB)));
            Eq("i64 CDAB word order", new[] { 0x0004, 0x0003, 0x0002, 0x0001 },
               Formats.Parse("0x0001000200030004", F(DataType.Int64, ByteOrder.CDAB)));
            Eq("f64 pi ABCD", new[] { 0x4009, 0x21FB, 0x5444, 0x2D18 }, Formats.Encode(Math.PI, F(DataType.Float64, ByteOrder.ABCD)));
            Eq("f64 pi DCBA", new[] { 0x182D, 0x4454, 0xFB21, 0x0940 }, Formats.Encode(Math.PI, F(DataType.Float64, ByteOrder.DCBA)));
            // BCD
            Eq("bcd16 1234", new[] { 0x1234 }, Formats.Parse("1234", F(DataType.Bcd16, ByteOrder.ABCD)));
            Eq("bcd16 invalid nibble shown", "BCD? 0x12A4", Formats.Format(new[] { 0x12A4 }, 0, F(DataType.Bcd16, ByteOrder.ABCD)));
            Check("bcd16 invalid has no number", Formats.Number(new[] { 0x12A4 }, 0, F(DataType.Bcd16, ByteOrder.ABCD)) == null);
            Throws<FormatException>("bcd16 range", () => Formats.Parse("10000", F(DataType.Bcd16, ByteOrder.ABCD)));
            Eq("bcd32 CDAB", new[] { 0x5678, 0x1234 }, Formats.Parse("12345678", F(DataType.Bcd32, ByteOrder.CDAB)));
            // Unix time: 2026-01-01T00:00:00Z = 1767225600 = 0x6955B900
            var unix = F(DataType.UnixTime32, ByteOrder.ABCD);
            Eq("unix32 decode", "2026-01-01T00:00:00Z", Formats.Format(new[] { 0x6955, 0xB900 }, 0, unix));
            Eq("unix32 parse ISO with offset", new[] { 0x6955, 0xB900 }, Formats.Parse("2026-01-01T07:00:00+07:00", unix));
            Eq("unix32 parse seconds", new[] { 0x6955, 0xB900 }, Formats.Parse("1767225600", unix));
            // strings
            var s3 = new DisplayFormat(DataType.String, ByteOrder.ABCD, 3);
            Eq("string encode NUL padded", new[] { 0x4865, 0x6C6C, 0x6F00 }, Formats.Parse("Hello", s3));
            Eq("string decode", "Hello", Formats.Format(new[] { 0x4865, 0x6C6C, 0x6F00 }, 0, s3));
            Eq("string BADC (byte-swapped registers)", "Hello",
               Formats.Format(new[] { 0x6548, 0x6C6C, 0x006F }, 0, new DisplayFormat(DataType.String, ByteOrder.BADC, 3)));
            Eq("string CDAB (reversed registers)", "Hello",
               Formats.Format(new[] { 0x6F00, 0x6C6C, 0x4865 }, 0, new DisplayFormat(DataType.String, ByteOrder.CDAB, 3)));
            Throws<FormatException>("string too long", () => Formats.Parse("Hello!!", s3));
            Throws<FormatException>("string non-ASCII", () => Formats.Parse("Grüß", s3));
            Throws<FormatException>("string length limit", () => new DisplayFormat(DataType.String, ByteOrder.ABCD, 65));
            // bit fields: bits 4..6 of 0x00F5 = 7
            var bits = new DisplayFormat(DataType.BitField, bitStart: 4, bitWidth: 3);
            Eq("bit field decode", "7", Formats.Format(new[] { 0x00F5 }, 0, bits));
            Eq("bit field encode", new[] { 0x0050 }, Formats.Parse("5", bits));
            Eq("bit field mask", 0x0070, Formats.BitMask(bits));
            Throws<FormatException>("bit field value range", () => Formats.Parse("8", bits));
            Throws<FormatException>("bit field inside register", () => new DisplayFormat(DataType.BitField, bitStart: 14, bitWidth: 3));
            // keys round trip; presets keep Modbus Poll / Python keys
            var all = new List<DisplayFormat>(DisplayFormat.Presets) { s3, bits, new DisplayFormat(DataType.String, ByteOrder.DCBA, 10) };
            foreach (DataType t in Enum.GetValues(typeof(DataType)))
                if (t != DataType.String && t != DataType.BitField)
                    foreach (ByteOrder o in Enum.GetValues(typeof(ByteOrder))) all.Add(F(t, o));
            Check("format keys round trip (" + all.Count + ")", all.All(f => DisplayFormat.FromKey(f.Key) == f),
                  string.Join(" ", all.Where(f => DisplayFormat.FromKey(f.Key) != f).Select(f => f.Key)));
            Eq("preset key kept", "float_inverse", DisplayFormat.FloatInverse.Key);
            Eq("custom key", "f32_badc", F(DataType.Float32, ByteOrder.BADC).Key);
            Eq("ABCD key has no suffix", "str4", new DisplayFormat(DataType.String, ByteOrder.ABCD, 4).Key);
            Check("explicit _abcd accepted", DisplayFormat.FromKey("u32_abcd") == F(DataType.UInt32, ByteOrder.ABCD));
            foreach (string badKey in new[] { "f33", "str0", "str65", "bits15.2", "f32_xyz", "" })
                Throws<FormatException>("unknown key '" + badKey + "'", () => DisplayFormat.FromKey(badKey));
            // interpret-as table
            var rows = Formats.Interpretations(new[] { 0xC03F, 0x0000 });
            Eq("interpret rows for 2 registers", 30, rows.Count);
            Check("interpret finds float BADC", rows.Any(r => r.Key.Spec == "Float32 BADC" && r.Value == "1.5"));
            Eq("interpret rows for 4 registers", 42, Formats.Interpretations(new[] { 1, 2, 3, 4 }).Count);
            // renderer and logger with wide/custom cells
            var view = new ViewOptions();
            view.CellFormats[0] = s3;
            view.CellFormats[3] = F(DataType.Float32, ByteOrder.BADC);
            var cells = Render.Values(new PollDefinition { Quantity = 6 }, new[] { 0x4865, 0x6C6C, 0x6F00, 0xC03F, 0, 7 }, view);
            Eq("render string + partners", new[] { "Hello", "", "", "1.5", "", "7" }, cells.Select(c => c.Value).ToArray());
        }

        static void Wave1Offline()
        {
            // --- alarms with deadband (OPC UA LimitAlarmType semantics) ---
            var t = new Tag { High = 20, HighHigh = 30, Low = 5, LowLow = 0, Deadband = 1 };
            Eq("alarm none", AlarmLevel.None, AlarmEvaluator.Evaluate(t, 10, AlarmLevel.None));
            Eq("alarm H enter at limit", AlarmLevel.High, AlarmEvaluator.Evaluate(t, 20, AlarmLevel.None));
            Eq("alarm H held inside deadband", AlarmLevel.High, AlarmEvaluator.Evaluate(t, 19.5, AlarmLevel.High));
            Eq("alarm H clears below limit-deadband", AlarmLevel.None, AlarmEvaluator.Evaluate(t, 18.9, AlarmLevel.High));
            Eq("alarm HH", AlarmLevel.HighHigh, AlarmEvaluator.Evaluate(t, 31, AlarmLevel.High));
            Eq("alarm HH falls to H", AlarmLevel.High, AlarmEvaluator.Evaluate(t, 25, AlarmLevel.HighHigh));
            Eq("alarm HH held inside deadband", AlarmLevel.HighHigh, AlarmEvaluator.Evaluate(t, 29.5, AlarmLevel.HighHigh));
            Eq("alarm L", AlarmLevel.Low, AlarmEvaluator.Evaluate(t, 5, AlarmLevel.None));
            Eq("alarm L held", AlarmLevel.Low, AlarmEvaluator.Evaluate(t, 5.5, AlarmLevel.Low));
            Eq("alarm LL", AlarmLevel.LowLow, AlarmEvaluator.Evaluate(t, -1, AlarmLevel.Low));
            Throws<FormatException>("alarm limit order validated", () => new Tag { High = 10, Low = 20 }.Validate());
            Throws<FormatException>("zero scale rejected", () => new Tag { Scale = 0 }.Validate());

            // --- scaling (soil sensor: temperature x0.1, pH x0.01) ---
            var temp = new Tag { Name = "Suhu", Scale = 0.1, Unit = "°C", Decimals = 1 };
            Eq("scale 260 -> 26.0 °C", "26.0 °C", temp.Format(temp.ToEngineering(260)));
            Eq("scale back 26.0 -> 260", 260.0, Math.Round(temp.ToRaw(26.0), 6));
            var ph = new Tag { Scale = 0.01, Decimals = 2 };
            Eq("pH 700 -> 7.00", "7.00", ph.Format(ph.ToEngineering(700)));

            // --- tag map CSV round trip, including formula-injection guard and quotes ---
            string dir = Path.Combine(Path.GetTempPath(), "multipoll-selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string csv = Path.Combine(dir, "tags.csv");
                var tags = new Dictionary<int, Tag>
                {
                    { 0, temp }, { 7, new Tag { Name = "=cmd|x, \"pH\"", Scale = 0.01, High = 8.5, Low = 5.5, Deadband = 0.1, WriteMin = 0, WriteMax = 14 } },
                };
                TagMapCsv.Save(csv, tags);
                var back = TagMapCsv.Load(csv, 8);
                Check("tag CSV round trip", back.Count == 2 && back[0].Unit == "°C" && back[0].Scale == 0.1 && back[7].High == 8.5
                                            && back[7].WriteMax == 14 && back[7].Name == "=cmd|x, \"pH\"");
                Check("tag CSV formula guard in file", File.ReadAllText(csv).Contains("'=cmd"));
                File.WriteAllText(csv, "offset,name,scale\n9,x,1\n");
                Throws<FormatException>("tag CSV offset outside window", () => TagMapCsv.Load(csv, 8), e => e.Message.Contains("line 2"));
                File.WriteAllText(csv, "offset,name,h,l\n0,x,1,5\n");
                Throws<FormatException>("tag CSV bad limits", () => TagMapCsv.Load(csv, 8));
                File.WriteAllText(csv, "name,offset\n");
                Throws<FormatException>("tag CSV header checked", () => TagMapCsv.Load(csv, 8));

                // --- document round trip keeps tags ---
                var doc = new PollDocument();
                doc.Definition.Quantity = 8;
                doc.Tags[0] = temp;
                doc.Tags[7] = tags[7];
                string docPath = Path.Combine(dir, "d.mpoll");
                Persistence.SaveDocument(docPath, doc);
                var docBack = Persistence.LoadDocument(docPath);
                Check("document keeps tags", docBack.Tags.Count == 2 && docBack.Tags[0].Unit == "°C" && docBack.Tags[7].Low == 5.5
                                             && !docBack.Tags[0].High.HasValue);

                // --- logger: ISO-8601, header, status/RTT, scaling, error rows, deadband + heartbeat ---
                var def = new PollDefinition { Function = 3, Address = 0, Quantity = 2 };
                var view = new ViewOptions { PlcAddresses = true };
                Func<int, Tag> tagAt = o => o == 0 ? temp : null;
                string logPath = Path.Combine(dir, "log.csv");
                DateTime t0 = new DateTime(2026, 10, 29, 9, 15, 2, 123, DateTimeKind.Utc);
                using (var lg = new DataLogger(new LogSettings { Path = logPath, Time = LogTime.Utc, ChangedOnly = true, Deadband = 2, HeartbeatSeconds = 60 }, view, tagAt))
                {
                    Func<double, int[], Status, PollResult> res = (sec, v, s) => new PollResult { Definition = def, Values = v, Status = s, Timestamp = t0.AddSeconds(sec), RttMs = s == Status.Ok ? 12.34 : -1 };
                    Check("log first", lg.Log(res(0, new[] { 260, 65531 }, Status.Ok)));
                    Check("log skip within deadband", !lg.Log(res(1, new[] { 261, 65532 }, Status.Ok)));
                    Check("log change beyond deadband", lg.Log(res(2, new[] { 264, 65532 }, Status.Ok)));
                    Check("log status change", lg.Log(res(3, null, Status.Timeout)));
                    Check("log recovery", lg.Log(res(4, new[] { 264, 65532 }, Status.Ok)));
                    Check("log heartbeat", lg.Log(res(70, new[] { 264, 65532 }, Status.Ok)));
                }
                string[] lines = File.ReadAllLines(logPath);
                Eq("log header", "timestamp,status,rtt_ms,Suhu [°C],40002", lines[0]);
                Eq("log first row (negative number not formula-escaped)", "2026-10-29T09:15:02.123Z,OK,12.3,26.0,-5", lines[1]);
                Eq("log error row keeps columns", "2026-10-29T09:15:05.123Z,Timeout Error,,,", lines[3]);
                Eq("log line count (header + 5 logged rows)", 6, lines.Length);
                string local = DataLogger.Stamp(t0, LogTime.LocalWithOffset);
                Check("local stamp has offset", System.Text.RegularExpressions.Regex.IsMatch(local, @"^2026-10-29T\d\d:\d\d:02\.123[+-]\d\d:\d\d$"), local);

                // --- size rotation with retention ---
                string rot = Path.Combine(dir, "rot.csv");
                using (var lg = new DataLogger(new LogSettings { Path = rot, Rotation = LogRotation.Size, RotateSizeMb = 1, KeepFiles = 2 }, view))
                {
                    var big = new PollDefinition { Function = 3, Quantity = 100 };
                    int[] vals = Enumerable.Range(0, 100).Select(i => 30000 + i).ToArray();
                    for (int i = 0; i < 6000; i++)
                        lg.Log(new PollResult { Definition = big, Values = vals, Status = Status.Ok, Timestamp = t0.AddMilliseconds(i * 1000) });
                }
                var rotated = Directory.GetFiles(dir, "rot_*.csv");
                Check("size rotation keeps 2 archives", rotated.Length == 2, rotated.Length + " archives");
                Check("rotated file starts with header", File.ReadLines(rot).First().StartsWith("timestamp,"));
                Check("archive under limit + one line", rotated.All(f => new FileInfo(f).Length < 1024 * 1024 + 2000));

                // --- daily rotation names the file by date ---
                string daily = Path.Combine(dir, "day.csv");
                using (var lg = new DataLogger(new LogSettings { Path = daily, Rotation = LogRotation.Daily, Time = LogTime.Utc }, view))
                {
                    lg.Log(new PollResult { Definition = def, Values = new[] { 1, 2 }, Status = Status.Ok, Timestamp = DateTime.UtcNow });
                    Check("daily file name", Path.GetFileName(lg.CurrentPath) == "day_" + DateTime.UtcNow.ToString("yyyy-MM-dd") + ".csv", lg.CurrentPath);
                }

                // --- audit hash chain ---
                string audit = Path.Combine(dir, "audit.jsonl");
                var a = new AuditLog(audit);
                a.Record("test", 1, 6, 10, new[] { 5 }, new[] { 4 }, "OK");
                a.Record("test", 1, 16, 20, new[] { 1, 2 }, null, "BLOCKED: write protection on");
                new AuditLog(audit).Record("test", 2, 5, 0, new[] { 1 }, new[] { 0 }, "Timeout Error");
                Check("audit chain intact", AuditLog.Verify(audit) == null, AuditLog.Verify(audit));
                string[] al = File.ReadAllLines(audit);
                Check("audit has user and before", al[0].Contains("\"user\"") && al[0].Contains("\"before\":[4]"));
                al[1] = al[1].Replace("BLOCKED", "OK");
                File.WriteAllLines(audit, al);
                Check("audit edit detected", (AuditLog.Verify(audit) ?? "").StartsWith("line 3"), AuditLog.Verify(audit));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }

            // --- write decoding for confirmation/audit ---
            int addr; int[] vals2; string what;
            WriteInfo.Decode(Pdu.WriteMultipleRegisters(48, new[] { 2, 3 }), out addr, out vals2, out what);
            Check("decode FC16", addr == 48 && vals2.SequenceEqual(new[] { 2, 3 }));
            WriteInfo.Decode(Pdu.WriteSingleCoil(7, true), out addr, out vals2, out what);
            Check("decode FC05", addr == 7 && vals2[0] == 1);
            WriteInfo.Decode(Pdu.ReadWriteRegisters(0, 2, 50, new[] { 9 }), out addr, out vals2, out what);
            Check("decode FC23 write part", addr == 50 && vals2.SequenceEqual(new[] { 9 }));
            Eq("raw fc MBAP", 6, PollEngine.RawFunction(Hex.ParseSeparated("00 01 00 00 00 06 01 06 00 30 00 02"), ConnectionKind.Tcp, SerialMode.Rtu));
            Eq("raw fc RTU", 6, PollEngine.RawFunction(Hex.ParseSeparated("01 06 00 30 00 02 08 04"), ConnectionKind.Serial, SerialMode.Rtu));
            Eq("raw fc ASCII", 3, PollEngine.RawFunction(System.Text.Encoding.ASCII.GetBytes(":010300000001FB\r\n"), ConnectionKind.Serial, SerialMode.Ascii));
            Func<string, ConnectionKind, bool> ro = (hex, k) => PollEngine.IsReadOnlyRaw(Hex.ParseSeparated(hex), k, SerialMode.Rtu);
            Check("raw read-only: FC03 RTU", ro("01 03 00 00 00 08 44 0C", ConnectionKind.Serial));
            Check("raw read-only: FC03 MBAP", ro("00 01 00 00 00 06 01 03 00 00 00 01", ConnectionKind.Tcp));
            Check("raw read-only: FC08/00 echo", ro("01 08 00 00 12 34 ED 7C", ConnectionKind.Serial));
            Check("raw read-only: FC43/14", ro("01 2B 0E 01 00 70 77", ConnectionKind.Serial));
            Check("raw NOT read-only: FC06", !ro("01 06 00 30 00 02 08 04", ConnectionKind.Serial));
            Check("raw NOT read-only: FC08/04 listen-only", !ro("01 08 00 04 00 00 A1 CA", ConnectionKind.Serial));
            Check("raw NOT read-only: FC08/01 restart", !ro("01 08 00 01 00 00 B1 CB", ConnectionKind.Serial));
            Check("raw NOT read-only: serial broadcast read", !ro("00 03 00 00 00 01 85 DB", ConnectionKind.Serial));
            Check("raw NOT read-only: FC43/13 (not 14)", !ro("01 2B 0D 00 00 00 00", ConnectionKind.Serial));
            Check("raw NOT read-only: garbage", !ro("01", ConnectionKind.Serial));
            Check("raw read-only: ASCII FC03", PollEngine.IsReadOnlyRaw(System.Text.Encoding.ASCII.GetBytes(":010300000001FB\r\n"), ConnectionKind.Serial, SerialMode.Ascii));

            // --- serial timing (Modbus over Serial Line V1.02 §2.5.1.1) ---
            Check("t3.5 at 9600 = 4.01 ms", Math.Abs(SerialTiming.T35Ms(9600) - 4.0104) < 0.001);
            Eq("t3.5 above 19200 fixed", 1.75, SerialTiming.T35Ms(38400));
            Eq("FC03 x125 response bytes", 255, SerialTiming.ResponseBytes(3, 125, SerialMode.Rtu));
            Check("1200 baud timeout > 2.3 s", SerialTiming.SuggestTimeoutMs(1200, 3, 125, SerialMode.Rtu) > 2340);
            Check("9600 baud x8 timeout small", SerialTiming.SuggestTimeoutMs(9600, 3, 8, SerialMode.Rtu) <= 600);

            // --- KPIs ---
            var st = new PollStats();
            st.Record(Status.Ok, 10);
            st.Record(Status.Ok, 30);
            st.Record(Status.Timeout);
            st.Record(Status.Ok, 20);
            Check("kpi avg/max/last", st.AvgRttMs == 20 && st.MaxRttMs == 30 && st.LastRttMs == 20 && st.ConsecutiveErrors == 0);
            Eq("kpi success %", 75.0, st.SuccessPercent);
            for (int i = 0; i < 200; i++) st.Record(Status.Ok, 1);
            Check("kpi rolling window", st.RecentCount == PollStats.Window && st.SuccessPercent == 100);
            PollStats copy = st.Copy();
            st.Record(Status.Timeout);
            Check("kpi copy independent", copy.Errors == 1 && st.Errors == 2 && copy.SuccessPercent == 100);
        }

        static void Live(string host, int port)
        {
            var settings = new ConnectionSettings { Kind = ConnectionKind.Tcp, Host = host, TcpPort = port, ResponseTimeoutMs = 1500 };
            var traffic = new List<string>();
            using (var m = new ModbusMaster(settings) { OnTraffic = (dir, bytes) => traffic.Add(dir) })
            {
                Expect("tcp connect " + host + ":" + port, m.Connect);
                if (!m.Connected) return;
                Eq("03 read seed registers", new[] { 100, 65535, 0x3FC0, 0 }, m.Transact(1, Pdu.ReadRequest(3, 0, 4)));
                Eq("traffic Tx/Rx", "Tx,Rx", string.Join(",", traffic.Take(2)));
                Expect("06 write", () => m.Transact(1, Pdu.WriteSingleRegister(10, 0xBEEF)));
                Eq("06 read back", new[] { 0xBEEF }, m.Transact(1, Pdu.ReadRequest(3, 10, 1)));
                Expect("16 write", () => m.Transact(1, Pdu.WriteMultipleRegisters(20, new[] { 7, 8, 9 })));
                Eq("16 read back", new[] { 7, 8, 9 }, m.Transact(1, Pdu.ReadRequest(3, 20, 3)));
                Expect("05 write", () => m.Transact(1, Pdu.WriteSingleCoil(5, true)));
                Expect("15 write", () => m.Transact(1, Pdu.WriteMultipleCoils(8, new[] { 1, 0, 1, 1, 0, 0, 0, 0, 1 })));
                Eq("01 read back", new[] { 1, 0, 0, 1, 0, 1, 1, 0, 0, 0, 0, 1 }, m.Transact(1, Pdu.ReadRequest(1, 5, 12)));
                Expect("06 seed for mask", () => m.Transact(1, Pdu.WriteSingleRegister(30, 0x00F2)));
                Expect("22 mask write", () => m.Transact(1, Pdu.MaskWriteRegister(30, 0x00F0, 0x0025)));
                Eq("22 result", new[] { 0x00F5 }, m.Transact(1, Pdu.ReadRequest(3, 30, 1)));
                Eq("23 read/write", new[] { 7, 8 }, m.Transact(1, Pdu.ReadWriteRegisters(20, 2, 50, new[] { 99 })));
                Eq("23 written", new[] { 99 }, m.Transact(1, Pdu.ReadRequest(3, 50, 1)));
                Check("02 read", m.Transact(1, Pdu.ReadRequest(2, 0, 3)).Length == 3);
                Check("04 read", m.Transact(1, Pdu.ReadRequest(4, 0, 2)).Length == 2);
                Expect("float via 16", () => m.Transact(1, Pdu.WriteMultipleRegisters(60, Formats.Encode(-1234.5, DisplayFormat.Float))));
                Eq("float read back", "-1234.5", Formats.Format(m.Transact(1, Pdu.ReadRequest(3, 60, 2)), 0, DisplayFormat.Float));
                Throws<ModbusException>("illegal address exception", () => m.Transact(1, Pdu.ReadRequest(3, 195, 10)),
                                        e => e.Status == Status.IllegalDataAddress);
                byte[] raw = m.RawExchange(Hex.ParseSeparated("00 07 00 00 00 06 01 03 00 00 00 01"));
                Eq("test center raw exchange", Hex.Parse("00070000000501030200" + "64"), raw);
            }

            var variants = new[]
            {
                Tuple.Create("rtu over tcp", new ConnectionSettings { Kind = ConnectionKind.SerialOverTcp, Mode = SerialMode.Rtu, Host = host, TcpPort = port + 1 }),
                Tuple.Create("udp", new ConnectionSettings { Kind = ConnectionKind.Udp, Host = host, TcpPort = port + 2 }),
                Tuple.Create("ascii over tcp", new ConnectionSettings { Kind = ConnectionKind.SerialOverTcp, Mode = SerialMode.Ascii, Host = host, TcpPort = port + 3 }),
            };
            foreach (var v in variants)
            {
                using (var m = new ModbusMaster(v.Item2))
                {
                    try
                    {
                        m.Connect();
                        Eq(v.Item1 + " read", new[] { 100, 65535 }, m.Transact(1, Pdu.ReadRequest(3, 0, 2)));
                        Expect(v.Item1 + " write 16", () => m.Transact(1, Pdu.WriteMultipleRegisters(110, new[] { 1, 2 })));
                        Eq(v.Item1 + " coils", 3, m.Transact(1, Pdu.ReadRequest(2, 0, 3)).Length);
                    }
                    catch (ModbusException ex)
                    {
                        Check(v.Item1, false, ex.Describe());
                    }
                }
            }

            // Engine: two windows polling concurrently plus a queued write.
            var engine = new PollEngine();
            var got = new Dictionary<int, List<PollResult>>();
            var gate = new object();
            Action<PollResult> collect = r => { lock (gate) { if (!got.ContainsKey(r.PollId)) got[r.PollId] = new List<PollResult>(); got[r.PollId].Add(r); } };
            int a = engine.AddPoll(new PollDefinition { Function = 3, Address = 0, Quantity = 2, ScanRateMs = 50 }, collect);
            int b = engine.AddPoll(new PollDefinition { Function = 1, Address = 5, Quantity = 3, ScanRateMs = 50 }, collect);
            int c = engine.AddPoll(new PollDefinition { Function = 3, ScanRateMs = 20, Enabled = false }, collect);
            settings.DelayBetweenPollsMs = 0;
            try
            {
                engine.Start(settings);
                var until = DateTime.Now.AddSeconds(5);
                while (DateTime.Now < until)
                {
                    lock (gate) if (got.ContainsKey(a) && got.ContainsKey(b) && got[a].Count >= 3 && got[b].Count >= 3) break;
                    System.Threading.Thread.Sleep(20);
                }
                lock (gate)
                {
                    Check("engine polls window A", got.ContainsKey(a) && got[a].Count >= 3 && got[a].Last().Status == Status.Ok);
                    Check("engine polls window B", got.ContainsKey(b) && got[b].Count >= 3 && got[b].Last().Values.SequenceEqual(new[] { 1, 0, 0 }));
                    Check("disabled window not polled", !got.ContainsKey(c));
                }
                engine.ReadOnce(c);
                System.Threading.Thread.Sleep(300);
                lock (gate) Check("read once on disabled window", got.ContainsKey(c) && got[c].Count == 1);
                Expect("engine queued write", () => engine.Write(1, Pdu.WriteSingleRegister(1, 77)).Wait(3000));
                Eq("engine write read back", new[] { 100, 77 }, (int[])engine.Submit(m => m.Transact(1, Pdu.ReadRequest(3, 0, 2))).Result);
                Expect("restore register 1", () => engine.Write(1, Pdu.WriteSingleRegister(1, 65535)).Wait(3000));
                Check("engine stats", engine.Stats(a).Tx >= 3 && engine.Stats(a).Errors == 0);
                Check("engine RTT measured", engine.Stats(a).LastRttMs > 0 && engine.Stats(a).SuccessPercent == 100);

                // write protection: blocked locally, nothing changes on the device, audit records it
                string auditPath = Path.Combine(Path.GetTempPath(), "multipoll-selftest-audit-" + Guid.NewGuid().ToString("N") + ".jsonl");
                engine.Audit = new AuditLog(auditPath);
                engine.WriteProtected = true;
                bool blocked = false;
                try { engine.Write(1, Pdu.WriteSingleRegister(1, 4242)).Wait(3000); }
                catch (AggregateException ex) { blocked = ex.InnerException is WriteBlockedException; }
                Check("write protection blocks", blocked);
                Eq("protected register unchanged", new[] { 100, 65535 }, (int[])engine.Submit(m => m.Transact(1, Pdu.ReadRequest(3, 0, 2))).Result);
                blocked = false;
                try { engine.Raw(Hex.ParseSeparated("00 09 00 00 00 06 01 06 00 01 10 92")).Wait(3000); }
                catch (AggregateException ex) { blocked = ex.InnerException is WriteBlockedException; }
                Check("write protection blocks raw FC06", blocked);
                Check("raw read still allowed", ((byte[])engine.Raw(Hex.ParseSeparated("00 0A 00 00 00 06 01 03 00 00 00 01")).Result).Length == 11);
                engine.WriteProtected = false;
                Expect("audited write", () => engine.Write(1, Pdu.WriteSingleRegister(40, 1234)).Wait(3000));
                string[] auditLines = File.ReadAllLines(auditPath);
                Check("audit lines", auditLines.Length == 3 && auditLines[0].Contains("BLOCKED") && auditLines[2].Contains("\"result\":\"OK\""),
                      string.Join(" | ", auditLines));
                Check("audit before value", auditLines[2].Contains("\"before\":["));
                Check("audit chain", AuditLog.Verify(auditPath) == null);
                File.Delete(auditPath);
                engine.Audit = null;

                // read-only unit scan: slave 1 answers on the demo slave, ID 2 does not exist
                var hits = (List<ScanHit>)engine.Submit(m => Discovery.ScanUnits(m, 1, 3, 3, 0, 0, null, System.Threading.CancellationToken.None)).Result;
                Check("scan finds slave 1", hits.Any(h => h.SlaveId == 1 && h.Reply == "data"), string.Join(",", hits.Select(h => h.SlaveId + ":" + h.Reply)));
                Exception idErr = null;
                try { engine.Submit(m => Discovery.ReadDeviceId(m, 1, true)).Wait(3000); }
                catch (AggregateException ex) { idErr = ex.InnerException; }
                Check("FC43/14 answered or exception", idErr == null || idErr is ModbusException, idErr == null ? "" : idErr.Message);
            }
            catch (Exception ex)
            {
                Check("engine", false, ex.Message);
            }
            finally
            {
                engine.Stop();
            }
        }
    }
}
