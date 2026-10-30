using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using MultiPollStudio.UI;

namespace MultiPollStudio.Tests
{
    /// <summary>In-process GUI smoke test: drives the real MainForm/PollWindow/dialogs in a Windows message loop against
    /// tools/demo_slave.py. Build: ./build.sh uitest. Run: MultiPollStudioUiTest.exe host port outdir. Exit code = failures.
    /// Uses an owned per-run HKCU key and audit files under outdir; screenshots are app-rendered DrawToBitmap images,
    /// not desktop captures or interaction recordings.</summary>
    static partial class UiSmoke
    {
        static int _failures;
        static StreamWriter _log;
        static string _outDir;

        static void Check(string name, bool ok, string detail = "")
        {
            _log.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok || detail.Length == 0 ? "" : "  -- " + detail));
            if (!ok) _failures++;
        }

        static async Task<bool> Until(Func<bool> cond, int ms = 5000)
        {
            DateTime end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end)
            {
                if (cond()) return true;
                await Task.Delay(30);
            }
            return cond();
        }

        static T Field<T>(object o, string name)
        {
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f != null) return (T)f.GetValue(o);
            }
            throw new MissingFieldException(o.GetType().Name, name);
        }

        static void Shot(Form f, string file)
        {
            f.Activate();
            Application.DoEvents();
            using (var bmp = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(Path.Combine(_outDir, file), ImageFormat.Png);
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            bool devices = args.Length > 0 && args[0] == "devices";
            bool toolbar = args.Length > 0 && args[0] == "toolbar";
            if (devices || toolbar) args = args.Skip(1).ToArray();
            string host = args.Length > 0 ? args[0] : "127.0.0.1";
            int port = args.Length > 1 ? int.Parse(args[1]) : 5020;
            _outDir = args.Length > 2 ? args[2] : Path.GetTempPath();
            _log = new StreamWriter(Path.Combine(_outDir, "uitest.txt")) { AutoFlush = true };
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // isolate from the user's settings and audit trail
            string settingsKey = @"Software\MultiPollStudio-UiTest-" + Guid.NewGuid().ToString("N");
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(settingsKey))
                key.SetValue("UiSmokeRun", settingsKey);
            MainForm.SettingsKey = settingsKey;
            MainForm.AuditPath = Path.Combine(_outDir, "uitest-main-audit.jsonl");
            var main = new MainForm { TopMost = true };
            // watchdog: a modal box nobody answers must fail the run, not hang it
            var watchdog = new System.Threading.Timer(_ =>
            {
                _log.WriteLine("FAIL watchdog: run did not finish within 8 minutes (a modal dialog probably blocked it)");
                _log.WriteLine("UITEST FAILED: " + (_failures + 1));
                _log.Flush();
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(settingsKey, false);
                Environment.Exit(99);
            }, null, 8 * 60 * 1000, System.Threading.Timeout.Infinite);
            main.Shown += async (o, e) =>
            {
                try
                {
                    if (toolbar) await Toolbar(main, host, port);
                    else if (devices) await Devices(main, host, port);
                    else if (host.StartsWith("COM", StringComparison.OrdinalIgnoreCase)) await RunSerial(main, host, port);
                    else await Run(main, host, port);
                }
                catch (Exception ex) { Check("unexpected exception", false, ex.ToString()); }
                finally
                {
                    foreach (PollWindow w in main.Windows) w.Dirty = false;
                    _log.WriteLine(_failures == 0 ? "UITEST OK" : "UITEST FAILED: " + _failures);
                    main.Close();
                }
            };
            try { Application.Run(main); }
            finally
            {
                watchdog.Dispose();
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(settingsKey, false);
                _log.Dispose();
            }
            return _failures;
        }

        static string Cell(PollWindow w, int offset) { return w.ValueText(offset); }

        /// <summary>Read-only GUI scenario for a real RTU slave: FC03 slave 1, registers 0..7, 8N1 at the given baud.
        /// Never writes to the device. Usage: MultiPollStudioUiTest.exe COM26 9600 outdir</summary>
        static async Task RunSerial(MainForm main, string port, int baud)
        {
            await Until(() => main.Windows.Any(), 3000);
            main.ConnSettings = new ConnectionSettings { Kind = ConnectionKind.Serial, Mode = SerialMode.Rtu, Port = port, BaudRate = baud,
                                                         ResponseTimeoutMs = 1000, DelayBetweenPollsMs = 50 };
            PollWindow w = main.Windows.First();
            var view = new ViewOptions { PlcAddresses = true, Rows = 10 };
            view.CellFormats[0] = DisplayFormat.Signed;
            w.ApplyDefinition(new PollDefinition { SlaveId = 1, Function = 3, Address = 0, Quantity = 8, ScanRateMs = 1000 }, view);
            string[] names = { "Suhu x0.1 C", "Kelembapan x0.1 %", "EC uS/cm", "Salinitas", "N mg/kg", "P mg/kg", "K mg/kg", "pH x0.01" };
            for (int i = 0; i < names.Length; i++) w.Doc.Aliases[i] = names[i];
            w.Doc.Tags[0] = new Tag { Name = "Suhu", Scale = 0.1, Unit = "°C", Decimals = 1, High = 45, Low = 0, Deadband = 0.5 };
            w.Doc.Tags[7] = new Tag { Name = "pH", Scale = 0.01, Decimals = 2, High = 8.5, Low = 5.5, Deadband = 0.1 };
            w.Rebuild();
            main.Engine.WriteProtected = true; // hardware run: refuse any write locally, whatever happens
            string logPath = Path.Combine(_outDir, "serial-log.csv");
            if (File.Exists(logPath)) File.Delete(logPath);
            main.QuickConnect();
            w.StartLog(new LogSettings { Path = logPath });
            main.AddToTrend(w, new List<int> { 0, 7 });
            Check("open " + port, main.Engine.Running);
            if (!main.Engine.Running) return;
            main.ShowTraffic();
            Check("sensor values arrive", await Until(() => w.Values != null, 6000), w.ErrorText);
            Check("three polls without error", await Until(() => main.Engine.Stats(w.PollId).Tx >= 3, 6000)
                                                && main.Engine.Stats(w.PollId).Errors == 0, w.StatusText);
            _log.WriteLine("      values: " + (w.Values == null ? "none" : string.Join(", ", w.Values)) + " | " + w.StatusText);
            Check("scaled temperature cell", Cell(w, 0).EndsWith(" °C"), Cell(w, 0));
            Check("scaled pH cell", Cell(w, 7).Contains("."), Cell(w, 7));
            _log.WriteLine("      scaled: Suhu=" + Cell(w, 0) + "  pH=" + Cell(w, 7) + "  alarms=" + w.ActiveAlarmCount);
            w.StopLog();
            string[] logLines = File.ReadAllLines(logPath);
            Check("serial log has header + rows", logLines.Length >= 3 && logLines[0].StartsWith("timestamp,status,rtt_ms,Suhu [°C]"), logLines.FirstOrDefault());
            _log.WriteLine("      log: " + logLines[0] + " / " + (logLines.Length > 1 ? logLines[1] : ""));
            Check("trend received sensor points", main.TrendWin.PointCount(0) >= 3);
            Shot(main.TrendWin, "serial_trend.png");
            if (main.TrafficWin.Count > 1) _log.WriteLine("      " + main.TrafficWin.Line(0) + " / " + main.TrafficWin.Line(1));
            w.WindowState = FormWindowState.Normal;
            w.SetBounds(0, 0, 520, 300);
            main.TrafficWin.SetBounds(main.Left + 540, main.Top + 80, 620, 260);
            await Task.Delay(500);
            Shot(main, "serial_main.png");
            Shot(main.TrafficWin, "serial_traffic.png");
            main.Disconnect();
            Check("port released", !main.Engine.Running);

            // Find Device on the real line: read-only FC03 probes; the sensor is known at 9600 8N1 ID 1
            using (FindDeviceDialog fd = main.CreateFindDevice(main.ConnSettings))
            {
                fd.Show(main);
                fd.Configure(port, new[] { 4800, 9600 }, true, true, false, 1, 3, false, 0);
                fd.StartOrStop();
                await fd.Running;
                Check("serial find device finds the sensor at 9600 N1 ID 1", fd.Hits.Count == 1 && fd.Hits[0].Baud == 9600
                      && fd.Hits[0].Parity == System.IO.Ports.Parity.None && fd.Hits[0].SlaveId == 1, fd.ProgressText + " " + string.Join(" | ", fd.Hits.Select(h => h.Line)));
                _log.WriteLine("      find: " + fd.ProgressText + " " + string.Join(" | ", fd.Hits.Select(h => h.Line)));
                Shot(fd, "serial_find.png");
                fd.Close();
            }
        }

        static async Task Run(MainForm main, string host, int port)
        {
            await Until(() => main.Windows.Any(), 3000);
            Confirm.Enabled = false; // unattended run: a modal confirmation would block; the confirm text is checked in Wave1
            main.Engine.WriteProtected = false;
            main.ConnSettings = new ConnectionSettings { Kind = ConnectionKind.Tcp, Host = host, TcpPort = port, DelayBetweenPollsMs = 10 };
            main.QuickConnect();
            Check("connect", main.Engine.Running);
            if (!main.Engine.Running) return;

            PollWindow w1 = main.Windows.First();
            w1.ApplyDefinition(new PollDefinition { Function = 3, Address = 0, Quantity = 20, ScanRateMs = 200 },
                               new ViewOptions { PlcAddresses = true, Rows = 10 });
            Check("w1 values arrive", await Until(() => w1.Values != null));
            using (var connection = new ConnectionDialog(main.ConnSettings))
            {
                connection.Show(main);
                Shot(connection, "connection_setup.png");
                connection.Close();
            }
            using (var definition = new DefinitionDialog(w1.Doc.Definition, w1.Doc.View, w1.ApplyDefinition,
                                                         () => w1.Engine.ReadOnce(w1.PollId)))
            {
                definition.Show(main);
                Shot(definition, "poll_setup.png");
                definition.Close();
            }
            Check("w1 header 40001", w1.Grid.Columns[1].HeaderText == "40001", w1.Grid.Columns[1].HeaderText);
            Check("w1 signed values", Cell(w1, 0) == "100" && Cell(w1, 1) == "-1", Cell(w1, 0) + "," + Cell(w1, 1));

            // per-cell float format on offset 2 (registers 2..3 = 1.5)
            w1.Grid.ClearSelection();
            Point p2 = w1.CellOf(2);
            w1.Grid[p2.X, p2.Y].Selected = true;
            w1.SetDisplay(DisplayFormat.Float);
            Check("per-cell float 1.5", await Until(() => Cell(w1, 2) == "1.5"), Cell(w1, 2));
            Check("float partner cell blank", Cell(w1, 3) == "", Cell(w1, 3));

            // edit a value cell through the real DataGridView edit path -> FC06 -> read back
            Point p5 = w1.CellOf(5);
            w1.Activate();
            w1.Grid.CurrentCell = w1.Grid[p5.X, p5.Y];
            w1.Grid.BeginEdit(true);
            w1.Grid.EditingControl.Text = "4321";
            w1.Grid.EndEdit();
            Check("grid edit writes FC06", await Until(() => Cell(w1, 5) == "4321"), Cell(w1, 5));

            // alias edit
            Point a5 = w1.CellOf(5, alias: true);
            w1.Grid.CurrentCell = w1.Grid[a5.X, a5.Y];
            w1.Grid.BeginEdit(true);
            w1.Grid.EditingControl.Text = "Setpoint";
            w1.Grid.EndEdit();
            Check("alias stored", w1.Doc.Aliases.ContainsKey(5) && w1.Doc.Aliases[5] == "Setpoint" && w1.Dirty);

            // more windows: input registers (changing), coils, illegal address
            PollWindow w2 = main.NewWindow();
            w2.ApplyDefinition(new PollDefinition { Function = 4, Quantity = 2, ScanRateMs = 100 },
                               new ViewOptions { Display = DisplayFormat.Unsigned, PlcAddresses = true });
            Check("w2 values", await Until(() => w2.Values != null));
            int[] first = w2.Values;
            Check("w2 input registers change", await Until(() => w2.Values != null && !w2.Values.SequenceEqual(first), 3000));
            Check("w2 header 30001", w2.Grid.Columns[1].HeaderText == "30001", w2.Grid.Columns[1].HeaderText);

            PollWindow w3 = main.NewWindow();
            w3.ApplyDefinition(new PollDefinition { Function = 1, Quantity = 8, ScanRateMs = 150 }, new ViewOptions());
            Check("w3 coils", await Until(() => w3.Values != null && w3.Values.Length == 8));

            PollWindow w4 = main.NewWindow();
            w4.ApplyDefinition(new PollDefinition { Function = 3, Address = 195, Quantity = 10, ScanRateMs = 300 }, new ViewOptions());
            Check("w4 illegal address shown", await Until(() => w4.ErrorText.StartsWith("Illegal Data Address")), w4.ErrorText);
            Check("w4 error counter", main.Engine.Stats(w4.PollId).Errors > 0);

            // traffic window
            main.ShowTraffic();
            Check("traffic lines", await Until(() => main.TrafficWin.Count > 4));

            main.LayoutMdi(MdiLayout.TileVertical);
            await Task.Delay(800);
            Shot(main.TrafficWin, "traffic.png");
            main.TrafficWin.Hide();
            await Task.Delay(300);
            Shot(main, "main.png");

            // write dialogs, shown non-modally and sent with their real Send button
            await Dialog(main, new WriteSingleCoilDialog(main.Engine, 1, 20, false, 1), "05 single coil", null, "fc05_success.png");
            Check("05 effect", await ReadEq(main, 1, 20, 1, new[] { 1 }));

            var d06 = new WriteRegisterDialog(main.Engine, 1, 60, false, DisplayFormat.Signed, "0");
            await Dialog(main, d06, "06 single register", () => Field<TextBox>(d06, "_value").Text = "-7", "fc06_success.png");
            Check("06 effect", await ReadEq(main, 3, 60, 1, new[] { 65529 }));

            var d16f = new WriteRegisterDialog(main.Engine, 1, 62, false, DisplayFormat.Float, "0");
            await Dialog(main, d16f, "16 float via register dialog", () => Field<TextBox>(d16f, "_value").Text = "2,5", "fc16_float_success.png");
            Check("16 float effect", await ReadEq(main, 3, 62, 2, Formats.Encode(2.5, DisplayFormat.Float)));

            await Dialog(main, new WriteCoilsDialog(main.Engine, 1, 30, false, 3, new[] { 1, 0, 1 }), "15 multiple coils", null, "fc15_success.png");
            Check("15 effect", await ReadEq(main, 1, 30, 3, new[] { 1, 0, 1 }));

            await Dialog(main, new WriteRegistersDialog(main.Engine, 1, 70, false, 3, new[] { 9, 8, 7 }), "16 multiple registers", null, "fc16_success.png");
            Check("16 effect", await ReadEq(main, 3, 70, 3, new[] { 9, 8, 7 }));

            await main.Engine.Write(1, Pdu.WriteSingleRegister(80, 0x00F2));
            var d22 = new MaskWriteDialog(main.Engine, 1, 80, false);
            await Dialog(main, d22, "22 mask write", () =>
            {
                Field<TextBox>(d22, "_and").Text = "00F0";
                Field<TextBox>(d22, "_or").Text = "0025";
            }, "fc22_success.png");
            Check("22 effect", await ReadEq(main, 3, 80, 1, new[] { 0x00F5 }));

            var d23 = new ReadWriteRegistersDialog(main.Engine, 1, 90, false, 2, new[] { 11, 12 });
            await Dialog(main, d23, "23 read/write", () =>
            {
                Field<NumericUpDown>(d23, "_readAddress").Value = 70;
                Field<NumericUpDown>(d23, "_readQuantity").Value = 3;
            }, "fc23_success.png");
            ListBox rr = Field<ListBox>(d23, "_readResult");
            Check("23 read result list", rr.Items.Count == 3 && (string)rr.Items[0] == "00070 = 9", rr.Items.Count > 0 ? (string)rr.Items[0] : "empty");
            Check("23 written", await ReadEq(main, 3, 90, 2, new[] { 11, 12 }));
            d23.Close();

            var bad = new WriteRegisterDialog(main.Engine, 1, 500, false, DisplayFormat.Signed, "0");
            bad.Show(main);
            Field<TextBox>(bad, "_value").Text = "1";
            Field<Button>(bad, "SendButton").PerformClick();
            Label badResult = Field<Label>(bad, "ResultLabel");
            Check("06 illegal address in result line", await Until(() => badResult.Text.StartsWith("Illegal Data Address")), badResult.Text);
            bad.Close();

            // Test Center: raw MBAP frame
            var tc = new TestCenterDialog(main.Engine);
            tc.Show(main);
            Field<ComboBox>(tc, "_input").Text = "00 01 00 00 00 06 01 03 00 00 00 02";
            ((Button)tc.AcceptButton).PerformClick();
            ListBox tcLog = Field<ListBox>(tc, "_log");
            Check("test center rx", await Until(() => tcLog.Items.Count >= 2), tcLog.Items.Count.ToString());
            if (tcLog.Items.Count >= 2)
                Check("frame console response bytes", ((string)tcLog.Items[1]).EndsWith("00 01 00 00 00 07 01 03 04 00 64 FF FF"), (string)tcLog.Items[1]);
            Shot(tc, "testcenter.png");
            tc.Close();

            // persistence: document and workspace round trips
            string doc = Path.Combine(_outDir, "w1.mpoll");
            Check("save document", w1.Save(doc));
            PollWindow reopened = main.OpenPath(doc);
            Check("reopen document", reopened != null && reopened.Doc.Aliases[5] == "Setpoint"
                                     && reopened.Doc.View.CellFormats[2] == DisplayFormat.Float && reopened.Doc.View.PlcAddresses);
            string ws = Path.Combine(_outDir, "test.mpollw");
            main.SaveWorkspace(ws);
            int before = main.Windows.Count();
            foreach (PollWindow w in main.Windows) w.Dirty = false;
            main.LoadWorkspace(ws);
            await Task.Delay(300);
            Check("workspace reload window count", main.Windows.Count() == before, before + " -> " + main.Windows.Count());
            PollWindow anyW = main.Windows.FirstOrDefault(w => w.Doc.Definition.Function == 4);
            Check("workspace window polls again", anyW != null && await Until(() => anyW.Values != null));
            main.LayoutMdi(MdiLayout.TileVertical);
            Shot(main, "workspace_restored.png");

            // log file
            PollWindow lw = main.Windows.First(w => w.Doc.Definition.Function == 3 && w.Doc.Definition.Address == 0);
            string logPath = Path.Combine(_outDir, "log.csv");
            if (File.Exists(logPath)) File.Delete(logPath);
            using (var logging = new LogDialog())
            {
                Field<TextBox>(logging, "_path").Text = logPath;
                logging.Show(main);
                Shot(logging, "logging_setup.png");
                lw.StartLog(logging.Result());
                logging.Close();
            }
            await Until(() => File.Exists(logPath) && new FileInfo(logPath).Length > 0, 3000);
            await Task.Delay(500);
            lw.StopLog();
            string[] logLines = File.Exists(logPath) ? File.ReadAllLines(logPath) : new string[0];
            Check("csv log header", logLines.Length >= 2 && logLines[0].StartsWith("timestamp,status,rtt_ms,40001,40002"), logLines.FirstOrDefault() ?? "none");
            Check("csv log row", logLines.Length >= 2 && System.Text.RegularExpressions.Regex.IsMatch(logLines[1],
                  @"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}[+-]\d\d:\d\d,OK,\d+\.\d,100,-1"), logLines.Length > 1 ? logLines[1] : "none");

            await Wave1(main, lw, logPath);
            await DataTypes(main);

            // disconnect / reconnect
            main.Disconnect();
            Check("disconnected", !main.Engine.Running);
            int tx = main.Engine.Stats(lw.PollId).Tx;
            main.QuickConnect();
            Check("reconnect polls again", await Until(() => main.Engine.Stats(lw.PollId).Tx > tx));
        }

        /// <summary>Tags, scaling, alarms, write protection, KPIs and trend on a live window (holding registers 0..19).</summary>
        static async Task Wave1(MainForm main, PollWindow w, string logPath)
        {
            Confirm.Enabled = false; // a modal confirmation would block this unattended run
            main.Engine.Audit = new AuditLog(Path.Combine(_outDir, "uitest-audit.jsonl"));
            File.Delete(main.Engine.Audit.Path);
            main.Engine.Audit = new AuditLog(Path.Combine(_outDir, "uitest-audit.jsonl"));
            w.Doc.View.CellFormats.Clear();
            w.Rebuild();

            // scaling: register 0 = 100 -> 10.0 °C
            w.Doc.Tags[0] = new Tag { Name = "Temp", Scale = 0.1, Unit = "°C", Decimals = 1, High = 9, Deadband = 0.5 };
            w.Rebuild();
            Check("scaled cell", await Until(() => Cell(w, 0) == "10.0 °C"), Cell(w, 0));
            Check("H alarm active", await Until(() => w.Alarms.ContainsKey(0) && w.Alarms[0] == AlarmLevel.High), "");
            Point p0 = w.CellOf(0);
            Check("alarm colours cell", w.Grid[p0.X, p0.Y].Style.BackColor.ToArgb() != Color.Empty.ToArgb());
            Check("status shows KPIs and alarms", w.StatusText.Contains("OK ") && w.StatusText.Contains("RTT ") && w.StatusText.Contains("ALARMS 1"), w.StatusText);
            w.Activate();
            Shot(w, "alarm_active.png");
            using (var tag = new TagDialog("40001", w.Doc.TagAt(0), true))
            {
                tag.Show(main);
                Shot(tag, "tag_setup.png");
                tag.Close();
            }

            // grid write in engineering units: 8.5 °C -> raw 85, clears the alarm (8.5 < 9 - 0.5)
            string desc;
            byte[] pdu = w.BuildCellWrite(0, "8.5", out desc);
            Check("engineering -> raw 85", pdu[0] == 6 && Pdu.Get16(pdu, 3) == 85, Hex.Format(pdu));
            Check("confirm text shows old and raw", desc.Contains("raw 85") && desc.Contains("now 10.0 °C"), desc);
            w.Grid.CurrentCell = w.Grid[p0.X, p0.Y];
            w.Grid.BeginEdit(true);
            w.Grid.EditingControl.Text = "8.5";
            w.Grid.EndEdit();
            Check("scaled write read back", await Until(() => Cell(w, 0) == "8.5 °C"), Cell(w, 0));
            Check("alarm cleared", await Until(() => w.Alarms[0] == AlarmLevel.None));
            Check("alarm history has both transitions", w.AlarmHistory.Count >= 2 && w.AlarmHistory.Last().To == AlarmLevel.None);
            Shot(w, "alarm_cleared.png");

            // write limits
            w.Doc.Tags[0].WriteMax = 50;
            Exception limitEx = null;
            try { w.BuildCellWrite(0, "60", out desc); } catch (FormatException ex) { limitEx = ex; }
            Check("write limit enforced", limitEx != null && limitEx.Message.Contains("write limits"), limitEx == null ? "none" : limitEx.Message);

            // write protection from the menu path: grid edit is refused, value unchanged, audit records it
            main.Engine.WriteProtected = true; // engine flag directly: the menu path would persist to HKCU
            object before = await main.Engine.Submit(m => m.Transact(1, Pdu.ReadRequest(3, 0, 1)));
            Exception blocked = null;
            try { await main.Engine.Write(1, Pdu.WriteSingleRegister(0, 999)); } catch (Exception ex) { blocked = ex; }
            object after = await main.Engine.Submit(m => m.Transact(1, Pdu.ReadRequest(3, 0, 1)));
            Check("protected write refused", blocked is WriteBlockedException, blocked == null ? "sent" : blocked.GetType().Name);
            Check("protected value unchanged", ((int[])before)[0] == ((int[])after)[0] && ((int[])after)[0] == 85);
            main.Engine.WriteProtected = false;
            string[] audit = File.ReadAllLines(main.Engine.Audit.Path);
            Check("audit: grid write OK + blocked", audit.Length >= 2 && audit.Any(l => l.Contains("\"result\":\"OK\"") && l.Contains("\"values\":[85]"))
                                                && audit.Any(l => l.Contains("BLOCKED")), audit.Length + " lines");
            Check("audit chain intact", AuditLog.Verify(main.Engine.Audit.Path) == null);

            // tag map CSV export/import round trip through the window
            string csv = Path.Combine(_outDir, "uitest-tags.csv");
            TagMapCsv.Save(csv, w.ExportTags());
            w.Doc.Tags.Clear();
            w.ImportTagMap(csv);
            Check("tag map reimported", w.Doc.TagAt(0) != null && w.Doc.TagAt(0).Unit == "°C" && w.Doc.TagAt(0).WriteMax == 50);

            // trend: two pens, points accumulate, CSV export has timestamps
            main.AddToTrend(w, new List<int> { 0, 1 });
            Check("trend pens", main.TrendWin.Count == 2, main.TrendWin.Count.ToString());
            Check("trend points", await Until(() => main.TrendWin.PointCount(0) >= 25, 8000), main.TrendWin.PointCount(0).ToString());
            main.TrendWin.Refresh();
            await Task.Delay(300);
            string trendCsv = main.TrendWin.ToCsv();
            Check("trend csv", trendCsv.StartsWith("timestamp,series,value\n") && trendCsv.Contains("Temp [°C],8.5"), trendCsv.Split('\n').Skip(1).FirstOrDefault());
            main.TrendWin.SavePng(Path.Combine(_outDir, "trend.png"));
            Shot(main.TrendWin, "trend_window.png");
            Shot(main, "main_wave1.png");
            main.TrendWin.Hide();

            // restore register 0 for later runs
            await main.Engine.Write(1, Pdu.WriteSingleRegister(0, 100));
            w.Doc.Tags.Clear();
            w.Rebuild();
            Confirm.Enabled = true;
        }

        /// <summary>N1: custom type × byte order cells, string and bit-field writes through the grid, Interpret As,
        /// tag map format column. Uses holding registers 100..111 of the demo slave.</summary>
        static async Task DataTypes(MainForm main)
        {
            Confirm.Enabled = false;
            // seed: 100..101 float 1.5 BADC, 102..103 int32 -2 CDAB, 104..107 "Hi!" string, 108 = 0x00F5
            await main.Engine.Write(1, Pdu.WriteMultipleRegisters(100, new[] { 0xC03F, 0x0000, 0xFFFE, 0xFFFF, 0x4869, 0x2100, 0, 0, 0x00F5 }));
            PollWindow w = main.NewWindow();
            w.ApplyDefinition(new PollDefinition { Function = 3, Address = 100, Quantity = 12, ScanRateMs = 150 }, new ViewOptions { Rows = 20 });
            Check("n1 values", await Until(() => w.Values != null));
            w.SetCellFormat(0, new DisplayFormat(DataType.Float32, ByteOrder.BADC));
            w.SetCellFormat(2, new DisplayFormat(DataType.Int32, ByteOrder.CDAB));
            w.SetCellFormat(4, new DisplayFormat(DataType.String, ByteOrder.ABCD, 4));
            w.SetCellFormat(8, new DisplayFormat(DataType.BitField, bitStart: 4, bitWidth: 3));
            Check("n1 float BADC cell", await Until(() => Cell(w, 0) == "1.5"), Cell(w, 0));
            Check("n1 int32 CDAB cell", Cell(w, 2) == "-2", Cell(w, 2));
            Check("n1 string cell + blank partners", Cell(w, 4) == "Hi!" && Cell(w, 5) == "" && Cell(w, 7) == "", Cell(w, 4));
            Check("n1 bit field cell", Cell(w, 8) == "7", Cell(w, 8));

            // grid writes: float BADC, string, bit field (merges other bits from the last read)
            Point p0 = w.CellOf(0);
            w.Activate();
            w.Grid.CurrentCell = w.Grid[p0.X, p0.Y];
            w.Grid.BeginEdit(true);
            w.Grid.EditingControl.Text = "-0,25";
            w.Grid.EndEdit();
            Check("n1 float BADC write", await Until(() => Cell(w, 0) == "-0.25"), Cell(w, 0));
            Check("n1 float BADC on the wire", await ReadEq(main, 3, 100, 2, new[] { 0x80BE, 0x0000 }));
            string desc;
            byte[] pdu = w.BuildCellWrite(4, "TestData", out desc);
            Check("n1 string uses FC16 over 4 registers", pdu[0] == 16 && Pdu.Get16(pdu, 3) == 4, Hex.Format(pdu));
            Point p4 = w.CellOf(4);
            w.Grid.CurrentCell = w.Grid[p4.X, p4.Y];
            w.Grid.BeginEdit(true);
            w.Grid.EditingControl.Text = "TestData";
            w.Grid.EndEdit();
            Check("n1 string write", await Until(() => Cell(w, 4) == "TestData"), Cell(w, 4));
            pdu = w.BuildCellWrite(8, "2", out desc);
            Check("n1 bit field merges (0x00F5 -> 0x00A5)", pdu[0] == 6 && Pdu.Get16(pdu, 3) == 0x00A5, Hex.Format(pdu) + " " + desc);
            Point p8 = w.CellOf(8);
            w.Grid.CurrentCell = w.Grid[p8.X, p8.Y];
            w.Grid.BeginEdit(true);
            w.Grid.EditingControl.Text = "2";
            w.Grid.EndEdit();
            Check("n1 bit field write", await Until(() => Cell(w, 8) == "2") && await ReadEq(main, 3, 108, 1, new[] { 0x00A5 }), Cell(w, 8));
            Exception tooWide = null;
            w.SetCellFormat(10, new DisplayFormat(DataType.Float64, ByteOrder.ABCD));
            try { w.BuildCellWrite(10, "1", out desc); } catch (FormatException ex) { tooWide = ex; }
            Check("n1 value past window end refused", tooWide != null && tooWide.Message.Contains("needs 4 registers"), tooWide == null ? "none" : tooWide.Message);
            Check("n1 cut-off value falls back to hex", await Until(() => Cell(w, 10).StartsWith("0x")), Cell(w, 10));

            // format picker: string type enables length, OK builds the format
            var fd = new FormatDialog(DisplayFormat.Float);
            fd.Show(main);
            Field<ComboBox>(fd, "_type").SelectedIndex = (int)DataType.String;
            Field<ComboBox>(fd, "_order").SelectedIndex = (int)ByteOrder.BADC;
            Field<NumericUpDown>(fd, "_length").Value = 5;
            Check("n1 format dialog enables length only for strings", Field<NumericUpDown>(fd, "_length").Enabled && !Field<NumericUpDown>(fd, "_bitStart").Enabled);
            ((Button)fd.AcceptButton).PerformClick();
            Check("n1 format dialog result", fd.Result == new DisplayFormat(DataType.String, ByteOrder.BADC, 5), fd.Result == null ? "null" : fd.Result.Key);
            Check("n1 format dialog info", Field<Label>(fd, "_info").Text.StartsWith("String 10 ch BADC: 5 registers"), Field<Label>(fd, "_info").Text);
            Shot(fd, "format_dialog.png");
            fd.Close();

            // Interpret As follows the selection
            w.Grid.ClearSelection();
            w.Grid[p0.X, p0.Y].Selected = true;
            main.ShowInterpret(w);
            Check("n1 interpret rows", await Until(() => main.InterpretWin.RowCount >= 30), main.InterpretWin.RowCount.ToString());
            var rows = Enumerable.Range(0, main.InterpretWin.RowCount).Select(i => main.InterpretWin.Row(i)).ToList();
            Check("n1 interpret shows float BADC -0.25", rows.Contains("Float32 BADC = -0.25"), string.Join(" | ", rows.Take(12)));
            Check("n1 interpret source", main.InterpretWin.Source.Contains("80BE 0000"), main.InterpretWin.Source);
            Shot(main.InterpretWin, "interpret_float.png");
            w.Grid.ClearSelection();
            w.Grid[p4.X, p4.Y].Selected = true;
            main.InterpretWin.BringToFront();
            main.InterpretWin.Refresh();
            await Task.Delay(400);
            Shot(main.InterpretWin, "interpret.png");
            main.InterpretWin.Hide();
            w.WindowState = FormWindowState.Normal;
            w.SetBounds(0, 0, 560, 360);
            await Task.Delay(300);
            Shot(main, "main_n1.png");

            // tag map carries the formats; document keeps them
            string csv = Path.Combine(_outDir, "uitest-n1-tags.csv");
            TagMapCsv.Save(csv, w.ExportTags(), w.Doc.View.CellFormats);
            w.Doc.View.CellFormats.Clear();
            w.ImportTagMap(csv);
            Check("n1 tag map restores formats", w.Doc.View.CellFormats.Count == 5
                                                 && w.Doc.View.CellFormats[4] == new DisplayFormat(DataType.String, ByteOrder.ABCD, 4),
                  string.Join(",", w.Doc.View.CellFormats.Select(kv => kv.Key + "=" + kv.Value.Key)));
            string doc = Path.Combine(_outDir, "n1.mpoll");
            Check("n1 save", w.Save(doc));
            string text = File.ReadAllText(doc);
            Check("n1 document keys", text.Contains("\"str4\"") && text.Contains("\"f32_badc\"") && text.Contains("\"bits4.3\""), text);
            w.Dirty = false;
            w.Close();
            Confirm.Enabled = true;
        }

        /// <summary>Device features against tools/device_sim.py (RTU over TCP on port, Modbus TCP on port+1): profiles,
        /// Device Settings writes with confirmation and audit, slave-ID follow-up, factory reset, PZEM reset and
        /// address recovery, diagnosis + one-click fixes, Find Device, two connections polling in parallel.
        /// Usage: MultiPollStudioUiTest.exe devices host 5030 outdir</summary>
        static async Task Devices(MainForm main, string host, int port)
        {
            await Until(() => main.Windows.Any(), 3000);
            foreach (PollWindow w0 in main.Windows.ToList()) { w0.Dirty = false; w0.Close(); }
            var asked = new List<string>();
            Ui.AutoAnswer = text => { asked.Add(text); return true; };
            Confirm.Enabled = true;                           // confirmation path on, answered by AutoAnswer
            main.Engine.Audit = new AuditLog(Path.Combine(_outDir, "devices-audit.jsonl"));
            File.Delete(main.Engine.Audit.Path);
            main.Engine.Audit = new AuditLog(Path.Combine(_outDir, "devices-audit.jsonl"));
            main.Engine.WriteProtected = false;
            var rtu = new ConnectionSettings { Kind = ConnectionKind.SerialOverTcp, Mode = SerialMode.Rtu, Host = host, TcpPort = port, ResponseTimeoutMs = 400, DelayBetweenPollsMs = 5 };
            var mbtcp = new ConnectionSettings { Kind = ConnectionKind.Tcp, Host = host, TcpPort = port + 1, ResponseTimeoutMs = 400, DelayBetweenPollsMs = 5 };
            main.ConnSettings = rtu;
            main.QuickConnect();
            Check("dev connect", main.Engine.Running);
            if (!main.Engine.Running) return;

            // --- profiles: add ADM-4204V (ID 1) → one window per block, values with names and limits
            DeviceProfile adm = main.ProfileById("adm-4204v");
            Check("dev profiles loaded", main.DeviceProfiles.Count >= 10 && adm != null, main.DeviceProfiles.Count.ToString());
            using (var profiles = new AddDeviceDialog(main.DeviceProfiles, Field<List<string>>(main, "_profileErrors")))
            {
                profiles.Choose(adm.Id);
                profiles.Show(main);
                Shot(profiles, "device_profiles.png");
                profiles.Close();
            }
            List<PollWindow> admWins = main.AddDevice(adm, 1, false);
            PollWindow cfg = admWins.First(w => w.Doc.Definition.Address == 0x9F);
            Check("dev ADM windows per block", admWins.Count == adm.Blocks.Count, admWins.Count.ToString());
            Check("dev ADM config values", await Until(() => cfg.Values != null && Cell(cfg, 0) == "0x8110"), cfg.ErrorText);
            Check("dev ADM aliases", cfg.Doc.Aliases[1] == "Slave ID" && cfg.Text.StartsWith("ADM-4204V ID 1"), cfg.Text);
            main.LayoutMdi(MdiLayout.TileVertical);
            Shot(main, "device_profile_windows.png");

            // --- Device Settings: read current values, set ID 1 → 9 (FC06 0x00A0), windows follow, read back
            var ds = new DeviceSettingsDialog(main, cfg, adm) { RestartWaitMs = 200 };
            ds.Show(main);
            await ds.ReadAllAsync();
            Check("dev settings read", ds.CurrentText("slave_id") == "1" && ds.CurrentText("baud") == "9600" && ds.CurrentText("parity") == "None",
                  ds.CurrentText("slave_id") + "/" + ds.CurrentText("baud") + "/" + ds.CurrentText("parity"));
            ds.Choose("slave_id", 9);
            asked.Clear();
            await ds.SetAsync("slave_id");
            Check("dev set ID asked to confirm", asked.Any(a => a.Contains("Slave ID → 9") && a.Contains("restarts by itself")), string.Join(" | ", asked));
            Check("dev set ID verified", ds.LogLines.Any(l => l.Contains("Verified: the device reports 9")), string.Join(" | ", ds.LogLines));
            Check("dev all ADM windows follow ID 9", admWins.All(w => w.Doc.Definition.SlaveId == 9), string.Join(",", admWins.Select(w => w.Doc.Definition.SlaveId)));
            Check("dev ADM polls at ID 9", await Until(() => cfg.LastStatus == Status.Ok && Cell(cfg, 1) == "9"), cfg.ErrorText + " " + Cell(cfg, 1));
            Check("dev window title follows", cfg.Text.Contains(" ID 9 "), cfg.Text);
            string[] audit = File.ReadAllLines(main.Engine.Audit.Path);
            Check("dev audit has the ID write", audit.Any(l => l.Contains("\"address\":160") && l.Contains("\"values\":[9]") && l.Contains("\"result\":\"OK\"")), audit.LastOrDefault());

            // write protection refuses settings
            main.Engine.WriteProtected = true; // engine flag directly: the menu path would persist to HKCU
            ds.Choose("parity", 2);
            await ds.SetAsync("parity");
            Check("dev protected setting refused", ds.LogLines.Any(l => l.Contains("Write protection is on")), ds.LogLines.LastOrDefault());
            main.Engine.WriteProtected = false;

            // baud change: the simulator stores it; the profile choice moves the connection to 19200 (RTU over TCP: label only)
            ds.Choose("baud", 4);
            await ds.SetAsync("baud");
            Check("dev baud set and connection follows", main.ConnSettings.BaudRate == 19200 && ds.LogLines.Any(l => l.Contains("Connection changed to")), main.ConnSettings.Describe());
            Check("dev baud read back", await Until(() => ds.LogLines.Any(l => l.Contains("Verified: the device reports 19200")), 4000), ds.LogLines.LastOrDefault());
            Shot(ds, "device_settings.png");

            // factory reset (two FC06 writes): back to ID 1 / 9600
            asked.Clear();
            await ds.RunAction("factory_reset");
            Check("dev factory reset confirmed", asked.Any(a => a.Contains("Restore factory settings")), string.Join(" | ", asked));
            Check("dev factory reset back to ID 1", await Until(() => ds.CurrentText("slave_id") == "1" && cfg.Doc.Definition.SlaveId == 1, 5000), ds.CurrentText("slave_id"));
            Check("dev factory reset baud", ds.CurrentText("baud") == "9600" && main.ConnSettings.BaudRate == 9600, ds.CurrentText("baud"));
            ds.Close();
            foreach (PollWindow w in admWins) { w.Dirty = false; w.Close(); }

            // --- PZEM-016 (ID 2): u32 low word first, reset energy (raw FC 0x42), forgotten ID via 0xF8
            DeviceProfile pzem = main.ProfileById("pzem-016");
            PollWindow pz = main.AddDevice(pzem, 2, false).First(w => w.Doc.Definition.Function == 4);
            Check("dev PZEM current 1.000 A (u32 CDAB × 0.001)", await Until(() => pz.Values != null && Cell(pz, 1).StartsWith("1.000")), Cell(pz, 1));
            Check("dev PZEM energy 123456", Cell(pz, 5).StartsWith("123456"), Cell(pz, 5));
            var pds = new DeviceSettingsDialog(main, pz, pzem) { RestartWaitMs = 100 };
            pds.Show(main);
            await pds.RunAction("reset_energy");
            Check("dev PZEM reset energy", await Until(() => Cell(pz, 5).StartsWith("0")), Cell(pz, 5) + " " + string.Join(" | ", pds.LogLines));
            PollDefinition wrong = pz.Doc.Definition.Clone();
            wrong.SlaveId = 77;
            pz.ApplyDefinition(wrong, pz.Doc.View);
            int? found = await pds.RecoverAsync(false);
            Check("dev PZEM forgotten ID via 0xF8", found == 2 && pz.Doc.Definition.SlaveId == 2, found.ToString());
            pds.Close();
            foreach (PollWindow w in main.Windows.ToList()) { w.Dirty = false; w.Close(); }

            // --- BGT-SMPS recovery via 0xFE (reply carries the real ID 4)
            DeviceProfile bgt = main.ProfileById("bgt-smps");
            PollWindow soil = main.AddDevice(bgt, 99, false)[0];
            var bds = new DeviceSettingsDialog(main, soil, bgt) { RestartWaitMs = 100 };
            bds.Show(main);
            int? soilId = await bds.RecoverAsync(false);
            Check("dev BGT forgotten ID via 0xFE", soilId == 4, soilId.ToString());
            Check("dev BGT scaled values", await Until(() => soil.LastStatus == Status.Ok && Cell(soil, 0) == "26.0 °C" && Cell(soil, 7).StartsWith("6.86")), Cell(soil, 0) + " / " + Cell(soil, 7));
            bds.Close();

            // --- diagnosis + one-click fixes
            PollDefinition bad = soil.Doc.Definition.Clone();
            bad.Function = 4; // BGT answers FC03 only → Illegal Function
            soil.ApplyDefinition(bad, soil.Doc.View);
            Check("dev illegal function shown with fix link", await Until(() => soil.LastFailure != null && soil.LastFailure.Status == Status.IllegalFunction && soil.FixVisible), soil.ErrorText);
            using (DiagnoseDialog dd = main.CreateDiagnose(soil))
            {
                dd.Show(main);
                Advice sw = dd.Advice.First(a => a.Fix == FixKind.SwitchFunction);
                await dd.RunFix(sw);
                Check("dev fix switch FC04→FC03", soil.Doc.Definition.Function == 3 && dd.ResultText.Contains("answers now"), dd.ResultText);
                Shot(dd, "diagnose.png");
                dd.Close();
            }
            soil.Reframe(4, 8); // 4..11: only 4..7 exist; tags/aliases move with their registers
            Check("dev reframe keeps pH tag on register 7", soil.Doc.Aliases[3] == "pH" && soil.Doc.TagAt(3).Scale == 0.01, string.Join(",", soil.Doc.Aliases.Select(kv => kv.Key + "=" + kv.Value)));
            Check("dev illegal address", await Until(() => soil.LastFailure != null && soil.LastFailure.Status == Status.IllegalDataAddress), soil.ErrorText);
            string probe = await main.RunFix(soil, Diagnosis.For(Status.IllegalDataAddress, "", rtu, soil.Doc.Definition).First(a => a.Fix == FixKind.ProbeRange));
            Check("dev fix probe range keeps 4..7", soil.Doc.Definition.Address == 4 && soil.Doc.Definition.Quantity == 4 && probe.Contains("answers now"), probe);
            Check("dev pH still scaled after fix", await Until(() => Cell(soil, 3).StartsWith("6.86")), Cell(soil, 3));
            soil.Reframe(0, 8);
            Check("dev reframe back keeps names of kept registers", soil.Doc.Aliases[7] == "pH" && !soil.Doc.Aliases.ContainsKey(0), string.Join(",", soil.Doc.Aliases.Keys));
            PollDefinition bad2 = soil.Doc.Definition.Clone();
            bad2.SlaveId = 50;
            soil.ApplyDefinition(bad2, soil.Doc.View);
            Check("dev timeout for absent ID", await Until(() => soil.LastFailure != null && soil.LastFailure.Status == Status.Timeout, 4000), soil.ErrorText);

            // Find Device (line search over the simulator bus: one setting, IDs 1..6)
            using (FindDeviceDialog fd = main.CreateFindDevice(main.ConnSettings))
            {
                fd.Show(main);
                fd.Configure(main.ConnSettings.Port, new[] { 9600 }, true, false, false, 1, 6, true, 300);
                Field<CheckBox>(fd, "_stopFirst").Checked = false;
                fd.StartOrStop();
                await fd.Running;
                Check("dev find device lists 5 devices", fd.Hits.Count == 5 && fd.Hits.Select(h => h.SlaveId).SequenceEqual(new[] { 1, 2, 3, 4, 5 }),
                      fd.ProgressText + " " + string.Join(",", fd.Hits.Select(h => h.SlaveId)));
                Check("dev find device identifies ADM-4204V", fd.Hits.Count > 0 && fd.Hits[0].Identified.Contains("ADM-4204V"), fd.Hits.Count > 0 ? fd.Hits[0].Line : "");
                Check("dev engine restored after search", await Until(() => main.Engine.Running), "");
                Shot(fd, "find_device.png");
                main.ApplyHit(soil, main.ConnSettings, fd.Hits.First(h => h.SlaveId == 4));
                fd.Close();
            }
            Check("dev use hit → polls ID 4", await Until(() => soil.LastStatus == Status.Ok && soil.Doc.Definition.SlaveId == 4), soil.ErrorText);

            // --- a second connection in parallel: XY-MD02 over Modbus TCP in its own window
            DeviceProfile xy = main.ProfileById("xy-md02");
            PollWindow th = main.AddDevice(xy, 5, false, mbtcp).First(w => w.Doc.Definition.Function == 4);
            Check("dev own connection engine", th.Engine != main.Engine && th.Doc.Connection != null && th.Text.Contains("[" + host + "]"), th.Text);
            Check("dev XY-MD02 via Modbus TCP 30.5 °C", await Until(() => th.LastStatus == Status.Ok && Cell(th, 0).StartsWith("30.5")), Cell(th, 0) + " " + th.ErrorText);
            int soilTx = main.Engine.Stats(soil.PollId).Tx, thTx = th.Engine.Stats(th.PollId).Tx;
            await Task.Delay(2500);
            Check("dev both connections poll", main.Engine.Stats(soil.PollId).Tx > soilTx && th.Engine.Stats(th.PollId).Tx > thTx,
                  (main.Engine.Stats(soil.PollId).Tx - soilTx) + " / " + (th.Engine.Stats(th.PollId).Tx - thTx));
            main.LayoutMdi(MdiLayout.TileVertical);
            await Task.Delay(600);
            Shot(main, "devices_main.png");
            string ws = Path.Combine(_outDir, "devices.mpollw");
            main.SaveWorkspace(ws);
            foreach (PollWindow w in main.Windows.ToList()) { w.Dirty = false; w.Close(); }
            Check("dev idle own engine released", main.AllEngines.Count() == 1, main.AllEngines.Count().ToString());
            main.LoadWorkspace(ws);
            PollWindow th2 = main.Windows.FirstOrDefault(w => w.Doc.Connection != null);
            Check("dev workspace restores own connection", th2 != null && await Until(() => th2.LastStatus == Status.Ok), th2 == null ? "none" : th2.ErrorText);
            main.LayoutMdi(MdiLayout.TileVertical);
            Shot(main, "devices_workspace_restored.png");
            main.Disconnect();
            Check("dev disconnect stops all", main.AllEngines.All(e => !e.Running));
            Ui.AutoAnswer = null;
        }

        static async Task Dialog(MainForm main, Form dlg, string name, Action fill, string screenshot)
        {
            dlg.Show(main);
            fill?.Invoke();
            Label result = Field<Label>(dlg, "ResultLabel");
            result.Text = "";
            Field<Button>(dlg, "SendButton").PerformClick();
            bool done = await Until(() => result.Text.Length > 0 && result.Text != "Sending…");
            Check(name + ": Response ok", done && result.Text == "Response ok", result.Text);
            if (done && result.Text == "Response ok") Shot(dlg, screenshot);
            if (!(dlg is ReadWriteRegistersDialog)) dlg.Close();
        }

        static async Task<bool> ReadEq(MainForm main, int fc, int address, int quantity, int[] expected)
        {
            object r = await main.Engine.Submit(m => m.Transact(1, Pdu.ReadRequest(fc, address, quantity)));
            bool ok = ((int[])r).SequenceEqual(expected);
            if (!ok) _log.WriteLine("      read " + fc + "@" + address + " = [" + string.Join(",", (int[])r) + "], expected [" + string.Join(",", expected) + "]");
            return ok;
        }
    }
}
