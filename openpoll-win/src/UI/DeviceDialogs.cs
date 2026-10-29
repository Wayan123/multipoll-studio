using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MultiPollStudio.UI
{
    /// <summary>"Why? / Fix…" on a window's error line: likely causes, manual checks and one-click fixes for the last
    /// failure. Fixes change only MultiPoll Studio settings or send reads; they never write to the device.</summary>
    sealed class DiagnoseDialog : Form
    {
        readonly TextBox _text = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = SystemColors.Window };
        readonly FlowLayoutPanel _fixes = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(6) };
        readonly Label _result = new Label { Dock = DockStyle.Bottom, AutoSize = false, Height = 40, Padding = new Padding(8, 4, 8, 4), ForeColor = Color.FromArgb(0, 90, 0) };
        public readonly List<Advice> Advice;
        /// <summary>Runs a fix; returns a one-line outcome shown in the dialog.</summary>
        readonly Func<Advice, Task<string>> _run;

        public DiagnoseDialog(string title, string error, List<Advice> advice, Func<Advice, Task<string>> run)
        {
            Text = "Diagnose — " + title;
            Font = SystemFonts.MessageBoxFont;
            Size = new Size(640, 520);
            MinimumSize = new Size(480, 360);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Advice = advice;
            _run = run;
            var sb = new System.Text.StringBuilder();
            sb.Append("Error: ").Append(error).Append("\r\n\r\n");
            foreach (Advice a in advice)
            {
                sb.Append(a.Title).Append("\r\n");
                if (a.Causes.Length > 0) sb.Append("Likely causes:\r\n");
                foreach (string c in a.Causes) sb.Append("  • ").Append(c).Append("\r\n");
                if (a.Checks.Length > 0) sb.Append("Check by hand:\r\n");
                for (int i = 0; i < a.Checks.Length; i++) sb.Append("  ").Append(i + 1).Append(". ").Append(a.Checks[i]).Append("\r\n");
                sb.Append("\r\n");
            }
            _text.Text = sb.ToString();
            foreach (Advice a in advice.Where(x => x.Fix != FixKind.None))
            {
                Advice captured = a;
                var b = Ui.Button(a.FixLabel, async (o, e) => await RunFix(captured, (Button)o));
                b.AutoSize = true;
                _fixes.Controls.Add(b);
            }
            if (_fixes.Controls.Count == 0) _fixes.Controls.Add(new Label { AutoSize = true, Text = "No automatic fix for this error; follow the checks above." });
            var close = Ui.Button("Close", (o, e) => Close());
            _fixes.Controls.Add(close);
            CancelButton = close;
            Controls.Add(_text);
            Controls.Add(_result);
            Controls.Add(_fixes);
        }

        public string ResultText { get { return _result.Text; } }

        public async Task RunFix(Advice a, Button b = null)
        {
            if (b != null) b.Enabled = false;
            _result.ForeColor = SystemColors.ControlText;
            _result.Text = "Running: " + a.FixLabel + " …";
            try
            {
                string r = await _run(a);
                _result.ForeColor = Color.FromArgb(0, 110, 0);
                _result.Text = r;
            }
            catch (Exception ex)
            {
                _result.ForeColor = Color.FromArgb(170, 0, 0);
                _result.Text = Ui.Describe(ex);
            }
            finally
            {
                if (b != null && !b.IsDisposed) b.Enabled = true;
            }
        }
    }

    /// <summary>Find a serial device whose baud, parity and ID are unknown (read-only). Stops the connection that owns
    /// the port while it runs; "Use" applies the found settings to that connection.</summary>
    sealed class FindDeviceDialog : Form
    {
        readonly ComboBox _port;
        readonly CheckedListBox _bauds = new CheckedListBox { Width = 120, Height = 150, CheckOnClick = true, IntegralHeight = false };
        readonly CheckBox _pN = new CheckBox { Text = "None", Checked = true, AutoSize = true }, _pE = new CheckBox { Text = "Even", Checked = true, AutoSize = true },
                          _pO = new CheckBox { Text = "Odd", Checked = true, AutoSize = true }, _stop2 = new CheckBox { Text = "also 8N2", AutoSize = true };
        readonly NumericUpDown _from = Ui.Num(1, 255, 1, 60), _to = Ui.Num(1, 255, 247, 60), _addr = Ui.Num(0, 65535, 0, 80), _timeout = Ui.Num(0, 5000, 0, 70);
        readonly ComboBox _fc = Ui.Combo(new object[] { "03 Holding register", "04 Input register" }, width: 160);
        readonly RadioButton _quick = new RadioButton { Text = "Quick: IDs 1, 2, 3, 247, 254 at each setting", AutoSize = true, Checked = true };
        readonly RadioButton _full = new RadioButton { Text = "Full: every ID in the range", AutoSize = true };
        readonly CheckBox _stopFirst = new CheckBox { Text = "Stop at the first device", AutoSize = true, Checked = true };
        readonly ListBox _results = new ListBox { Width = 460, Height = 140, IntegralHeight = false };
        readonly Label _progress = new Label { AutoSize = true, MaximumSize = new Size(460, 0) };
        readonly Label _estimate = new Label { AutoSize = true, ForeColor = SystemColors.GrayText };
        readonly Button _start, _use;
        readonly ConnectionSettings _base;
        CancellationTokenSource _cts;
        public readonly List<LineHit> Hits = new List<LineHit>();
        /// <summary>Stop/restart the engine that owns the port (null when no engine uses it).</summary>
        public Func<string, bool> ReleasePort = p => true;
        public Action<string> RestorePort = p => { };
        /// <summary>Profiles to identify a hit by their identify register (optional).</summary>
        public List<DeviceProfile> Profiles = new List<DeviceProfile>();
        /// <summary>Set when the user clicks "Use": the hit to apply.</summary>
        public LineHit Chosen;

        public FindDeviceDialog(ConnectionSettings current)
        {
            Text = "Find Device (baud / parity / ID, read-only)";
            _base = current.Clone();
            if (_base.Kind != ConnectionKind.SerialOverTcp) _base.Kind = ConnectionKind.Serial; // RTU over TCP: simulator/gateway tests
            var ports = SerialPort.GetPortNames().OrderBy(p => p.Length).ThenBy(p => p).Cast<object>().ToList();
            _port = Ui.Combo(ports, 0, editable: true, width: 110);
            if (current.Kind == ConnectionKind.Serial) _port.Text = current.Port;
            foreach (int b in new[] { 1200, 2400, 4800, 9600, 14400, 19200, 38400, 57600, 115200 })
                _bauds.Items.Add(b, LineSearchOptions.CommonBauds.Contains(b));
            _start = Ui.Button("&Start", (o, e) => StartOrStop());
            _use = Ui.Button("&Use selected", (o, e) => UseSelected());
            _use.Enabled = false;
            var close = Ui.Button("Close", null);
            close.DialogResult = DialogResult.Cancel;
            CancelButton = close;
            var note = new Label
            {
                AutoSize = true, MaximumSize = new Size(460, 0), ForeColor = Color.FromArgb(160, 60, 0),
                Text = "Sends single reads only. Connect one device while searching: two unknown devices on one bus answer at the same time. " +
                       "The port is released from its connection while the search runs.",
            };
            var parities = Ui.Row(_pN, _pE, _pO, _stop2);
            var form = Ui.Form("Port:", _port, "Baud rates:", _bauds, "Parity:", parities, "From ID:", _from, "To ID:", _to, null, _quick, null, _full,
                               "Read:", _fc, "Register (base 0):", _addr, "Probe timeout [ms] (0 = auto):", _timeout, null, _stopFirst, null, _estimate);
            Ui.Stack(this, new Control[] { note, form, _progress, _results }, _start, _use, close);
            foreach (Control c in new Control[] { _pN, _pE, _pO, _stop2, _quick, _full }) ((ButtonBase)c).Click += (o, e) => UpdateEstimate();
            _bauds.ItemCheck += (o, e) => BeginInvoke((Action)UpdateEstimate);
            _from.ValueChanged += (o, e) => UpdateEstimate();
            _to.ValueChanged += (o, e) => UpdateEstimate();
            _results.SelectedIndexChanged += (o, e) => _use.Enabled = _results.SelectedIndex >= 0;
            _results.DoubleClick += (o, e) => UseSelected();
            FormClosing += (o, e) => { if (_cts != null) _cts.Cancel(); };
            UpdateEstimate();
        }

        public LineSearchOptions Options()
        {
            var parities = new List<Parity>();
            if (_pN.Checked) parities.Add(Parity.None);
            if (_pE.Checked) parities.Add(Parity.Even);
            if (_pO.Checked) parities.Add(Parity.Odd);
            int[] bauds = _bauds.CheckedItems.Cast<int>().OrderBy(b => Array.IndexOf(LineSearchOptions.CommonBauds, b) < 0 ? 99 : Array.IndexOf(LineSearchOptions.CommonBauds, b)).ToArray();
            return new LineSearchOptions
            {
                Bauds = bauds, Parities = parities.ToArray(), TryTwoStopBits = _stop2.Checked, FromId = (int)_from.Value, ToId = (int)_to.Value,
                Function = _fc.SelectedIndex == 1 ? 4 : 3, Address = (int)_addr.Value, TimeoutMs = (int)_timeout.Value,
                StopAtFirst = _stopFirst.Checked, FullRange = _full.Checked,
            };
        }

        void UpdateEstimate()
        {
            LineSearchOptions o = Options();
            if (o.Bauds.Length == 0 || o.Parities.Length == 0 || o.FromId > o.ToId) { _estimate.Text = "Select at least one baud rate and parity."; return; }
            TimeSpan t = LineSearch.Estimate(o);
            _estimate.Text = LineSearch.Settings(o).Count + " line settings × " + LineSearch.Ids(o).Count + " IDs; at most "
                             + (t.TotalMinutes >= 1 ? t.TotalMinutes.ToString("0.#") + " min" : t.TotalSeconds.ToString("0") + " s") + " if nothing answers";
        }

        /// <summary>Set the search for tests: bauds, parities, ID range, quick/full.</summary>
        public void Configure(string port, int[] bauds, bool parityNone, bool parityEven, bool parityOdd, int from, int to, bool full, int timeoutMs)
        {
            _port.Text = port;
            for (int i = 0; i < _bauds.Items.Count; i++) _bauds.SetItemChecked(i, bauds.Contains((int)_bauds.Items[i]));
            _pN.Checked = parityNone; _pE.Checked = parityEven; _pO.Checked = parityOdd;
            _from.Value = from; _to.Value = to;
            _full.Checked = full; _quick.Checked = !full;
            _timeout.Value = timeoutMs;
            UpdateEstimate();
        }

        public Task Running { get; private set; }

        public void StartOrStop()
        {
            if (_cts != null) { _cts.Cancel(); return; }
            Running = Search();
        }

        async Task Search()
        {
            LineSearchOptions o = Options();
            if (o.Bauds.Length == 0 || o.Parities.Length == 0) { Ui.Error(this, new FormatException("Select at least one baud rate and parity")); return; }
            string port = _port.Text.Trim();
            if (!ReleasePort(port)) return;
            ConnectionSettings s = _base.Clone();
            s.Port = port;
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            _start.Text = "&Stop";
            _results.Items.Clear();
            Hits.Clear();
            List<DeviceProfile> profiles = Profiles.Where(p => p.IdentifyFunction != 0).ToList();
            try
            {
                List<LineHit> hits = await Task.Run(() => LineSearch.Run(s, o,
                    text => Post(() => _progress.Text = text),
                    hit =>
                    {
                        hit.Identified = Identify(s, hit, profiles);
                        Post(() => { Hits.Add(hit); _results.Items.Add(hit.Line); if (_results.SelectedIndex < 0) _results.SelectedIndex = 0; });
                    }, token));
                _progress.Text = (token.IsCancellationRequested ? "Stopped. " : "Done. ") + hits.Count + " device(s) found."
                                 + (hits.Count == 0 ? " Check wiring (A/B), power and that the device is RS-485 Modbus RTU." : " Select one and click Use.");
            }
            catch (Exception ex)
            {
                _progress.Text = Ui.Describe(ex);
            }
            finally
            {
                _cts = null;
                if (!IsDisposed) _start.Text = "&Start";
                RestorePort(port);
            }
        }

        /// <summary>Read each candidate profile's identify register at the hit's line settings (read-only).</summary>
        static string Identify(ConnectionSettings s, LineHit hit, List<DeviceProfile> profiles)
        {
            if (profiles.Count == 0) return "";
            ConnectionSettings c = s.Clone();
            c.BaudRate = hit.Baud; c.Parity = hit.Parity; c.StopBits = hit.StopBits; c.ResponseTimeoutMs = LineSearch.ProbeTimeoutMs(hit.Baud);
            var names = new List<string>();
            try
            {
                using (var m = new ModbusMaster(c))
                {
                    m.Connect();
                    foreach (var g in profiles.GroupBy(p => Tuple.Create(p.IdentifyFunction, p.IdentifyRegister)))
                    {
                        int value;
                        try { value = m.Transact(hit.SlaveId, Pdu.ReadRequest(g.Key.Item1, g.Key.Item2, 1))[0]; }
                        catch (ModbusException) { continue; }
                        foreach (DeviceProfile p in g)
                        {
                            string label;
                            if (p.IdentifyValues.TryGetValue(value, out label) && !names.Contains(label)) names.Add(label);
                        }
                    }
                }
            }
            catch (ModbusException) { }
            return string.Join(", ", names);
        }

        void Post(Action a)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(a); } catch (InvalidOperationException) { }
        }

        void UseSelected()
        {
            if (_results.SelectedIndex < 0) return;
            Chosen = Hits[_results.SelectedIndex];
            DialogResult = DialogResult.OK;
        }

        public void Select(int i) { _results.SelectedIndex = i; }
        public string ProgressText { get { return _progress.Text; } }
        public string Port { get { return _port.Text.Trim(); } }
    }

    /// <summary>Connection for one window (own COM port or TCP host), or the main connection.</summary>
    sealed class WindowConnectionDialog : Form
    {
        readonly RadioButton _main = new RadioButton { Text = "Use the main connection (Connection → Connect)", AutoSize = true };
        readonly RadioButton _own = new RadioButton { Text = "Use its own connection:", AutoSize = true };
        readonly Label _ownText = new Label { AutoSize = true, ForeColor = SystemColors.GrayText };
        ConnectionSettings _settings;
        public ConnectionSettings Result;

        public WindowConnectionDialog(ConnectionSettings own, ConnectionSettings main)
        {
            Text = "Window Connection";
            _settings = (own ?? main ?? new ConnectionSettings()).Clone();
            (own != null ? _own : _main).Checked = true;
            var edit = Ui.Button("&Edit…", (o, e) =>
            {
                using (var d = new ConnectionDialog(_settings))
                    if (d.ShowDialog(this) == DialogResult.OK) { _settings = d.Result(); _own.Checked = true; Sync(); }
            });
            var info = new Label
            {
                AutoSize = true, MaximumSize = new Size(420, 0),
                Text = "Windows with the same port or host share one connection and poll in turn. Different ports poll in parallel.",
            };
            var ok = Ui.Button("OK", (o, e) => { Result = _own.Checked ? _settings : null; DialogResult = DialogResult.OK; });
            var cancel = Ui.Button("Cancel", null);
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok;
            CancelButton = cancel;
            Ui.Stack(this, new Control[] { _main, _own, Ui.Row(_ownText, edit), info }, ok, cancel);
            Sync();
        }

        void Sync() { _ownText.Text = _settings.Describe(); }
    }

    /// <summary>Add a known device: pick a profile, slave ID and connection; opens one poll window per register block.</summary>
    sealed class AddDeviceDialog : Form
    {
        readonly ListBox _list = new ListBox { Width = 300, Height = 260, IntegralHeight = false };
        readonly TextBox _info = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Width = 380, Height = 260, BackColor = SystemColors.Window };
        readonly NumericUpDown _slave = Ui.Num(1, 255, 1, 70);
        readonly CheckBox _applyLine = new CheckBox { Text = "Set the connection's baud/parity to the profile defaults", AutoSize = true };
        readonly CheckBox _settings = new CheckBox { Text = "Open Device Settings", AutoSize = true };
        readonly List<DeviceProfile> _profiles;
        public DeviceProfile Profile { get { return _list.SelectedIndex >= 0 ? _profiles[_list.SelectedIndex] : null; } }
        public int SlaveId { get { return (int)_slave.Value; } }
        public bool ApplyLine { get { return _applyLine.Checked; } }
        public bool OpenSettings { get { return _settings.Checked; } }

        public AddDeviceDialog(List<DeviceProfile> profiles, IEnumerable<string> loadErrors)
        {
            Text = "Add Device from Profile";
            _profiles = profiles;
            foreach (DeviceProfile p in profiles) _list.Items.Add(p.ToString() + (p.Status == "observed" ? "" : "  (partial)"));
            _list.SelectedIndexChanged += (o, e) => Show();
            var ok = Ui.Button("&Add", (o, e) => { if (Profile != null) DialogResult = DialogResult.OK; });
            var cancel = Ui.Button("Cancel", null);
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok;
            CancelButton = cancel;
            var errors = loadErrors.ToList();
            var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true };
            row.Controls.Add(_list, 0, 0);
            row.Controls.Add(_info, 1, 0);
            var controls = new List<Control> { row, Ui.Form("Slave ID:", _slave, null, _applyLine, null, _settings),
                new Label { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(680, 0),
                            Text = "Own profiles: put *.json files in " + MultiPollStudio.Profiles.UserFolder() + " (same format as the built-in ones, see README)." } };
            if (errors.Count > 0)
                controls.Add(new Label { AutoSize = true, ForeColor = Color.FromArgb(170, 0, 0), MaximumSize = new Size(680, 0), Text = "Skipped: " + string.Join("; ", errors.Take(3)) });
            Ui.Stack(this, controls.ToArray(), ok, cancel);
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        public void Choose(string id) { _list.SelectedIndex = _profiles.FindIndex(p => p.Id == id); }

        new void Show()
        {
            DeviceProfile p = Profile;
            if (p == null) return;
            _slave.Value = Math.Max(1, Math.Min(255, p.SlaveId));
            var sb = new System.Text.StringBuilder();
            sb.Append(p.Title).Append("\r\n").Append(p.Category).Append(" · default ").Append(p.LineText).Append(", ID ").Append(p.SlaveId)
              .Append(p.BaudFixed ? " (baud fixed)" : "").Append("\r\n\r\n");
            if (p.Notes.Length > 0) sb.Append(p.Notes).Append("\r\n\r\n");
            foreach (ProfileBlock b in p.Blocks)
                sb.Append("• ").Append(b.Name).Append(": FC").Append(b.Function.ToString("D2")).Append(" ").Append(b.Address).Append("..").Append(b.Address + b.Quantity - 1)
                  .Append(" (").Append(b.Points.Count).Append(" values)\r\n");
            if (p.Settings.Count > 0) sb.Append("\r\nSettings: ").Append(string.Join(", ", p.Settings.Select(s => s.Name + (s.Verified ? "" : " (unverified)")))).Append("\r\n");
            if (p.Status != "observed") sb.Append("\r\nPartial profile: some facts come from third-party copies of the manual or are unverified; check before relying on them.\r\n");
            sb.Append("\r\nSources:\r\n");
            foreach (var s in p.Sources) sb.Append("  ").Append(s.Key).Append(" — ").Append(s.Value).Append("\r\n");
            _info.Text = sb.ToString();
        }
    }

    /// <summary>Vendor-tool style device configuration: shows the profile's settings with their current values and
    /// sets them with one click (the user never types a register or function code). Each write goes through write
    /// protection, confirmation and the audit log; serial settings that the device takes over after a restart are
    /// applied to the connection and verified by reading the device back at the new settings.</summary>
    sealed class DeviceSettingsDialog : Form
    {
        sealed class Row
        {
            public ProfileSetting Setting;
            public Label Current;
            public Control Editor;
            public Button Set;
        }

        readonly DeviceProfile _p;
        readonly MainForm _main;
        readonly PollWindow _w;
        readonly List<Row> _rows = new List<Row>();
        readonly ListBox _log = new ListBox { Width = 560, Height = 120, IntegralHeight = false, HorizontalScrollbar = true };
        readonly Label _target = new Label { AutoSize = true };
        readonly NumericUpDown _slave;
        /// <summary>Waits for the device to restart after a setting write (ms); tests shorten it.</summary>
        public int RestartWaitMs = 2500;

        public DeviceSettingsDialog(MainForm main, PollWindow w, DeviceProfile p)
        {
            _main = main;
            _w = w;
            _p = p;
            Text = "Device Settings — " + p.Title;
            _slave = Ui.Num(1, 255, w.Doc.Definition.SlaveId, 70);
            var grid = new TableLayoutPanel { ColumnCount = 4, AutoSize = true, Dock = DockStyle.Top };
            grid.Controls.Add(Bold("Setting"), 0, 0);
            grid.Controls.Add(Bold("Device now"), 1, 0);
            grid.Controls.Add(Bold("New value"), 2, 0);
            int r = 1;
            foreach (ProfileSetting s in p.Settings)
            {
                var row = new Row { Setting = s, Current = new Label { AutoSize = true, Text = s.ReadFunction == 0 ? "(not readable)" : "?", Margin = new Padding(3, 7, 12, 3) } };
                if (s.Choices.Count > 0) row.Editor = Ui.Combo(s.Choices.Cast<object>(), 0, width: 170);
                else row.Editor = Ui.Num((int)Math.Max(int.MinValue, s.Min ?? 0), (int)Math.Min(int.MaxValue, s.Max ?? 65535), (int)(s.Min ?? 0), 90);
                row.Set = Ui.Button("Set", async (o, e) => await SetAsync(row));
                row.Set.Enabled = s.WriteFunction != 0;
                var label = new Label { AutoSize = true, Text = s.Name + (s.Verified ? "" : " *"), Margin = new Padding(3, 7, 3, 3) };
                if (!s.Verified || s.Note.Length > 0) new ToolTip().SetToolTip(label, (s.Verified ? "" : "Not verified in the manufacturer manual. ") + s.Note);
                grid.Controls.Add(label, 0, r);
                grid.Controls.Add(row.Current, 1, r);
                grid.Controls.Add(row.Editor, 2, r);
                grid.Controls.Add(row.Set, 3, r);
                _rows.Add(row);
                r++;
            }
            var actions = Ui.Row();
            foreach (ProfileAction a in p.Actions)
            {
                ProfileAction captured = a;
                actions.Controls.Add(Ui.Button(a.Name + (a.Verified ? "" : " *") + "…", async (o, e) => await RunAction(captured)));
            }
            if (p.Recovery != null) actions.Controls.Add(Ui.Button("&Find forgotten ID…", async (o, e) => await RecoverAsync()));
            var notes = new Label
            {
                AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = SystemColors.GrayText,
                Text = (p.BeforeWrite.Length > 0 ? "Before changing settings: " + p.BeforeWrite + "\n" : "")
                       + (p.Settings.Any(s => !s.Verified) || p.Actions.Any(a => !a.Verified) ? "* not verified in the manufacturer manual.\n" : "")
                       + "Every change is confirmed, recorded in the write audit log and refused while write protection is on.",
            };
            var refresh = Ui.Button("&Read all", async (o, e) => await ReadAllAsync());
            var close = Ui.Button("Close", null);
            close.DialogResult = DialogResult.Cancel;
            CancelButton = close;
            Ui.Stack(this, new Control[] { Ui.Form("Slave ID now:", _slave, null, _target), grid, actions, notes, Ui.Group("Log", _log) }, refresh, close);
            UpdateTarget();
            Shown += async (o, e) => await ReadAllAsync();
        }

        static Label Bold(string t) { return new Label { Text = t, AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold), Margin = new Padding(3, 3, 12, 3) }; }

        PollEngine Engine { get { return _w.Engine; } }
        int Slave { get { return (int)_slave.Value; } }

        void UpdateTarget() { _target.Text = _w.ConnectionText; }

        public void Log(string text)
        {
            _log.Items.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + text);
            _log.TopIndex = Math.Max(0, _log.Items.Count - 1);
        }

        public IEnumerable<string> LogLines { get { return _log.Items.Cast<string>(); } }
        public string CurrentText(string key) { return _rows.First(x => x.Setting.Key == key).Current.Text; }

        public void Choose(string key, double value)
        {
            Row row = _rows.First(x => x.Setting.Key == key);
            var combo = row.Editor as ComboBox;
            if (combo != null) combo.SelectedIndex = row.Setting.Choices.FindIndex(c => c.Value == value);
            else ((NumericUpDown)row.Editor).Value = (decimal)value;
        }

        double NewValue(Row row)
        {
            var combo = row.Editor as ComboBox;
            return combo != null ? ((SettingChoice)combo.SelectedItem).Value : (double)((NumericUpDown)row.Editor).Value;
        }

        public async Task ReadAllAsync()
        {
            if (!Engine.Running) { Log("Not connected: " + Status.NoConnection.Text()); return; }
            foreach (Row row in _rows.Where(x => x.Setting.ReadFunction != 0))
            {
                try
                {
                    ProfileSetting s = row.Setting;
                    int slave = Slave;
                    double v = (double)await Engine.Submit(m => DeviceOps.Read(m, slave, s));
                    row.Current.Text = s.Describe(v);
                    var combo = row.Editor as ComboBox;
                    if (combo != null) { int i = s.Choices.FindIndex(c => c.Value == v); if (i >= 0) combo.SelectedIndex = i; }
                    else
                    {
                        var n = (NumericUpDown)row.Editor;
                        n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, (decimal)v));
                    }
                }
                catch (Exception ex)
                {
                    row.Current.Text = "error";
                    Log(row.Setting.Name + ": " + Ui.Describe(ex));
                    if (ex is ModbusException && ((ModbusException)ex).Status == Status.Timeout) break; // device absent: do not wait for every row
                }
            }
        }

        public Task SetAsync(string key) { return SetAsync(_rows.First(x => x.Setting.Key == key)); }

        async Task SetAsync(Row row)
        {
            ProfileSetting s = row.Setting;
            double value = NewValue(row);
            byte[] pdu;
            try { pdu = s.WritePdu(value); }
            catch (FormatException ex) { Ui.Error(this, ex); return; }
            int slave = Slave;
            string what = s.Name + " → " + s.Describe(value) + " (slave " + slave + ")";
            string apply = s.Apply == SettingApply.PowerCycle ? "\n\nThe device uses the new value after a power cycle."
                         : s.Apply == SettingApply.AutoRestart ? "\n\nThe device saves and restarts by itself." : "";
            string warn = s.Verified ? "" : "\n\nThis setting is not verified in the manufacturer manual.";
            if (_p.BeforeWrite.Length > 0) warn += "\n\nFirst: " + _p.BeforeWrite;
            if (Confirm.Enabled && !Confirm.Ask(this, "Set " + what + apply + warn)) return;
            row.Set.Enabled = false;
            try
            {
                await Engine.Write(slave, pdu);
                Log("Set " + what + ": response ok");
                await FollowUp(s, value);
            }
            catch (WriteBlockedException ex) { Log(ex.Message); }
            catch (Exception ex)
            {
                Log("Set " + what + ": " + Ui.Describe(ex));
                ModbusException me = ex as ModbusException;
                if (me != null && (int)me.Status >= 0x80 && _p.BeforeWrite.Length > 0) Log("The device refused the write. " + _p.BeforeWrite);
            }
            finally { if (!row.Set.IsDisposed) row.Set.Enabled = true; }
        }

        /// <summary>After a write: move the window (and the connection) to the new ID / line settings and verify by
        /// reading the setting back from the device at its new address.</summary>
        async Task FollowUp(ProfileSetting s, double value)
        {
            SettingChoice choice = s.Choices.FirstOrDefault(c => c.Value == value);
            bool lineChange = choice != null && (choice.Baud.HasValue || choice.Parity.HasValue || choice.StopBits.HasValue);
            if (s.Role == SettingRole.SlaveId)
            {
                int id = (int)value;
                _slave.Value = id;
                _main.SetWindowsSlave(_w, id);
                Log("Windows of this device now poll slave " + id + ".");
            }
            if (lineChange)
            {
                ConnectionSettings c = _main.ConnectionOf(_w).Clone();
                if (choice.Baud.HasValue) c.BaudRate = choice.Baud.Value;
                if (choice.Parity.HasValue) c.Parity = choice.Parity.Value;
                if (choice.StopBits.HasValue) c.StopBits = choice.StopBits.Value;
                if (s.Apply == SettingApply.PowerCycle)
                {
                    Log("Power-cycle the device, then click OK to switch the connection to " + c.BaudRate + " " + LineSearch.ParityLetter(c.Parity) + ".");
                    if (!Ui.Ask(this, "Power-cycle the device now, then click OK to switch the connection to the new settings.", "Device Settings", MessageBoxIcon.Information)) return;
                }
                _main.ChangeConnection(_w, c);
                Log("Connection changed to " + c.Describe() + ".");
                UpdateTarget();
            }
            else if (s.Apply == SettingApply.PowerCycle)
            {
                Log("Power-cycle the device to use the new value.");
                return;
            }
            if (s.ReadFunction == 0) return;
            int slave = Slave;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                await Task.Delay(attempt == 0 && s.Apply == SettingApply.AutoRestart ? RestartWaitMs : 700);
                try
                {
                    double back = (double)await Engine.Submit(m => DeviceOps.Read(m, slave, s));
                    Row row = _rows.First(x => x.Setting == s);
                    row.Current.Text = s.Describe(back);
                    Log(back == value ? "Verified: the device reports " + s.Describe(back) + "." : "Read back " + s.Describe(back) + ", expected " + s.Describe(value) + ".");
                    return;
                }
                catch (Exception ex) when (ex is ModbusException || ex is FormatException) { if (attempt == 2) Log("Read-back failed: " + Ui.Describe(ex) + ". Use Find Device if the device is gone."); }
            }
        }

        public Task RunAction(string key) { return RunAction(_p.Actions.First(a => a.Key == key)); }

        async Task RunAction(ProfileAction a)
        {
            int slave = Slave;
            string text = (a.Confirm.Length > 0 ? a.Confirm : "Run " + a.Name + "?") + "\n\nSlave " + slave + (a.Verified ? "" : "\n\nThis action is not verified in the manufacturer manual.");
            if (!Ui.Ask(this, text, a.Name, MessageBoxIcon.Warning)) return;
            try
            {
                foreach (ActionStep step in a.Steps)
                {
                    if (step.RawPdu != null)
                    {
                        if (Engine.WriteProtected) throw new WriteBlockedException();
                        byte[] pdu = step.RawPdu;
                        if (Engine.Audit != null) Engine.Audit.Record(Engine.Settings.Describe(), slave, pdu[0], -1, null, null, "ACTION " + a.Key + " " + Hex.Format(pdu));
                        await Engine.Submit(m => DeviceOps.Custom(m, slave, pdu));
                    }
                    else await Engine.Write(slave, step.Pdu());
                }
                Log(a.Name + ": response ok");
                if (a.Key == "factory_reset")
                {
                    Log("Factory defaults: " + _p.LineText + ", ID " + _p.SlaveId + ". Switching the window to them.");
                    _slave.Value = _p.SlaveId;
                    _main.SetWindowsSlave(_w, _p.SlaveId);
                    ConnectionSettings c = _main.ConnectionOf(_w).Clone();
                    c.BaudRate = _p.Baud; c.Parity = _p.Parity; c.StopBits = _p.StopBits; c.DataBits = _p.DataBits;
                    if (!c.Kind.UsesMbap()) _main.ChangeConnection(_w, c);
                    await Task.Delay(RestartWaitMs);
                    await ReadAllAsync();
                }
            }
            catch (Exception ex) { Log(a.Name + ": " + Ui.Describe(ex)); }
        }

        public async Task<int?> RecoverAsync(bool ask = true)
        {
            ProfileRecovery r = _p.Recovery;
            if (ask && !Ui.Ask(this, "Only one device may be connected to the bus for this query (address " + r.Address + ")." + (r.Verified ? "" : "\n\n" + r.Note),
                               "Find forgotten ID", MessageBoxIcon.Information)) return null;
            try
            {
                int id = (int)await Engine.Submit(m => (object)DeviceOps.Recover(m, r));
                Log("The device reports slave ID " + id + ".");
                _slave.Value = Math.Max(1, Math.Min(255, id));
                _main.SetWindowsSlave(_w, id);
                await ReadAllAsync();
                return id;
            }
            catch (Exception ex)
            {
                Log("No answer at address " + r.Address + ": " + Ui.Describe(ex) + ". Try Find Device (scans baud and ID).");
                return null;
            }
        }
    }
}
