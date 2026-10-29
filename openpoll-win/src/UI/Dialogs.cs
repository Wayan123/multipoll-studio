using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MultiPollStudio.UI
{
    /// <summary>Small layout helpers so dialogs stay declarative without a designer.</summary>
    static class Ui
    {
        public static NumericUpDown Num(int min, int max, int value, int width = 90)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), Width = width };
        }

        public static ComboBox Combo(IEnumerable<object> items, int selected = 0, bool editable = false, int width = 200)
        {
            var c = new ComboBox { DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList, Width = width };
            foreach (object i in items) c.Items.Add(i);
            if (c.Items.Count > 0) c.SelectedIndex = Math.Max(0, Math.Min(selected, c.Items.Count - 1));
            return c;
        }

        public static TableLayoutPanel Form(params object[] labelControlPairs)
        {
            var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < labelControlPairs.Length; i += 2)
            {
                var label = labelControlPairs[i] as string;
                var control = (Control)labelControlPairs[i + 1];
                control.Margin = new Padding(3, 3, 3, 3);
                if (label != null)
                    t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) });
                t.Controls.Add(control);
                if (label == null) t.SetColumnSpan(control, 2);
            }
            return t;
        }

        public static GroupBox Group(string title, Control content)
        {
            var g = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Padding = new Padding(6) };
            content.Dock = DockStyle.Fill;
            g.Controls.Add(content);
            return g;
        }

        public static FlowLayoutPanel Row(params Control[] controls)
        {
            var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Dock = DockStyle.Top };
            f.Controls.AddRange(controls);
            return f;
        }

        public static Button Button(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(80, 26) };
            if (click != null) b.Click += click;
            return b;
        }

        /// <summary>Vertical stack: rows top to bottom, then a right-aligned button bar.</summary>
        public static void Stack(Form f, Control[] rows, params Button[] buttons)
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true, Padding = new Padding(8) };
            foreach (Control r in rows) root.Controls.Add(r);
            var bar = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
            foreach (Button b in buttons.Reverse()) bar.Controls.Add(b);
            root.Controls.Add(bar);
            f.Controls.Add(root);
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.StartPosition = FormStartPosition.CenterParent;
            f.ShowInTaskbar = false;
            f.Font = SystemFonts.MessageBoxFont;
        }

        public static void Error(IWin32Window owner, Exception ex)
        {
            Exception e = ex is AggregateException ? ((AggregateException)ex).InnerException : ex;
            var me = e as ModbusException;
            MessageBox.Show(owner, me != null ? me.Describe() : e.Message, "MultiPoll Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public static string Describe(Exception ex)
        {
            Exception e = ex is AggregateException ? ((AggregateException)ex).InnerException : ex;
            var me = e as ModbusException;
            return me != null ? me.Describe() : e.Message;
        }

        public static Font Mono() { return new Font(FontFamily.GenericMonospace, 9f); }

        /// <summary>OK/Cancel question. Unattended tests set <see cref="AutoAnswer"/> to answer without a modal box.</summary>
        public static bool Ask(IWin32Window owner, string text, string title, MessageBoxIcon icon = MessageBoxIcon.Question)
        {
            if (AutoAnswer != null) return AutoAnswer(text);
            return MessageBox.Show(owner, text, title, MessageBoxButtons.OKCancel, icon, MessageBoxDefaultButton.Button2) == DialogResult.OK;
        }

        /// <summary>Test hook for <see cref="Ask"/>: receives the question text, returns the answer.</summary>
        public static Func<string, bool> AutoAnswer;

        /// <summary>The ten Modbus Poll presets, plus <paramref name="current"/> when it is a custom format.</summary>
        public static object[] FormatChoices(DisplayFormat current)
        {
            var list = DisplayFormat.Presets.Cast<object>().ToList();
            if (Array.IndexOf(DisplayFormat.Presets, current) < 0) list.Add(current);
            return list.ToArray();
        }

        public static int FormatIndex(DisplayFormat current)
        {
            int i = Array.IndexOf(DisplayFormat.Presets, current);
            return i >= 0 ? i : DisplayFormat.Presets.Length;
        }
    }

    // --- Connection ----------------------------------------------------------------

    sealed class ConnectionDialog : Form
    {
        readonly ComboBox _kind, _port, _baud, _databits, _parity, _stopbits;
        readonly RadioButton _rtu, _ascii;
        readonly TextBox _host;
        readonly NumericUpDown _tcpPort, _connectTimeout, _responseTimeout, _delay, _rtsDelay;
        readonly CheckBox _rtsToggle, _dsr, _cts, _echo;
        readonly GroupBox _serialBox, _netBox, _modeBox, _advBox;

        static readonly Parity[] Parities = { Parity.None, Parity.Even, Parity.Odd };

        public ConnectionDialog(ConnectionSettings s)
        {
            Text = "Connection Setup";
            _kind = Ui.Combo(ConnectionKindInfo.Labels, (int)s.Kind, width: 260);
            var ports = SerialPort.GetPortNames().OrderBy(p => p.Length).ThenBy(p => p).ToList();
            _port = Ui.Combo(ports.Cast<object>(), 0, editable: true, width: 110);
            _port.Text = s.Port;
            _baud = Ui.Combo(new object[] { 300, 600, 1200, 2400, 4800, 9600, 14400, 19200, 38400, 57600, 115200, 230400 }, 5, true, 110);
            _baud.Text = s.BaudRate.ToString(CultureInfo.InvariantCulture);
            _databits = Ui.Combo(new object[] { 7, 8 }, s.DataBits == 7 ? 0 : 1, width: 110);
            _parity = Ui.Combo(new object[] { "None", "Even", "Odd" }, Math.Max(0, Array.IndexOf(Parities, s.Parity)), width: 110);
            _stopbits = Ui.Combo(new object[] { 1, 2 }, s.StopBits == StopBits.Two ? 1 : 0, width: 110);
            _serialBox = Ui.Group("Serial Settings", Ui.Form("Port:", _port, "Baud:", _baud, "Data bits:", _databits,
                                                           "Parity:", _parity, "Stop bits:", _stopbits));
            _rtu = new RadioButton { Text = "RTU", AutoSize = true, Checked = s.Mode == SerialMode.Rtu };
            _ascii = new RadioButton { Text = "ASCII", AutoSize = true, Checked = s.Mode == SerialMode.Ascii };
            _modeBox = Ui.Group("Mode", Ui.Row(_rtu, _ascii));
            _host = new TextBox { Text = s.Host, Width = 150 };
            _tcpPort = Ui.Num(1, 65535, s.TcpPort);
            _connectTimeout = Ui.Num(100, 60000, s.ConnectTimeoutMs);
            _netBox = Ui.Group("Remote Server", Ui.Form("IP Address:", _host, "Port:", _tcpPort, "Connect Timeout [ms]:", _connectTimeout));
            _responseTimeout = Ui.Num(10, 60000, s.ResponseTimeoutMs);
            _delay = Ui.Num(0, 60000, s.DelayBetweenPollsMs);
            var suggest = Ui.Button("Suggest", (o, e) =>
            {
                int baud;
                if (!int.TryParse(_baud.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out baud) || baud <= 0) return;
                var mode = _rtu.Checked ? SerialMode.Rtu : SerialMode.Ascii;
                int ms = SerialTiming.SuggestTimeoutMs(baud, 3, Pdu.MaxReadRegisters, mode);
                _responseTimeout.Value = Math.Max(_responseTimeout.Minimum, Math.Min(_responseTimeout.Maximum, ms));
            });
            suggest.MinimumSize = new Size(60, 24);
            new ToolTip().SetToolTip(suggest, "Serial: wire time of a full 125-register read at this baud, plus device turnaround, doubled for USB latency");
            var timing = Ui.Group("Timing", Ui.Form("Response Timeout [ms]:", Ui.Row(_responseTimeout, suggest), "Delay Between Polls [ms]:", _delay));
            _rtsToggle = new CheckBox { Text = "RTS Toggle", AutoSize = true, Checked = s.RtsToggle };
            _rtsDelay = Ui.Num(0, 1000, s.RtsDisableDelayMs, 70);
            _dsr = new CheckBox { Text = "DSR flow control", AutoSize = true, Checked = s.DsrFlow };
            _cts = new CheckBox { Text = "CTS flow control", AutoSize = true, Checked = s.CtsFlow };
            _echo = new CheckBox { Text = "Remove Echo", AutoSize = true, Checked = s.RemoveEcho };
            _advBox = Ui.Group("Advanced (serial)", Ui.Form(null, _rtsToggle, "RTS disable delay [ms]:", _rtsDelay,
                                                            null, _dsr, null, _cts, null, _echo));

            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top };
            grid.Controls.Add(_serialBox, 0, 0);
            grid.SetRowSpan(_serialBox, 2);
            grid.Controls.Add(_modeBox, 1, 0);
            grid.Controls.Add(timing, 1, 1);
            grid.Controls.Add(_netBox, 0, 2);
            grid.Controls.Add(_advBox, 1, 2);

            var ok = Ui.Button("OK", null);
            ok.DialogResult = DialogResult.OK;
            var cancel = Ui.Button("Cancel", null);
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok;
            CancelButton = cancel;
            Ui.Stack(this, new Control[] { Ui.Form("Connection:", _kind), grid }, ok, cancel);
            _kind.SelectedIndexChanged += (o, e) => Sync();
            Sync();
        }

        void Sync()
        {
            var k = (ConnectionKind)_kind.SelectedIndex;
            _serialBox.Enabled = _advBox.Enabled = k == ConnectionKind.Serial;
            _netBox.Enabled = k.IsNetwork();
            _modeBox.Enabled = !k.UsesMbap();
            _connectTimeout.Enabled = k == ConnectionKind.Tcp || k == ConnectionKind.SerialOverTcp;
        }

        public ConnectionSettings Result()
        {
            int baud;
            if (!int.TryParse(_baud.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out baud) || baud <= 0) baud = 9600;
            return new ConnectionSettings
            {
                Kind = (ConnectionKind)_kind.SelectedIndex,
                Mode = _rtu.Checked ? SerialMode.Rtu : SerialMode.Ascii,
                Port = _port.Text.Trim(),
                BaudRate = baud,
                DataBits = (int)_databits.SelectedItem,
                Parity = Parities[_parity.SelectedIndex],
                StopBits = (int)_stopbits.SelectedItem == 2 ? StopBits.Two : StopBits.One,
                Host = _host.Text.Trim(),
                TcpPort = (int)_tcpPort.Value,
                ConnectTimeoutMs = (int)_connectTimeout.Value,
                ResponseTimeoutMs = (int)_responseTimeout.Value,
                DelayBetweenPollsMs = (int)_delay.Value,
                RtsToggle = _rtsToggle.Checked,
                RtsDisableDelayMs = (int)_rtsDelay.Value,
                DsrFlow = _dsr.Checked,
                CtsFlow = _cts.Checked,
                RemoveEcho = _echo.Checked,
            };
        }
    }

    // --- Read/Write definition ----------------------------------------------------------

    sealed class DefinitionDialog : Form
    {
        readonly NumericUpDown _slave, _address, _quantity, _scan;
        readonly ComboBox _function, _display, _rows;
        readonly CheckBox _plc, _enabled, _hideAlias, _addrInCell;
        readonly Action<PollDefinition, ViewOptions> _apply;
        readonly Action _once;
        readonly ViewOptions _baseView;

        public DefinitionDialog(PollDefinition d, ViewOptions v, Action<PollDefinition, ViewOptions> apply, Action once)
        {
            Text = "Poll Setup";
            _apply = apply;
            _once = once;
            _baseView = v;
            _slave = Ui.Num(0, 255, d.SlaveId);
            _function = Ui.Combo(Addressing.FunctionLabels, d.Function - 1, width: 230);
            _plc = new CheckBox { Text = "PLC Addresses (Base 1)", AutoSize = true, Checked = v.PlcAddresses };
            _address = Ui.Num(0, 65536, d.Address + (v.PlcAddresses ? 1 : 0));
            _quantity = Ui.Num(1, Pdu.MaxReadBits, d.Quantity);
            _scan = Ui.Num(1, 3600000, d.ScanRateMs, 100);
            _enabled = new CheckBox { Text = "Polling enabled", AutoSize = true, Checked = d.Enabled };
            _rows = Ui.Combo(ViewOptions.RowChoices.Cast<object>(), Math.Max(0, Array.IndexOf(ViewOptions.RowChoices, v.Rows)), width: 80);
            _display = Ui.Combo(Ui.FormatChoices(v.Display), Ui.FormatIndex(v.Display), width: 220);
            _hideAlias = new CheckBox { Text = "Hide Alias Columns", AutoSize = true, Checked = v.HideAlias };
            _addrInCell = new CheckBox { Text = "Address in Cell", AutoSize = true, Checked = v.AddressInCell };
            _plc.CheckedChanged += (o, e) =>
                _address.Value = Math.Max(_address.Minimum, Math.Min(_address.Maximum, _address.Value + (_plc.Checked ? 1 : -1)));

            var form = Ui.Form("Unit ID:", _slave, "Function:", _function, "Address:", _address, null, _plc,
                               "Quantity:", _quantity, "Interval [ms]:", _scan, null, _enabled);
            var view = Ui.Group("View", Ui.Form("Rows:", _rows, "Display:", _display, null, _hideAlias, null, _addrInCell));
            var ok = Ui.Button("OK", (o, e) => { if (Apply()) { DialogResult = DialogResult.OK; } });
            var cancel = Ui.Button("Cancel", null);
            cancel.DialogResult = DialogResult.Cancel;
            var apply_ = Ui.Button("&Apply", (o, e) => Apply());
            var onceBtn = Ui.Button("&Read Once", (o, e) => { if (Apply()) _once(); });
            AcceptButton = ok;
            CancelButton = cancel;
            Ui.Stack(this, new Control[] { form, view }, ok, cancel, apply_, onceBtn);
        }

        bool Apply()
        {
            try
            {
                bool plc = _plc.Checked;
                var d = new PollDefinition
                {
                    SlaveId = (int)_slave.Value,
                    Function = _function.SelectedIndex + 1,
                    Address = (int)_address.Value - (plc ? 1 : 0),
                    Quantity = (int)_quantity.Value,
                    ScanRateMs = (int)_scan.Value,
                    Enabled = _enabled.Checked,
                };
                d.Validate();
                var v = new ViewOptions
                {
                    Display = (DisplayFormat)_display.SelectedItem,
                    PlcAddresses = plc,
                    Rows = (int)_rows.SelectedItem,
                    HideAlias = _hideAlias.Checked,
                    AddressInCell = _addrInCell.Checked,
                    CellFormats = _baseView.CellFormats,
                };
                _apply(d, v);
                return true;
            }
            catch (FormatException ex)
            {
                Ui.Error(this, ex);
                return false;
            }
        }
    }

    // --- Write functions -------------------------------------------------------------------

    /// <summary>Base: slave/address, Send, result line, close on success.</summary>
    abstract class WriteDialog : Form
    {
        protected readonly PollEngine Engine;
        protected readonly bool Plc;
        protected readonly NumericUpDown Slave, Address;
        protected readonly Label ResultLabel = new Label { Text = "N/A", AutoSize = true, MinimumSize = new Size(260, 0) };
        protected readonly CheckBox CloseOnOk = new CheckBox { Text = "Close dialog on \"Response ok\"", AutoSize = true };
        protected readonly Button SendButton;
        readonly List<Control> _rows = new List<Control>();
        readonly List<object> _form = new List<object>();

        protected WriteDialog(string title, PollEngine engine, int slave, int address, bool plc)
        {
            Text = title;
            Engine = engine;
            Plc = plc;
            Slave = Ui.Num(0, 255, slave);
            Address = Ui.Num(0, 65536, address + (plc ? 1 : 0));
            AddField("Slave ID:", Slave);
            AddField(plc ? "Address (base 1):" : "Address:", Address);
            SendButton = Ui.Button("&Send", (o, e) => Send());
        }

        protected void AddField(string label, Control c) { _form.Add(label); _form.Add(c); }
        protected void AddRow(Control c) { _rows.Add(c); }

        /// <summary>Call at the end of the derived constructor.</summary>
        protected void Build()
        {
            var close = Ui.Button("Close", null);
            close.DialogResult = DialogResult.Cancel;
            CancelButton = close;
            AcceptButton = SendButton;
            var rows = new List<Control> { Ui.Form(_form.ToArray()) };
            rows.AddRange(_rows);
            rows.Add(Ui.Group("Result", ResultLabel));
            rows.Add(CloseOnOk);
            Ui.Stack(this, rows.ToArray(), SendButton, close);
        }

        protected int ProtocolAddress(NumericUpDown n)
        {
            int a = (int)n.Value - (Plc ? 1 : 0);
            if (a < 0 || a > 0xFFFF) throw new FormatException("Address out of range");
            return a;
        }

        protected abstract byte[] BuildPdu();
        protected virtual void OnSuccess(int[] values) { }

        async void Send()
        {
            byte[] pdu;
            try { pdu = BuildPdu(); }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException)
            {
                Ui.Error(this, ex);
                return;
            }
            if (Confirm.Enabled && !Confirm.Ask(this, Text, (int)Slave.Value, pdu)) return;
            SendButton.Enabled = false;
            ResultLabel.Text = "Sending…";
            try
            {
                object r = await Engine.Write((int)Slave.Value, pdu);
                OnSuccess(r as int[]);
                ResultLabel.Text = Slave.Value == 0 ? "Broadcast sent (no response)" : "Response ok";
                if (CloseOnOk.Checked) DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                ResultLabel.Text = Ui.Describe(ex);
            }
            finally
            {
                if (!IsDisposed) SendButton.Enabled = true;
            }
        }
    }

    sealed class WriteSingleCoilDialog : WriteDialog
    {
        readonly RadioButton _on = new RadioButton { Text = "On", AutoSize = true };
        readonly RadioButton _off = new RadioButton { Text = "Off", AutoSize = true };
        readonly CheckBox _fc15 = new CheckBox { Text = "Use 15: Write multiple coils", AutoSize = true };

        public WriteSingleCoilDialog(PollEngine e, int slave, int address, bool plc, int value)
            : base("05: Write Single Coil", e, slave, address, plc)
        {
            (value != 0 ? _on : _off).Checked = true;
            AddField("Value:", Ui.Row(_on, _off));
            AddRow(_fc15);
            Build();
        }

        protected override byte[] BuildPdu()
        {
            int a = ProtocolAddress(Address);
            return _fc15.Checked ? Pdu.WriteMultipleCoils(a, new[] { _on.Checked ? 1 : 0 }) : Pdu.WriteSingleCoil(a, _on.Checked);
        }
    }

    sealed class WriteRegisterDialog : WriteDialog
    {
        readonly ComboBox _fmt;
        readonly TextBox _value = new TextBox { Width = 160 };
        readonly CheckBox _fc16 = new CheckBox { Text = "Use 16: Write multiple registers", AutoSize = true };

        public WriteRegisterDialog(PollEngine e, int slave, int address, bool plc, DisplayFormat fmt, string value)
            : base("06: Write Single Register", e, slave, address, plc)
        {
            _fmt = Ui.Combo(Ui.FormatChoices(fmt), Ui.FormatIndex(fmt), width: 220);
            if (value != null) _value.Text = value;
            AddField("Format:", _fmt);
            AddField("Value:", _value);
            AddRow(_fc16);
            _fmt.SelectedIndexChanged += (o, ev) => Sync();
            Sync();
            Build();
        }

        DisplayFormat Format { get { return (DisplayFormat)_fmt.SelectedItem; } }

        void Sync()
        {
            bool wide = Format.Width > 1;
            if (wide) _fc16.Checked = true;
            _fc16.Enabled = !wide;
        }

        protected override byte[] BuildPdu()
        {
            int[] words = Formats.Parse(_value.Text, Format);
            int a = ProtocolAddress(Address);
            return words.Length == 1 && !_fc16.Checked ? Pdu.WriteSingleRegister(a, words[0]) : Pdu.WriteMultipleRegisters(a, words);
        }
    }

    /// <summary>Shared by 15/16/23: an editable value list with open/save.</summary>
    abstract class ValueListDialog : WriteDialog
    {
        protected readonly NumericUpDown Quantity;
        protected readonly ComboBox Fmt;
        protected readonly ListBox List = new ListBox { Width = 300, Height = 180, SelectionMode = SelectionMode.MultiExtended, IntegralHeight = false };
        protected List<int> Values;
        readonly bool _bits;

        protected ValueListDialog(string title, PollEngine e, int slave, int address, bool plc, int quantity, int[] initial,
                                  bool bits, int maxQuantity)
            : base(title, e, slave, address, plc)
        {
            _bits = bits;
            Quantity = Ui.Num(1, maxQuantity, quantity);
            Fmt = Ui.Combo(new object[] { DisplayFormat.Signed, DisplayFormat.Unsigned, DisplayFormat.Hex, DisplayFormat.Binary }, 0, width: 120);
            Values = (initial ?? new int[0]).Take(quantity).ToList();
            while (Values.Count < quantity) Values.Add(0);
            AddField("Quantity:", Quantity);
            if (!bits) AddField("Format:", Fmt);
            AddRow(List);
            AddRow(Ui.Row(Ui.Button("&Edit", (o, ev) => EditSelected()), Ui.Button("&Open", (o, ev) => OpenFile()),
                          Ui.Button("S&ave", (o, ev) => SaveFile())));
            List.DoubleClick += (o, ev) => EditSelected();
            Quantity.ValueChanged += (o, ev) => ResizeValues((int)Quantity.Value);
            Fmt.SelectedIndexChanged += (o, ev) => Refill();
            Address.ValueChanged += (o, ev) => Refill();
            Refill();
        }

        DisplayFormat CurrentFormat { get { return _bits ? DisplayFormat.Unsigned : (DisplayFormat)Fmt.SelectedItem; } }

        protected static string Show(int v, DisplayFormat f) { return Formats.Format(new[] { v }, 0, f); }

        void ResizeValues(int n)
        {
            while (Values.Count < n) Values.Add(0);
            if (Values.Count > n) Values.RemoveRange(n, Values.Count - n);
            Refill();
        }

        void Refill()
        {
            List.BeginUpdate();
            List.Items.Clear();
            int baseAddr = (int)Address.Value;
            for (int i = 0; i < Values.Count; i++)
                List.Items.Add((baseAddr + i).ToString("D5") + " = " + (_bits ? Values[i].ToString() : Show(Values[i], CurrentFormat)));
            List.EndUpdate();
        }

        void EditSelected()
        {
            var rows = List.SelectedIndices.Cast<int>().ToList();
            if (rows.Count == 0) rows.Add(0);
            string current = _bits ? Values[rows[0]].ToString() : Show(Values[rows[0]], CurrentFormat);
            string text = InputBox.Show(this, "Enter Value", "Value:", current);
            if (text == null) return;
            try
            {
                int v = _bits ? Formats.ParseCoil(text) : Formats.Parse(text, CurrentFormat)[0];
                foreach (int r in rows) Values[r] = v;
                Refill();
            }
            catch (FormatException ex)
            {
                Ui.Error(this, ex);
            }
        }

        void OpenFile()
        {
            using (var d = new OpenFileDialog { Filter = "Value list (*.txt)|*.txt|All files (*.*)|*.*" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var vals = File.ReadAllText(d.FileName).Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                        .Select(t => t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt32(t.Substring(2), 16) : int.Parse(t, CultureInfo.InvariantCulture))
                        .ToList();
                    if (vals.Count == 0) throw new FormatException("file contains no values");
                    Values = vals.Take((int)Quantity.Maximum).ToList();
                    Quantity.Value = Values.Count;
                    Refill();
                }
                catch (Exception ex) when (ex is IOException || ex is FormatException || ex is OverflowException)
                {
                    Ui.Error(this, ex);
                }
            }
        }

        void SaveFile()
        {
            using (var d = new SaveFileDialog { Filter = "Value list (*.txt)|*.txt", FileName = "values.txt" })
                if (d.ShowDialog(this) == DialogResult.OK) File.WriteAllLines(d.FileName, Values.Select(v => v.ToString(CultureInfo.InvariantCulture)));
        }
    }

    sealed class WriteCoilsDialog : ValueListDialog
    {
        public WriteCoilsDialog(PollEngine e, int slave, int address, bool plc, int quantity, int[] initial)
            : base("15: Write Multiple Coils", e, slave, address, plc, quantity, initial, true, Pdu.MaxWriteBits) { Build(); }

        protected override byte[] BuildPdu() { return Pdu.WriteMultipleCoils(ProtocolAddress(Address), Values); }
    }

    sealed class WriteRegistersDialog : ValueListDialog
    {
        public WriteRegistersDialog(PollEngine e, int slave, int address, bool plc, int quantity, int[] initial)
            : base("16: Write Multiple Registers", e, slave, address, plc, quantity, initial, false, Pdu.MaxWriteRegisters) { Build(); }

        protected override byte[] BuildPdu() { return Pdu.WriteMultipleRegisters(ProtocolAddress(Address), Values); }
    }

    sealed class ReadWriteRegistersDialog : ValueListDialog
    {
        readonly NumericUpDown _readAddress, _readQuantity;
        readonly ListBox _readResult = new ListBox { Width = 300, Height = 100, IntegralHeight = false };

        public ReadWriteRegistersDialog(PollEngine e, int slave, int address, bool plc, int quantity, int[] initial)
            : base("23: Read/Write Multiple Registers", e, slave, address, plc, quantity, initial, false, Pdu.MaxRwWriteRegisters)
        {
            _readAddress = Ui.Num(0, 65536, (int)Address.Value);
            _readQuantity = Ui.Num(1, Pdu.MaxRwReadRegisters, Math.Min(quantity, Pdu.MaxRwReadRegisters));
            AddField("Read Address:", _readAddress);
            AddField("Read Quantity:", _readQuantity);
            AddRow(Ui.Group("Read Result", _readResult));
            Build();
        }

        protected override byte[] BuildPdu()
        {
            return Pdu.ReadWriteRegisters(ProtocolAddress(_readAddress), (int)_readQuantity.Value, ProtocolAddress(Address), Values);
        }

        protected override void OnSuccess(int[] values)
        {
            _readResult.Items.Clear();
            int baseAddr = (int)_readAddress.Value;
            var fmt = (DisplayFormat)Fmt.SelectedItem;
            for (int i = 0; values != null && i < values.Length; i++)
                _readResult.Items.Add((baseAddr + i).ToString("D5") + " = " + Show(values[i], fmt));
        }
    }

    sealed class MaskWriteDialog : WriteDialog
    {
        readonly TextBox _and = new TextBox { Text = "FFFF", Width = 80 };
        readonly TextBox _or = new TextBox { Text = "0000", Width = 80 };

        public MaskWriteDialog(PollEngine e, int slave, int address, bool plc) : base("22: Mask Write Register", e, slave, address, plc)
        {
            AddField("AND Mask (hex):", _and);
            AddField("OR Mask (hex):", _or);
            AddRow(new Label { Text = "Result = (Current AND And_Mask) OR (Or_Mask AND (NOT And_Mask))", AutoSize = true });
            Build();
        }

        protected override byte[] BuildPdu()
        {
            return Pdu.MaskWriteRegister(ProtocolAddress(Address), Formats.Parse(_and.Text, DisplayFormat.Hex)[0],
                                         Formats.Parse(_or.Text, DisplayFormat.Hex)[0]);
        }
    }

    // --- Logging ------------------------------------------------------------------------------

    sealed class LogDialog : Form
    {
        readonly TextBox _path = new TextBox { Text = "logfile.csv", Width = 260 };
        readonly CheckBox _each = new CheckBox { Text = "Each Read", AutoSize = true, Checked = true };
        readonly NumericUpDown _rate = Ui.Num(1, 86400, 1, 70);
        readonly ComboBox _delim = Ui.Combo(new object[] { "Fixed Width", "Comma (CSV)", "Tab" }, 1, width: 120);
        readonly ComboBox _time = Ui.Combo(new object[] { "Local time with UTC offset", "UTC (Z)" }, width: 200);
        readonly CheckBox _errors = new CheckBox { Text = "Log Errors (row with status, no values)", AutoSize = true, Checked = true };
        readonly CheckBox _changed = new CheckBox { Text = "Log if data changed only", AutoSize = true };
        readonly NumericUpDown _deadband = Ui.Num(0, 65535, 0, 70);
        readonly NumericUpDown _heartbeat = Ui.Num(0, 86400, 60, 70);
        readonly ComboBox _rotation = Ui.Combo(new object[] { "None", "New file every day", "New file at size limit" }, width: 200);
        readonly NumericUpDown _sizeMb = Ui.Num(1, 2048, 10, 70);
        readonly NumericUpDown _keep = Ui.Num(0, 10000, 30, 70);
        readonly CheckBox _raw = new CheckBox { Text = "Raw register values (ignore tag scaling)", AutoSize = true };

        public LogDialog()
        {
            Text = "Log Setup";
            _each.CheckedChanged += (o, e) => Sync();
            _changed.CheckedChanged += (o, e) => Sync();
            _rotation.SelectedIndexChanged += (o, e) => Sync();
            var browse = Ui.Button("…", (o, e) =>
            {
                using (var d = new SaveFileDialog { Filter = "Modbus log data (*.csv;*.txt)|*.csv;*.txt", FileName = _path.Text, OverwritePrompt = false })
                    if (d.ShowDialog(this) == DialogResult.OK) _path.Text = d.FileName;
            });
            browse.MinimumSize = new Size(30, 24);
            var ok = Ui.Button("OK", null);
            ok.DialogResult = DialogResult.OK;
            var cancel = Ui.Button("Cancel", null);
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok;
            CancelButton = cancel;
            var what = Ui.Group("What to log", Ui.Form(null, _each, "Rate [s]:", _rate, null, _errors, null, _changed,
                                                      "Deadband [raw]:", _deadband, "Heartbeat [s] (0 = off):", _heartbeat, null, _raw));
            var file = Ui.Group("File", Ui.Form("Delimiter:", _delim, "Timestamps:", _time, "Rotation:", _rotation,
                                               "Size limit [MB]:", _sizeMb, "Keep files (0 = all):", _keep));
            Ui.Stack(this, new Control[] { Ui.Form("File:", Ui.Row(_path, browse)), what, file }, ok, cancel);
            Sync();
        }

        void Sync()
        {
            _rate.Enabled = !_each.Checked;
            _deadband.Enabled = _heartbeat.Enabled = _changed.Checked;
            _sizeMb.Enabled = _rotation.SelectedIndex == (int)LogRotation.Size;
            _keep.Enabled = _rotation.SelectedIndex != (int)LogRotation.None;
        }

        public LogSettings Result()
        {
            return new LogSettings
            {
                Path = _path.Text, EachPoll = _each.Checked, RateSeconds = (int)_rate.Value, Delimiter = (Delimiter)_delim.SelectedIndex,
                LogErrors = _errors.Checked, ChangedOnly = _changed.Checked, Deadband = (double)_deadband.Value,
                HeartbeatSeconds = _changed.Checked ? (int)_heartbeat.Value : 0, Time = (LogTime)_time.SelectedIndex,
                Rotation = (LogRotation)_rotation.SelectedIndex, RotateSizeMb = (int)_sizeMb.Value, KeepFiles = (int)_keep.Value,
                RawValues = _raw.Checked,
            };
        }
    }

    // --- Test Center ------------------------------------------------------------------------------

    sealed class TestCenterDialog : Form
    {
        readonly PollEngine _engine;
        readonly ComboBox _input = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
        readonly ComboBox _check = Ui.Combo(new object[] { "No check", "Add CRC", "Add LRC + CRLF" }, width: 130);
        readonly ListBox _log = new ListBox { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, IntegralHeight = false };
        int _sequence;

        public TestCenterDialog(PollEngine engine)
        {
            _engine = engine;
            Text = "Frame Console";
            Font = SystemFonts.MessageBoxFont;
            _log.Font = Ui.Mono();
            Size = new Size(720, 440);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            _input.AccessibleName = "Frame bytes";
            _check.AccessibleName = "Frame check";
            _log.AccessibleName = "Frame console traffic";
            var top = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, Height = 32 };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.Controls.Add(_input);
            top.Controls.Add(_check);
            var buttons = Ui.Row(Ui.Button("&Send", (o, e) => Send()), Ui.Button("A&dd to list", (o, e) => AddToList()),
                                 Ui.Button("&Open list", (o, e) => OpenList()), Ui.Button("S&ave list", (o, e) => SaveList()),
                                 Ui.Button("&Copy", (o, e) => CopySelected()), Ui.Button("C&lear", (o, e) => _log.Items.Clear()));
            var hint = new Label
            {
                Text = "Enter hex bytes separated by comma, period or space. Raw frames bypass framing: include the MBAP header " +
                       "for Modbus TCP, or add CRC/LRC for RTU/ASCII. Check the unit ID in the frame before sending.",
                Dock = DockStyle.Bottom, Height = 50,
            };
            Controls.Add(_log);
            Controls.Add(hint);
            Controls.Add(buttons);
            Controls.Add(top);
            AcceptButton = (Button)buttons.Controls[0];
        }

        async void Send()
        {
            byte[] data;
            try { data = TestCenter.AddCheck(Hex.ParseSeparated(_input.Text), (CheckKind)_check.SelectedIndex); }
            catch (FormatException ex) { Ui.Error(this, ex); return; }
            _log.Items.Add(Traffic.Line(++_sequence, "TX", data));
            try
            {
                var rx = (byte[])await _engine.Raw(data);
                _log.Items.Add(rx.Length > 0 ? Traffic.Line(++_sequence, "RX", rx) : (++_sequence).ToString("D6") + " | RX | (no response)");
            }
            catch (Exception ex)
            {
                _log.Items.Add((++_sequence).ToString("D6") + " | ERROR | " + Ui.Describe(ex));
            }
            _log.TopIndex = Math.Max(0, _log.Items.Count - 1);
        }

        void AddToList()
        {
            string t = _input.Text.Trim();
            if (t.Length > 0 && !_input.Items.Contains(t)) _input.Items.Add(t);
        }

        void OpenList()
        {
            using (var d = new OpenFileDialog { Filter = "Frame list (*.txt)|*.txt|All files (*.*)|*.*" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var entries = TestCenter.Load(d.FileName);
                    _input.Items.Clear();
                    _input.Items.AddRange(entries.Cast<object>().ToArray());
                    if (entries.Count > 0) _input.SelectedIndex = 0;
                }
                catch (Exception ex) when (ex is IOException || ex is FormatException) { Ui.Error(this, ex); }
            }
        }

        void SaveList()
        {
            using (var d = new SaveFileDialog { Filter = "Frame list (*.txt)|*.txt", FileName = "frames.txt" })
                if (d.ShowDialog(this) == DialogResult.OK) TestCenter.Save(d.FileName, _input.Items.Cast<string>());
        }

        void CopySelected()
        {
            var lines = _log.SelectedItems.Cast<string>().ToList();
            if (lines.Count > 0) Clipboard.SetText(string.Join(Environment.NewLine, lines));
        }
    }

    /// <summary>Optional write confirmation shared by grid edits and write dialogs.</summary>
    static class Confirm
    {
        public static bool Enabled;

        /// <summary>Show target, slave, function and decoded values; returns true to send.</summary>
        public static bool Ask(IWin32Window owner, string title, int slave, byte[] pdu)
        {
            int address;
            int[] values;
            string what;
            WriteInfo.Decode(pdu, out address, out values, out what);
            string vals = string.Join(", ", values.Take(16)) + (values.Length > 16 ? ", … (" + values.Length + " values)" : "");
            return Ask(owner, (slave == 0 ? "BROADCAST to all slaves (no reply is returned)" : "Slave " + slave)
                              + "\nFunction " + pdu[0].ToString("D2") + " (" + title + ")\nAddress " + address + " (base 0), " + what + ": " + vals);
        }

        public static bool Ask(IWin32Window owner, string text)
        {
            return Ui.Ask(owner, text + "\n\nSend this write to the device?", "Confirm write", MessageBoxIcon.Warning);
        }
    }

    // --- Tag / scaling / alarms ------------------------------------------------------------------

    sealed class TagDialog : Form
    {
        readonly TextBox _name = new TextBox { Width = 200 };
        readonly TextBox _scale = new TextBox { Width = 100, Text = "1" };
        readonly TextBox _offset = new TextBox { Width = 100, Text = "0" };
        readonly TextBox _unit = new TextBox { Width = 100 };
        readonly NumericUpDown _decimals = Ui.Num(-1, 10, -1, 60);
        readonly TextBox _hh = new TextBox { Width = 100 }, _h = new TextBox { Width = 100 }, _l = new TextBox { Width = 100 }, _ll = new TextBox { Width = 100 };
        readonly TextBox _deadband = new TextBox { Width = 100, Text = "0" };
        readonly TextBox _wmin = new TextBox { Width = 100 }, _wmax = new TextBox { Width = 100 };
        public Tag Result;
        public bool Clear;

        static string F(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }
        static string F(double? v) { return v.HasValue ? F(v.Value) : ""; }

        public TagDialog(string address, Tag t, bool single)
        {
            Text = "Tag " + address;
            _name.Text = t.Name;
            _name.Enabled = single;
            _scale.Text = F(t.Scale);
            _offset.Text = F(t.Offset);
            _unit.Text = t.Unit;
            _decimals.Value = t.Decimals;
            _hh.Text = F(t.HighHigh); _h.Text = F(t.High); _l.Text = F(t.Low); _ll.Text = F(t.LowLow);
            _deadband.Text = F(t.Deadband);
            _wmin.Text = F(t.WriteMin); _wmax.Text = F(t.WriteMax);
            var scaling = Ui.Group("Scaling: value = raw × scale + offset", Ui.Form("Name:", _name, "Scale:", _scale, "Offset:", _offset,
                                                                                  "Unit:", _unit, "Decimals (-1 auto):", _decimals));
            var alarms = Ui.Group("Alarms (engineering units, empty = off)", Ui.Form("HH:", _hh, "H:", _h, "L:", _l, "LL:", _ll,
                                                                                      "Deadband:", _deadband));
            var limits = Ui.Group("Write limits (engineering units)", Ui.Form("Min:", _wmin, "Max:", _wmax));
            var ok = Ui.Button("OK", (o, e) => Accept());
            var clear = Ui.Button("Remove tag", (o, e) => { Clear = true; DialogResult = DialogResult.OK; });
            var cancel = Ui.Button("Cancel", null);
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok;
            CancelButton = cancel;
            Ui.Stack(this, new Control[] { scaling, alarms, limits }, ok, clear, cancel);
        }

        static double Req(TextBox b, string what)
        {
            double v;
            if (!double.TryParse(b.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) || double.IsNaN(v) || double.IsInfinity(v))
                throw new FormatException(what + ": '" + b.Text + "' is not a number");
            return v;
        }

        static double? Opt(TextBox b, string what) { return b.Text.Trim().Length == 0 ? (double?)null : Req(b, what); }

        void Accept()
        {
            try
            {
                var t = new Tag
                {
                    Name = _name.Text.Trim(), Scale = Req(_scale, "Scale"), Offset = Req(_offset, "Offset"), Unit = _unit.Text.Trim(),
                    Decimals = (int)_decimals.Value, HighHigh = Opt(_hh, "HH"), High = Opt(_h, "H"), Low = Opt(_l, "L"), LowLow = Opt(_ll, "LL"),
                    Deadband = Req(_deadband, "Deadband"), WriteMin = Opt(_wmin, "Write min"), WriteMax = Opt(_wmax, "Write max"),
                };
                t.Validate();
                Result = t;
                DialogResult = DialogResult.OK;
            }
            catch (FormatException ex) { Ui.Error(this, ex); }
        }
    }

    // --- Unit-ID scan and device identification ---------------------------------------------------------

    sealed class ScanDialog : Form
    {
        readonly PollEngine _engine;
        readonly NumericUpDown _from = Ui.Num(1, 247, 1, 60), _to = Ui.Num(1, 247, 247, 60);
        readonly ComboBox _fc = Ui.Combo(new object[] { "03 Holding register", "04 Input register", "01 Coil", "02 Discrete input" }, width: 160);
        readonly NumericUpDown _addr = Ui.Num(0, 65535, 0, 80);
        readonly NumericUpDown _delay = Ui.Num(0, 5000, 50, 70);
        readonly ListBox _results = new ListBox { Width = 420, Height = 160, IntegralHeight = false };
        readonly Label _progress = new Label { AutoSize = true, Text = "" };
        readonly Button _start, _identify;
        System.Threading.CancellationTokenSource _cts;
        static readonly int[] Fcs = { 3, 4, 1, 2 };
        public readonly List<ScanHit> Hits = new List<ScanHit>();

        public ScanDialog(PollEngine engine)
        {
            _engine = engine;
            Text = "Unit-ID Scan (read-only)";
            var warning = new Label
            {
                AutoSize = true, MaximumSize = new Size(440, 0), ForeColor = Color.FromArgb(160, 60, 0),
                Text = "Active scanning sends one read to every ID in the range. On a running plant only scan with permission, " +
                       "preferably during planned downtime (NIST SP 800-82r3). The scan never writes. Silence does not prove an " +
                       "ID is free: a wrong baud rate, parity or wiring also produces silence.",
            };
            _start = Ui.Button("&Start", (o, e) => StartOrStop());
            _identify = Ui.Button("&Identify selected (FC43/14)", (o, e) => Identify());
            var close = Ui.Button("Close", null);
            close.DialogResult = DialogResult.Cancel;
            CancelButton = close;
            Ui.Stack(this, new Control[] { warning, Ui.Form("From ID:", _from, "To ID:", _to, "Read:", _fc, "Address (base 0):", _addr,
                                                             "Delay between probes [ms]:", _delay), _progress, _results }, _start, _identify, close);
            FormClosing += (o, e) => { if (_cts != null) _cts.Cancel(); };
        }

        async void StartOrStop()
        {
            if (_cts != null) { _cts.Cancel(); return; }
            int from = (int)_from.Value, to = (int)_to.Value, fc = Fcs[_fc.SelectedIndex], addr = (int)_addr.Value, delay = (int)_delay.Value;
            if (from > to) { Ui.Error(this, new FormatException("From ID must be ≤ To ID")); return; }
            _cts = new System.Threading.CancellationTokenSource();
            var token = _cts.Token;
            _start.Text = "&Stop";
            _results.Items.Clear();
            Hits.Clear();
            try
            {
                var hits = (List<ScanHit>)await _engine.Submit(m => Discovery.ScanUnits(m, from, to, fc, addr, delay,
                    id => { if (!IsDisposed) BeginInvoke((Action)(() => _progress.Text = "Probing ID " + id + " of " + from + ".." + to)); }, token));
                Hits.AddRange(hits);
                foreach (ScanHit h in hits)
                    _results.Items.Add("ID " + h.SlaveId.ToString().PadLeft(3) + "   " + h.Reply + "   " + h.RttMs.ToString("0") + " ms");
                _progress.Text = (token.IsCancellationRequested ? "Stopped. " : "Done. ") + hits.Count + " device(s) answered.";
            }
            catch (Exception ex)
            {
                _progress.Text = Ui.Describe(ex);
            }
            finally
            {
                _cts = null;
                if (!IsDisposed) _start.Text = "&Start";
            }
        }

        async void Identify()
        {
            int id;
            if (_results.SelectedIndex >= 0) id = Hits[_results.SelectedIndex].SlaveId;
            else
            {
                string s = InputBox.Show(this, "Identify", "Slave ID:", "1");
                if (s == null || !int.TryParse(s, out id) || id < 1 || id > 247) return;
            }
            try
            {
                var objs = (List<KeyValuePair<int, string>>)await _engine.Submit(m => Discovery.ReadDeviceId(m, id, true));
                string text = objs.Count == 0 ? "The device returned no identification objects."
                    : string.Join("\n", objs.Select(kv => Discovery.ObjectName(kv.Key) + ": " + kv.Value));
                MessageBox.Show(this, text, "Device identification, slave " + id, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Slave " + id + ": " + Ui.Describe(ex) + "\n\nMany devices do not implement FC43/14.",
                                "Device identification", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }

    /// <summary>Type × byte order picker; the info line shows the register count and order notes for the choice.</summary>
    sealed class FormatDialog : Form
    {
        readonly ComboBox _type = Ui.Combo(DisplayFormat.TypeLabels, 0, width: 210);
        readonly ComboBox _order = Ui.Combo(DisplayFormat.OrderLabels, 0, width: 210);
        readonly NumericUpDown _length = Ui.Num(1, DisplayFormat.MaxStringRegisters, 8, 60);
        readonly NumericUpDown _bitStart = Ui.Num(0, 15, 0, 60), _bitWidth = Ui.Num(1, 16, 1, 60);
        readonly Label _info = new Label { AutoSize = true, MaximumSize = new Size(360, 0) };
        public DisplayFormat Result;

        public FormatDialog(DisplayFormat current)
        {
            Text = "Display Format";
            _type.SelectedIndex = (int)current.Type;
            _order.SelectedIndex = (int)current.Order;
            if (current.Type == DataType.String) _length.Value = current.Length;
            if (current.Type == DataType.BitField) { _bitStart.Value = current.BitStart; _bitWidth.Value = current.BitWidth; }
            var form = Ui.Form("Type:", _type, "Byte order:", _order, "Length [registers]:", _length,
                               "First bit (0 = LSB):", _bitStart, "Bit count:", _bitWidth, null, _info);
            var ok = Ui.Button("OK", (o, e) => { if (Build() != null) DialogResult = DialogResult.OK; });
            var cancel = Ui.Button("Cancel", null);
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok;
            CancelButton = cancel;
            foreach (Control c in new Control[] { _type, _order })
                ((ComboBox)c).SelectedIndexChanged += (o, e) => Sync();
            foreach (NumericUpDown n in new[] { _length, _bitStart, _bitWidth }) n.ValueChanged += (o, e) => Sync();
            Ui.Stack(this, new Control[] { form }, ok, cancel);
            Sync();
        }

        DisplayFormat Build()
        {
            try
            {
                Result = new DisplayFormat((DataType)_type.SelectedIndex, (ByteOrder)_order.SelectedIndex, (int)_length.Value,
                                           (int)_bitStart.Value, (int)_bitWidth.Value);
                return Result;
            }
            catch (FormatException ex)
            {
                Ui.Error(this, ex);
                return null;
            }
        }

        void Sync()
        {
            var t = (DataType)_type.SelectedIndex;
            _length.Enabled = t == DataType.String;
            _bitStart.Enabled = _bitWidth.Enabled = t == DataType.BitField;
            _order.Enabled = t != DataType.BitField;
            if (t == DataType.BitField && _bitStart.Value + _bitWidth.Value > 16) _bitWidth.Value = 16 - _bitStart.Value;
            var f = new DisplayFormat(t, (ByteOrder)_order.SelectedIndex, (int)_length.Value, (int)_bitStart.Value, (int)_bitWidth.Value);
            _info.Text = f.Label + ": " + f.Width + (f.Width == 1 ? " register" : " registers")
                         + (f.Width == 1 && t != DataType.BitField ? "; word order does not apply (ABCD = AB, DCBA = BA)" : "")
                         + (t == DataType.BitField ? "; writes merge into the last read register value (FC06)" : "");
        }
    }

    /// <summary>Shows the registers at the selection decoded as every type and byte order side by side, so an unknown
    /// device's layout can be found by recognising the plausible value. Double-click a row to use that format.</summary>
    sealed class InterpretDialog : Form
    {
        readonly Label _source = new Label { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 6, 8, 0), AutoEllipsis = true };
        readonly ListView _list = new ListView
        {
            View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false, MultiSelect = false,
            Dock = DockStyle.Fill, HeaderStyle = ColumnHeaderStyle.Nonclickable,
        };
        readonly CheckBox _follow = new CheckBox { Text = "&Follow selection and live values", AutoSize = true, Checked = true };
        PollWindow _window;
        int _offset = -1;
        int[] _shown;

        /// <summary>Raised when the user applies a row to the window's selection.</summary>
        public event Action<PollWindow, int, DisplayFormat> Apply;

        public InterpretDialog()
        {
            Text = "Interpret As";
            Font = SystemFonts.MessageBoxFont;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(600, 560);
            MinimumSize = new Size(420, 300);
            _list.Font = Ui.Mono();
            _list.Columns.Add("Format", 170);
            _list.Columns.Add("Value", 380);
            _list.DoubleClick += (o, e) => ApplySelected();
            var use = Ui.Button("&Use for cell", (o, e) => ApplySelected());
            var copy = Ui.Button("&Copy", (o, e) => Copy());
            var close = Ui.Button("Close", (o, e) => Hide());
            CancelButton = close;
            var bar = Ui.Row(_follow, use, copy, close);
            bar.Dock = DockStyle.Bottom;
            bar.Padding = new Padding(4);
            _follow.Margin = new Padding(3, 8, 12, 3);
            Controls.Add(_list);
            Controls.Add(_source);
            Controls.Add(bar);
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
        }

        public int RowCount { get { return _list.Items.Count; } }

        public string Row(int i) { return _list.Items[i].Text + " = " + _list.Items[i].SubItems[1].Text; }

        public string Source { get { return _source.Text; } }

        /// <summary>Show the window's registers at its current selection.</summary>
        public void Track(PollWindow w)
        {
            _window = w;
            _offset = -1;
            _shown = null;
            Reload(w);
        }

        public void Forget(PollWindow w)
        {
            if (w != _window) return;
            _window = null;
            _list.Items.Clear();
            _source.Text = "Window closed";
        }

        /// <summary>Called after every applied result and selection change; ignored for other windows, while hidden,
        /// or when "Follow" is off.</summary>
        public void WindowUpdated(PollWindow w)
        {
            if (Visible && w == _window && _follow.Checked) Reload(w);
        }

        void Reload(PollWindow w)
        {
            int offset;
            int[] regs = w.RegistersAtSelection(out offset);
            string address = Addressing.Label(w.Doc.Definition.Function, w.Doc.Definition.Address + offset, w.Doc.View.PlcAddresses);
            if (regs == null)
            {
                _source.Text = w.DisplayTitle.TrimEnd(' ', '*') + " · " + address + ": no register values yet";
                _list.Items.Clear();
                _shown = null;
                _offset = offset;
                return;
            }
            if (offset == _offset && _shown != null && regs.SequenceEqual(_shown)) return;
            _offset = offset;
            _shown = regs;
            int n = Math.Min(regs.Length, 4);
            _source.Text = w.DisplayTitle.TrimEnd(' ', '*') + " · " + address + ": "
                           + string.Join(" ", regs.Take(n).Select(r => r.ToString("X4"))) + (regs.Length > n ? " …" : "")
                           + "   (A = most significant byte of the value)";
            int selected = _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : -1;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var kv in Formats.Interpretations(regs))
                _list.Items.Add(new ListViewItem(new[] { kv.Key.Spec, kv.Value }) { Tag = kv.Key });
            if (selected >= 0 && selected < _list.Items.Count) _list.Items[selected].Selected = true;
            _list.EndUpdate();
        }

        void ApplySelected()
        {
            if (_list.SelectedItems.Count == 0 || _window == null || _window.IsDisposed) return;
            var h = Apply;
            if (h != null) h(_window, _offset, (DisplayFormat)_list.SelectedItems[0].Tag);
        }

        void Copy()
        {
            var sb = new System.Text.StringBuilder(_source.Text + "\r\n");
            foreach (ListViewItem i in _list.Items) sb.Append(i.Text).Append('\t').Append(i.SubItems[1].Text).Append("\r\n");
            Clipboard.SetText(sb.ToString());
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            base.OnFormClosing(e);
        }
    }

    static class InputBox
    {
        /// <summary>Returns the entered text, or null when cancelled.</summary>
        public static string Show(IWin32Window owner, string title, string prompt, string value)
        {
            using (var f = new Form { Text = title })
            {
                var box = new TextBox { Text = value, Width = 220 };
                var ok = Ui.Button("OK", null);
                ok.DialogResult = DialogResult.OK;
                var cancel = Ui.Button("Cancel", null);
                cancel.DialogResult = DialogResult.Cancel;
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                Ui.Stack(f, new Control[] { Ui.Form(prompt, box) }, ok, cancel);
                return f.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
            }
        }
    }
}
