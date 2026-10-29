using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MultiPollStudio.UI
{
    sealed class TrafficWindow : Form
    {
        const int Limit = 5000;
        readonly ListBox _list = new ListBox { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, IntegralHeight = false, HorizontalScrollbar = true };
        readonly CheckBox _stop = new CheckBox { Text = "&Stop", Appearance = Appearance.Button, AutoSize = true, MinimumSize = new Size(80, 26), TextAlign = ContentAlignment.MiddleCenter };
        readonly CheckBox _stopOnError = new CheckBox { Text = "S&top on Error", AutoSize = true, Margin = new Padding(8, 8, 3, 3) };
        int _seq;

        public TrafficWindow()
        {
            Text = "Communication Traffic";
            Font = SystemFonts.MessageBoxFont;
            _list.Font = Ui.Mono();
            Size = new Size(760, 380);
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            _stop.CheckedChanged += (o, e) => _stop.Text = _stop.Checked ? "&Continue" : "&Stop";
            var bar = Ui.Row(_stop, _stopOnError, Ui.Button("&Copy", (o, e) => Copy()), Ui.Button("S&ave", (o, e) => SaveLog()),
                             Ui.Button("C&lear", (o, e) => _list.Items.Clear()), Ui.Button("E&xit", (o, e) => Hide()));
            bar.Dock = DockStyle.Bottom;
            Controls.Add(_list);
            Controls.Add(bar);
        }

        public int Count { get { return _list.Items.Count; } }
        public string Line(int i) { return (string)_list.Items[i]; }

        public void Add(string direction, byte[] data)
        {
            _seq++;
            if (_stop.Checked || !Visible) return;
            _list.BeginUpdate();
            _list.Items.Add(Traffic.Line(_seq, direction, data));
            while (_list.Items.Count > Limit) _list.Items.RemoveAt(0);
            _list.EndUpdate();
            _list.TopIndex = Math.Max(0, _list.Items.Count - 1);
        }

        public void Error(string text)
        {
            if (!Visible || _stop.Checked) return;
            _list.Items.Add((_seq % 1000000).ToString("D6") + " | ERROR | " + text);
            if (_stopOnError.Checked) _stop.Checked = true;
        }

        void Copy()
        {
            var lines = _list.SelectedItems.Cast<string>().ToList();
            if (lines.Count > 0) Clipboard.SetText(string.Join(Environment.NewLine, lines));
        }

        void SaveLog()
        {
            using (var d = new SaveFileDialog { Filter = "Modbus comm. data (*.txt)|*.txt", FileName = "commdata.txt" })
                if (d.ShowDialog(this) == DialogResult.OK) File.WriteAllLines(d.FileName, _list.Items.Cast<string>());
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnFormClosing(e);
        }
    }

    sealed class ErrorCountersDialog : Form
    {
        public ErrorCountersDialog(PollStats st)
        {
            Text = "Error Counters";
            var list = new ListBox { Width = 360, Height = 200, IntegralHeight = false };
            list.Items.Add(st.Tx + "\tTransmitted requests");
            if (st.ByStatus.Count == 0) list.Items.Add("NO ERRORS!");
            foreach (var kv in st.ByStatus.OrderBy(k => (int)k.Key)) list.Items.Add(kv.Value + "\t" + kv.Key.Text());
            list.UseTabStops = true;
            var ok = Ui.Button("OK", null);
            ok.DialogResult = DialogResult.OK;
            AcceptButton = ok;
            CancelButton = ok;
            Ui.Stack(this, new Control[] { list }, ok);
        }
    }

    sealed partial class MainForm : Form
    {
        public const string DocFilter = "MultiPoll Studio document (*.mpoll)|*.mpoll|All files (*.*)|*.*";
        const string WorkspaceFilter = "MultiPoll Studio workspace (*.mpollw)|*.mpollw";
        const string RegKey = @"Software\MultiPollStudio";

        public readonly PollEngine Engine = new PollEngine();
        public ConnectionSettings ConnSettings;
        public readonly TrafficWindow TrafficWin = new TrafficWindow();
        public readonly TrendWindow TrendWin = new TrendWindow();
        public readonly InterpretDialog InterpretWin = new InterpretDialog();
        readonly ToolStripStatusLabel _protect = new ToolStripStatusLabel("") { BorderSides = ToolStripStatusLabelBorderSides.Left };
        readonly ToolStripStatusLabel _hint = new ToolStripStatusLabel("For Help, press F1.") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        readonly ToolStripStatusLabel _conn = new ToolStripStatusLabel("No connection");
        readonly StatusStrip _statusBar = new StatusStrip();
        readonly List<ToolStripItem> _windowItems = new List<ToolStripItem>();
        readonly Dictionary<DisplayFormat, ToolStripMenuItem> _fmtItems = new Dictionary<DisplayFormat, ToolStripMenuItem>();
        ToolStripMenuItem _enabledItem, _plcItem, _protoItem, _disconnectItem, _autoItem, _logItem, _logOffItem, _protectItem, _confirmItem;

        public MainForm()
        {
            Text = "MultiPoll Studio";
            IsMdiContainer = true;
            Font = SystemFonts.MessageBoxFont;
            Size = new Size(1100, 700);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            ConnSettings = LoadConnSettings();
            Engine.OnTraffic = (d, b) => UiPost(() => TrafficWin.Add(d, b));
            Engine.OnConnection = (up, text) => UiPost(() => ConnectionChanged(up, text));
            Engine.Audit = new AuditLog(AuditPath ?? AuditLog.DefaultPath());
            Engine.WriteProtected = RegGet("WriteProtected", 0) == 1;
            Confirm.Enabled = RegGet("ConfirmWrites", 1) == 1;
            _statusBar.Items.Add(_hint);
            _statusBar.Items.Add(_protect);
            _statusBar.Items.Add(_conn);
            Controls.Add(_statusBar);
            MainMenuStrip = BuildMenu();
            Controls.Add(BuildToolbar());
            Controls.Add(MainMenuStrip);
            MdiChildActivate += (o, e) => SyncMenu();
            TrafficWin.Owner = this;
            TrendWin.Owner = this;
            InterpretWin.Owner = this;
            InterpretWin.Apply += (w, off, f) => w.SetCellFormat(off, f);
            SyncProtection();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (MdiChildren.Length == 0) NewWindow();
            if (RegGet("AutoConnect", 0) == 1) QuickConnect();
        }

        void UiPost(Action a)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(a); } catch (InvalidOperationException) { }
        }

        // -- settings (HKCU\Software\MultiPollStudio) -----------------------------------------

        static int RegGet(string name, int fallback)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(SettingsKey))
                return k == null ? fallback : Convert.ToInt32(k.GetValue(name, fallback));
        }

        static string RegGetString(string name)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(SettingsKey))
                return k == null ? null : k.GetValue(name) as string;
        }

        /// <summary>Settings key; tests set this to a throwaway key so the user's settings stay untouched.</summary>
        public static string SettingsKey = RegKey;
        /// <summary>Audit log location override (tests); null = %LOCALAPPDATA%\MultiPollStudio\audit.jsonl.</summary>
        public static string AuditPath;

        static void RegSet(string name, object value)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(SettingsKey)) k.SetValue(name, value);
        }

        static ConnectionSettings LoadConnSettings()
        {
            string raw = RegGetString("Connection");
            if (raw == null) return new ConnectionSettings();
            try { return Persistence.ConnectionFromDict(Persistence.Parse(raw)); }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException) { return new ConnectionSettings(); }
        }

        void SaveConnSettings() { RegSet("Connection", Persistence.Serialize(Persistence.ToDict(ConnSettings))); }

        // -- menu ---------------------------------------------------------------------------

        ToolStripMenuItem Item(ToolStripMenuItem parent, string text, Action action, Keys keys = Keys.None, bool needsWindow = false, string command = null)
        {
            var i = new ToolStripMenuItem(text, null, (o, e) =>
            {
                if (_contextHelp && command != null && command != "context") { ShowCommandHelp(command); return; }
                action();
            }) { ShortcutKeys = keys };
            if (command != null) _commands.Add(command, i);
            if (keys == (Keys.Shift | Keys.F6)) i.ShortcutKeyDisplayString = "Shift+F6";
            parent.DropDownItems.Add(i);
            if (needsWindow) _windowItems.Add(i);
            return i;
        }

        ToolStripMenuItem WItem(ToolStripMenuItem parent, string text, Action<PollWindow> action, Keys keys = Keys.None, string command = null)
        {
            return Item(parent, text, () => { PollWindow w = Active; if (w != null) action(w); }, keys, true, command);
        }

        static void Sep(ToolStripMenuItem m) { m.DropDownItems.Add(new ToolStripSeparator()); }

        MenuStrip BuildMenu()
        {
            var ms = new MenuStrip();
            var file = new ToolStripMenuItem("&File");
            Item(file, "&New", () => NewWindow(), Keys.Control | Keys.N, command: "new");
            Item(file, "&Open…", OpenDocument, Keys.Control | Keys.O, command: "open");
            WItem(file, "&Close", w => w.Close(), command: "close");
            WItem(file, "&Save", w => w.Save(), Keys.Control | Keys.S, command: "save");
            WItem(file, "Save &As…", SaveAs);
            Sep(file);
            Item(file, "Open &Workspace…", OpenWorkspace);
            Item(file, "Sa&ve Workspace…", SaveWorkspace);
            Sep(file);
            WItem(file, "&Print…", Print, Keys.Control | Keys.P, command: "print");
            Sep(file);
            Item(file, "E&xit", Close);

            var edit = new ToolStripMenuItem("&Edit");
            WItem(edit, "&Copy", w => w.CopySelection(), Keys.Control | Keys.C);
            WItem(edit, "&Paste", w => w.PasteAliases(), Keys.Control | Keys.V);
            WItem(edit, "Select &All", w => w.Grid.SelectAll(), Keys.Control | Keys.A);

            var conn = new ToolStripMenuItem("&Connection");
            Item(conn, "&Connect…", ConnectDialog, Keys.F3);
            _disconnectItem = Item(conn, "&Disconnect", Disconnect, Keys.F4);
            WItem(conn, "Connect Active &Target", ConnectTarget, command: "connect");
            WItem(conn, "Disconnect Active T&arget", DisconnectTarget, command: "disconnect");
            Sep(conn);
            _autoItem = Item(conn, "&Auto Connect at startup", () =>
            {
                _autoItem.Checked = !_autoItem.Checked;
                RegSet("AutoConnect", _autoItem.Checked ? 1 : 0);
            });
            _autoItem.Checked = RegGet("AutoConnect", 0) == 1;
            Item(conn, "&Quick Connect", QuickConnect, Keys.F5);
            Sep(conn);
            _protectItem = Item(conn, "&Write Protection (read-only)", () => SetWriteProtected(!Engine.WriteProtected), Keys.Control | Keys.Shift | Keys.W);
            _confirmItem = Item(conn, "Con&firm Every Write", () =>
            {
                Confirm.Enabled = !Confirm.Enabled;
                RegSet("ConfirmWrites", Confirm.Enabled ? 1 : 0);
                SyncProtection();
            });
            Item(conn, "Open Write A&udit Log", OpenAuditLog);
            Sep(conn);
            WItem(conn, "Window Connection (own port)…", EditWindowConnection);

            var device = new ToolStripMenuItem("De&vice");
            Item(device, "&Add Device from Profile…", () => AddDeviceDialog(null), Keys.Control | Keys.D);
            WItem(device, "Device &Settings (ID / baud / parity)…", OpenDeviceSettings, Keys.Control | Keys.E);
            WItem(device, "&Diagnose Last Error…", Diagnose, Keys.Control | Keys.G);
            Sep(device);
            Item(device, "Unit-ID &Scan…", OpenScan);
            Item(device, "&Find Device (baud / parity / ID)…", () => OpenFindDevice(Active), Keys.Control | Keys.F);
            Sep(device);
            Item(device, "Open &Profile Folder", OpenProfileFolder);

            var setup = new ToolStripMenuItem("&Poll");
            WItem(setup, "Poll &Setup…", w => w.OpenDefinition(), Keys.F8, command: "definition");
            WItem(setup, "Read &Once", w => w.ReadOnce(), Keys.F6, command: "read");
            _enabledItem = WItem(setup, "Polling &Enabled", w => w.SetEnabled(!w.Doc.Definition.Enabled), Keys.Shift | Keys.F6, command: "enabled");
            Sep(setup);
            WItem(setup, "&Use as Default", UseAsDefault);
            Sep(setup);
            WItem(setup, "&Tag / Scaling / Alarms…", w => w.EditTag(), Keys.Control | Keys.T);
            WItem(setup, "&Import Tag Map (CSV)…", w => w.ImportTagMap());
            WItem(setup, "E&xport Tag Map (CSV)…", w => w.ExportTagMap());

            var fn = new ToolStripMenuItem("&Write");
            WItem(fn, "0&5: Write Single Coil…", w => w.OpenFunction(FC.WriteSingleCoil), Keys.Alt | Keys.F5, command: "fc05");
            WItem(fn, "0&6: Write Single Register…", w => w.OpenFunction(FC.WriteSingleRegister), Keys.Alt | Keys.F6, command: "fc06");
            WItem(fn, "15: Write &Coils…", w => w.OpenFunction(FC.WriteMultipleCoils), Keys.Alt | Keys.F7, command: "fc15");
            WItem(fn, "16: Write &Registers…", w => w.OpenFunction(FC.WriteMultipleRegisters), Keys.Alt | Keys.F8, command: "fc16");
            WItem(fn, "22: &Mask Write Register…", w => w.OpenFunction(FC.MaskWriteRegister), command: "fc22");
            WItem(fn, "2&3: Read/Write Registers…", w => w.OpenFunction(FC.ReadWriteMultipleRegisters), command: "fc23");
            Sep(fn);
            WItem(fn, "&Frame Console…", w => OpenTestCenter(), Keys.Alt | Keys.T, command: "test");

            var display = new ToolStripMenuItem("&Display");
            Keys[] fmtKeys = { Keys.Alt | Keys.Shift | Keys.S, Keys.Alt | Keys.Shift | Keys.U, Keys.Alt | Keys.Shift | Keys.H, Keys.Alt | Keys.Shift | Keys.B };
            for (int i = 0; i < DisplayFormat.Presets.Length; i++)
            {
                DisplayFormat f = DisplayFormat.Presets[i];
                _fmtItems[f] = WItem(display, f.Label, w => w.SetDisplay(f), i < fmtKeys.Length ? fmtKeys[i] : Keys.None);
            }
            WItem(display, "Other &Format…", w => w.ChooseFormat());
            WItem(display, "&Interpret As…", ShowInterpret, Keys.Control | Keys.I);
            Sep(display);
            _plcItem = WItem(display, "&PLC Addresses (Base 1)", w => w.SetPlcAddresses(true));
            _protoItem = WItem(display, "P&rotocol Addresses (Base 0)", w => w.SetPlcAddresses(false));
            var monitor = new ToolStripMenuItem("&Monitor");
            Item(monitor, "&Communication Traffic…", ShowTraffic, command: "traffic");
            Item(monitor, "&Trend…", ShowTrend, Keys.Control | Keys.R);
            WItem(monitor, "Add Selection to T&rend", w => AddToTrend(w, w.SelectedOffsets()));
            WItem(monitor, "&Alarm History…", ShowAlarmHistory);
            Sep(monitor);
            WItem(monitor, "&Error Counters", w => { using (var d = new ErrorCountersDialog(w.Engine.Stats(w.PollId))) d.ShowDialog(this); }, Keys.F11);
            WItem(monitor, "R&eset Counters", w => w.ResetCounters(), Keys.F12);
            Sep(monitor);
            _logItem = WItem(monitor, "&Log…", w => w.StartLog(), Keys.Alt | Keys.L);
            _logOffItem = WItem(monitor, "Logging &Off", w => w.StopLog(), Keys.Alt | Keys.O);

            var view = new ToolStripMenuItem("&View");
            var toolbarItem = Item(view, "&Toolbar", () => { _toolbar.Visible = !_toolbar.Visible; });
            toolbarItem.Checked = true;
            view.DropDownOpening += (o, e) => toolbarItem.Checked = _toolbar.Visible;
            var statusItem = Item(view, "&Status Bar", () => { });
            statusItem.Checked = true;
            statusItem.Click += (o, e) => { statusItem.Checked = !statusItem.Checked; _statusBar.Visible = statusItem.Checked; };
            var topItem = Item(view, "&Always On Top", () => { });
            topItem.Click += (o, e) => { topItem.Checked = !topItem.Checked; TopMost = topItem.Checked; };

            var window = new ToolStripMenuItem("&Window");
            Item(window, "&Cascade", () => LayoutMdi(MdiLayout.Cascade));
            Item(window, "Tile &Horizontal", () => LayoutMdi(MdiLayout.TileHorizontal));
            Item(window, "Tile &Vertical", () => LayoutMdi(MdiLayout.TileVertical));
            Item(window, "&Arrange Icons", () => LayoutMdi(MdiLayout.ArrangeIcons));
            ms.MdiWindowListItem = window;

            var help = new ToolStripMenuItem("&Help");
            Item(help, "&Troubleshooting Guide…", ShowGuide, Keys.F1, command: "help");
            Item(help, "&Context Help (click a toolbar command)", () => SetContextHelp(!_contextHelp), Keys.Shift | Keys.F1, command: "context");
            Item(help, "&About MultiPoll Studio…", About);

            ms.Items.AddRange(new ToolStripItem[] { file, edit, conn, device, setup, fn, display, monitor, view, window, help });
            ms.MenuActivate += (o, e) => SyncMenu();
            return ms;
        }

        void SyncMenu()
        {
            PollWindow w = Active;
            foreach (ToolStripItem i in _windowItems) i.Enabled = w != null;
            _disconnectItem.Enabled = Engine.Running || _engines.Values.Any(e => e.Running);
            if (w != null)
            {
                _enabledItem.Checked = w.Doc.Definition.Enabled;
                foreach (var kv in _fmtItems) kv.Value.Checked = kv.Key == w.Doc.View.Display;
                _plcItem.Checked = w.Doc.View.PlcAddresses;
                _protoItem.Checked = !w.Doc.View.PlcAddresses;
                _logItem.Enabled = !w.Logging;
                _logOffItem.Enabled = w.Logging;
            }
            SyncToolbar();
        }

        // -- windows ---------------------------------------------------------------------------

        public PollWindow Active { get { return ActiveMdiChild as PollWindow; } }

        public IEnumerable<PollWindow> Windows { get { return MdiChildren.OfType<PollWindow>(); } }

        PollWindow AddWindow(PollWindow w)
        {
            w.MdiParent = this;
            w.StateChanged += SyncMenu;
            w.ConfirmWrite = (owner, text) => !Confirm.Enabled || Confirm.Ask(owner, text);
            w.ResultApplied += (win, r) => { TrendWin.OnResult(win, r); InterpretWin.WindowUpdated(win); };
            w.InterpretRequested += ShowInterpret;
            w.DiagnoseRequested += Diagnose;
            w.SettingsRequested += OpenDeviceSettings;
            w.Grid.SelectionChanged += (o, e) => InterpretWin.WindowUpdated(w);
            w.TrendRequested += AddToTrend;
            w.AlarmRaised += (win, ev) => _hint.Text = ev.TimestampUtc.ToLocalTime().ToString("HH:mm:ss") + "  " + win.DisplayTitle.TrimEnd(' ', '*') + ": " + ev.Describe();
            w.FormClosed += (o, e) => { TrendWin.RemoveWindow(w); InterpretWin.Forget(w); ReleaseUnusedEngines(); };
            if (w.Doc.Connection != null) w.Rebind(EngineFor(w.Doc.Connection, true));
            w.Show();
            return w;
        }

        public PollWindow NewWindow()
        {
            PollDocument doc = null;
            string raw = RegGetString("DefaultDocument");
            if (raw != null)
            {
                try
                {
                    doc = Persistence.FromDict(Persistence.Parse(raw));
                    doc.Aliases.Clear();
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException) { doc = null; }
            }
            return AddWindow(new PollWindow(Engine, doc));
        }

        void OpenDocument()
        {
            using (var d = new OpenFileDialog { Filter = DocFilter })
                if (d.ShowDialog(this) == DialogResult.OK) OpenPath(d.FileName);
        }

        public PollWindow OpenPath(string path)
        {
            try { return AddWindow(new PollWindow(Engine, Persistence.LoadDocument(path), path)); }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is InvalidOperationException
                                       || ex is FormatException || ex is InvalidCastException || ex is UnauthorizedAccessException)
            {
                Ui.Error(this, new FormatException(path + ": " + ex.Message));
                return null;
            }
        }

        void SaveAs(PollWindow w)
        {
            using (var d = new SaveFileDialog { Filter = DocFilter, FileName = w.Path ?? w.UntitledName + ".mpoll" })
                if (d.ShowDialog(this) == DialogResult.OK) w.Save(d.FileName);
        }

        void SaveWorkspace()
        {
            using (var d = new SaveFileDialog { Filter = WorkspaceFilter, FileName = "workspace.mpollw" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                SaveWorkspace(d.FileName);
            }
        }

        public void SaveWorkspace(string path)
        {
            var windows = Windows.Select(w => (object)new Dictionary<string, object>
            {
                { "document", Persistence.ToDict(w.Doc) },
                { "geometry", new object[] { w.Left, w.Top, w.Width, w.Height } },
            }).ToArray();
            var data = new Dictionary<string, object>
            {
                { "format", "multipoll-workspace" }, { "version", 1 },
                { "connection", Persistence.ToDict(ConnSettings) }, { "windows", windows },
            };
            File.WriteAllText(path, Persistence.Serialize(data));
        }

        void OpenWorkspace()
        {
            using (var d = new OpenFileDialog { Filter = WorkspaceFilter })
                if (d.ShowDialog(this) == DialogResult.OK) LoadWorkspace(d.FileName);
        }

        public void LoadWorkspace(string path)
        {
            List<KeyValuePair<PollDocument, object[]>> docs;
            ConnectionSettings conn;
            try
            {
                var data = Persistence.Parse(File.ReadAllText(path));
                if (!data.ContainsKey("format") || (string)data["format"] != "multipoll-workspace") throw new FormatException("not a MultiPoll Studio workspace");
                docs = ((System.Collections.ArrayList)data["windows"]).Cast<Dictionary<string, object>>()
                    .Select(e => new KeyValuePair<PollDocument, object[]>(
                        Persistence.FromDict((Dictionary<string, object>)e["document"]),
                        e.ContainsKey("geometry") ? ((System.Collections.ArrayList)e["geometry"]).ToArray() : null))
                    .ToList();
                conn = data.ContainsKey("connection")
                    ? Persistence.ConnectionFromDict((Dictionary<string, object>)data["connection"]) : ConnSettings;
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is InvalidOperationException
                                       || ex is FormatException || ex is InvalidCastException || ex is KeyNotFoundException
                                       || ex is NullReferenceException)
            {
                Ui.Error(this, new FormatException(path + ": " + ex.Message));
                return;
            }
            foreach (PollWindow w in Windows.ToList())
            {
                if (!w.MaybeSave()) return;
                w.Dirty = false;
                w.Close();
            }
            ConnSettings = conn;
            foreach (var kv in docs)
            {
                PollWindow w = AddWindow(new PollWindow(Engine, kv.Key));
                if (kv.Value != null && kv.Value.Length == 4)
                    w.SetBounds(Convert.ToInt32(kv.Value[0]), Convert.ToInt32(kv.Value[1]), Convert.ToInt32(kv.Value[2]), Convert.ToInt32(kv.Value[3]));
            }
        }

        void UseAsDefault(PollWindow w)
        {
            RegSet("DefaultDocument", Persistence.Serialize(Persistence.ToDict(w.Doc)));
            SaveConnSettings();
        }

        void Print(PollWindow w)
        {
            using (var doc = new PrintDocument { DocumentName = w.DisplayTitle })
            using (var dlg = new PrintDialog { Document = doc, UseEXDialog = true })
            {
                doc.PrintPage += (o, e) => w.PrintTo(e);
                if (dlg.ShowDialog(this) == DialogResult.OK) doc.Print();
            }
        }

        // -- connection ---------------------------------------------------------------------------

        void ConnectDialog()
        {
            using (var d = new ConnectionDialog(ConnSettings))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                ConnSettings = d.Result();
            }
            SaveConnSettings();
            QuickConnect();
        }

        /// <summary>Connect the main connection and every per-window connection. A failure is shown on the windows'
        /// error lines with "Why? / Fix…" instead of a modal box, so the diagnosis is one click away.</summary>
        public void QuickConnect()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                Engine.Start(ConnSettings);
            }
            catch (ModbusException ex)
            {
                ConnectionChanged(false, ex.Describe());
                foreach (PollWindow w in Windows.Where(x => x.Engine == Engine)) w.ShowFailure(ex.Status, ex.Detail);
                if (!Windows.Any()) Ui.Error(this, ex);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
            foreach (PollWindow w in Windows.Where(x => x.Doc.Connection != null))
                if (!w.Engine.Running) EngineFor(w.Doc.Connection, true);
        }

        /// <summary>Disconnect the main connection and every per-window connection.</summary>
        public void Disconnect()
        {
            foreach (PollEngine e in AllEngines) e.Stop();
            foreach (PollWindow w in Windows) w.SetErrorText(Status.NoConnection.Text());
        }

        void ConnectionChanged(bool up, string text)
        {
            _conn.Text = up ? text : "No connection — " + text;
            if (!up) TrafficWin.Error(text);
            SyncMenu();
        }

        // -- misc ------------------------------------------------------------------------------------

        void OpenTestCenter()
        {
            PollWindow w = Active;
            if (w == null || !w.Engine.Running) return;
            using (var d = new TestCenterDialog(w.Engine))
            {
                d.Text += " — " + TargetText(w);
                d.ShowDialog(this);
            }
        }

        public void ShowTraffic()
        {
            TrafficWin.Show();
            TrafficWin.BringToFront();
        }

        public void ShowTrend()
        {
            TrendWin.Show();
            TrendWin.BringToFront();
        }

        /// <summary>Interpret As panel for the window's selected register (follows selection and new values).</summary>
        public void ShowInterpret(PollWindow w)
        {
            InterpretWin.Show();
            InterpretWin.Track(w);
            InterpretWin.BringToFront();
        }

        public void AddToTrend(PollWindow w, List<int> offsets)
        {
            if (offsets.Count == 0)
            {
                MessageBox.Show(this, "Select one or more value cells first.", "MultiPoll Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int added = TrendWin.Add(w, offsets);
            if (added < offsets.Count && TrendWin.Count >= TrendWindow.MaxSeries)
                MessageBox.Show(this, "The trend shows at most " + TrendWindow.MaxSeries + " values.", "MultiPoll Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ShowTrend();
        }

        void ShowAlarmHistory(PollWindow w)
        {
            string text = w.AlarmHistory.Count == 0 ? "No alarm transitions yet."
                : string.Join("\n", w.AlarmHistory.Skip(Math.Max(0, w.AlarmHistory.Count - 40))
                                     .Select(ev => ev.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + "  " + ev.Describe()));
            MessageBox.Show(this, text, "Alarm history — " + w.DisplayTitle.TrimEnd(' ', '*'), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void SetWriteProtected(bool on)
        {
            foreach (PollEngine e in AllEngines) e.WriteProtected = on;
            RegSet("WriteProtected", on ? 1 : 0);
            SyncProtection();
        }

        void SyncProtection()
        {
            _protect.Text = Engine.WriteProtected ? "WRITE PROTECTED" : Confirm.Enabled ? "Writes: confirm" : "Writes: enabled";
            _protect.ForeColor = Engine.WriteProtected ? Color.FromArgb(0, 110, 0) : Confirm.Enabled ? SystemColors.ControlText : Color.FromArgb(170, 0, 0);
            if (_protectItem != null) _protectItem.Checked = Engine.WriteProtected;
            if (_confirmItem != null) _confirmItem.Checked = Confirm.Enabled;
            SyncToolbar();
        }

        void OpenAuditLog()
        {
            string path = Engine.Audit.Path;
            string broken = AuditLog.Verify(path);
            if (!File.Exists(path))
            {
                MessageBox.Show(this, "No writes have been recorded yet.\n\n" + path, "Write audit log", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            MessageBox.Show(this, (broken == null ? "Hash chain intact." : "WARNING: " + broken) + "\n\n" + path,
                            "Write audit log", MessageBoxButtons.OK, broken == null ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\"");
        }

        void OpenScan()
        {
            if (!Engine.Running)
            {
                Ui.Error(this, new ModbusException(Status.NoConnection));
                return;
            }
            using (var d = new ScanDialog(Engine)) d.ShowDialog(this);
        }

        void About()
        {
            MessageBox.Show(this, "MultiPoll Studio " + Application.ProductVersion + " for Windows\n\nOpen-source Modbus master test tool.\n" +
                                  "Licensed under the GNU GPL v3 or later.", "About MultiPoll Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                foreach (PollWindow w in Windows)
                    if (!w.MaybeSave()) { e.Cancel = true; return; }
                foreach (PollWindow w in Windows) w.Dirty = false; // already asked
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            foreach (PollEngine en in AllEngines) en.Stop();
            base.OnFormClosed(e);
        }
    }
}
