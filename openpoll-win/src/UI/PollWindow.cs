using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace MultiPollStudio.UI
{
    /// <summary>MDI child: one poll definition shown as (alias, value) column pairs, as in Modbus Poll.</summary>
    sealed class PollWindow : Form
    {
        static int _counter;

        /// <summary>The engine this window polls through: the main connection, or a per-window connection that
        /// <see cref="MainForm"/> shares between windows on the same port. Changes with <see cref="Rebind"/>.</summary>
        public PollEngine Engine { get; private set; }
        public PollDocument Doc;
        public string Path;
        public readonly string UntitledName;
        public int PollId { get; private set; }
        public int[] Values;
        public Status LastStatus = Status.NotInitialized;
        public bool Dirty;
        DataLogger _logger;
        bool _painting;

        readonly Label _status = new Label { Dock = DockStyle.Top, Height = 36, Padding = new Padding(2, 2, 0, 0), AutoEllipsis = true };
        readonly ToolTip _statusTip = new ToolTip();
        readonly Label _error = new Label
        {
            Dock = DockStyle.Fill, ForeColor = Color.FromArgb(192, 0, 0), Padding = new Padding(2, 2, 0, 0), AutoEllipsis = true,
            Text = "No connection",
        };
        readonly LinkLabel _fix = new LinkLabel { Dock = DockStyle.Right, AutoSize = true, Text = "Why? / Fix…", Padding = new Padding(2, 2, 6, 0), Visible = false };
        readonly Panel _errorBar = new Panel { Dock = DockStyle.Top, Height = 20 };
        /// <summary>The last failure (status, detail) for the diagnosis panel; Status.Ok when the last poll worked.</summary>
        public PollResult LastFailure;
        public readonly DataGridView Grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersWidth = 48,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        };

        public event Action StateChanged;
        /// <summary>Raised when the user clicks "Why? / Fix…" on the error line.</summary>
        public event Action<PollWindow> DiagnoseRequested;

        public PollWindow(PollEngine engine, PollDocument doc = null, string path = null)
        {
            Engine = engine;
            Doc = doc ?? new PollDocument();
            Path = path;
            UntitledName = "Poll " + (++_counter);
            Font = SystemFonts.MessageBoxFont;
            _status.Font = Ui.Mono();
            Size = new Size(520, 330);
            Grid.DefaultCellStyle.Font = Ui.Mono();
            Grid.CellEndEdit += OnCellEndEdit;
            Grid.CellBeginEdit += OnCellBeginEdit;
            Grid.KeyDown += OnGridKeyDown;
            Grid.CellMouseDown += (o, e) =>
            {
                if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && e.ColumnIndex >= 0 && !Grid[e.ColumnIndex, e.RowIndex].Selected)
                {
                    Grid.ClearSelection();
                    Grid[e.ColumnIndex, e.RowIndex].Selected = true;
                }
            };
            Grid.ContextMenuStrip = BuildContextMenu();
            Controls.Add(Grid);
            _errorBar.Controls.Add(_error);
            _errorBar.Controls.Add(_fix);
            _fix.LinkClicked += (o, e) => { var h = DiagnoseRequested; if (h != null) h(this); };
            Controls.Add(_errorBar);
            Controls.Add(_status);
            PollId = Engine.AddPoll(Doc.Definition, OnResult);
            Rebuild();
        }

        /// <summary>Re-register the poll on another engine (per-window connection changed). Values and alarms reset.</summary>
        public void Rebind(PollEngine engine)
        {
            if (engine == Engine) return;
            Engine.RemovePoll(PollId);
            Engine = engine;
            Values = null;
            Stale = false;
            Alarms.Clear();
            PollId = Engine.AddPoll(Doc.Definition, OnResult);
            _error.Text = Engine.Running ? "" : Status.NoConnection.Text();
            Rebuild();
            Fire();
        }

        /// <summary>The connection this window uses: its own, or the engine's current settings.</summary>
        public string ConnectionText
        {
            get { return Doc.Connection != null ? Doc.Connection.Describe() + " (own)" : Engine.Settings != null ? Engine.Settings.Describe() : "main connection"; }
        }

        public string DisplayTitle
        {
            get
            {
                string name = Doc.Title.Length > 0 ? Doc.Title : Path != null ? System.IO.Path.GetFileName(Path) : UntitledName;
                if (Doc.Connection != null) name += " [" + (Doc.Connection.Kind == ConnectionKind.Serial ? Doc.Connection.Port : Doc.Connection.Host) + "]";
                return name + (Dirty ? " *" : "");
            }
        }

        public void MarkDirty(bool dirty = true)
        {
            Dirty = dirty;
            Text = DisplayTitle;
        }

        // -- grid -------------------------------------------------------------------

        public void Rebuild()
        {
            PollDefinition d = Doc.Definition;
            ViewOptions v = Doc.View;
            int rows = v.Rows;
            int pairs = (d.Quantity + rows - 1) / rows;
            _painting = true;
            Grid.SuspendLayout();
            Grid.Columns.Clear();
            for (int p = 0; p < pairs; p++)
            {
                Grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = "Alias", Width = 110, Visible = !v.HideAlias, SortMode = DataGridViewColumnSortMode.NotSortable,
                });
                Grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = Addressing.Label(d.Function, d.Address + p * rows, v.PlcAddresses),
                    Width = v.AddressInCell ? 170 : 110,
                    SortMode = DataGridViewColumnSortMode.NotSortable,
                    DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight },
                });
            }
            Grid.Rows.Clear();
            if (pairs > 0) Grid.Rows.Add(rows);
            for (int r = 0; r < rows; r++)
            {
                Grid.Rows[r].HeaderCell.Value = r.ToString();
                for (int c = 0; c < Grid.ColumnCount; c++)
                {
                    int offset = (c / 2) * rows + r;
                    DataGridViewCell cell = Grid[c, r];
                    if (offset >= d.Quantity)
                    {
                        cell.ReadOnly = true;
                        cell.Style.BackColor = SystemColors.Control;
                        continue;
                    }
                    if (c % 2 == 0)
                    {
                        string alias;
                        cell.Value = Doc.Aliases.TryGetValue(offset, out alias) ? alias : "";
                    }
                    else
                    {
                        cell.ReadOnly = !d.Writable;
                    }
                }
            }
            Grid.ResumeLayout();
            _painting = false;
            PaintValues();
            UpdateStatus();
            Text = DisplayTitle;
        }

        public Point CellOf(int offset, bool alias = false)
        {
            int rows = Doc.View.Rows;
            return new Point((offset / rows) * 2 + (alias ? 0 : 1), offset % rows);
        }

        public int OffsetAt(int col, int row)
        {
            int offset = (col / 2) * Doc.View.Rows + row;
            return col % 2 == 1 && offset < Doc.Definition.Quantity ? offset : -1;
        }

        public string ValueText(int offset)
        {
            Point p = CellOf(offset);
            return Convert.ToString(Grid[p.X, p.Y].Value);
        }

        static readonly Color AlarmHighBack = Color.FromArgb(255, 205, 205);
        static readonly Color AlarmWarnBack = Color.FromArgb(255, 236, 179);
        static readonly Color StaleFore = Color.Gray;

        /// <summary>Alarm state per offset, carried between polls for deadband evaluation.</summary>
        public readonly Dictionary<int, AlarmLevel> Alarms = new Dictionary<int, AlarmLevel>();
        /// <summary>Alarm transitions since the window opened (newest last, capped).</summary>
        public readonly List<AlarmEvent> AlarmHistory = new List<AlarmEvent>();
        /// <summary>True while the shown values come from an earlier successful poll and the latest poll failed.</summary>
        public bool Stale { get; private set; }
        public event Action<PollWindow, PollResult> ResultApplied;
        public event Action<PollWindow, AlarmEvent> AlarmRaised;

        /// <summary>Numeric value of an offset: tag-scaled for scalable formats, raw for hex/binary/time, null for text,
        /// invalid BCD or a value cut off at the end of the window.</summary>
        public double? Engineering(int offset)
        {
            if (Values == null || offset >= Values.Length) return null;
            if (Doc.Definition.IsBits) return Values[offset];
            Tag t = Doc.TagAt(offset);
            DisplayFormat f = Doc.View.FormatAt(offset);
            if (offset + f.Width > Values.Length) return null;
            double? raw = Formats.Number(Values, offset, f);
            return raw.HasValue && t != null && f.Scalable ? t.ToEngineering(raw.Value) : raw;
        }

        bool ShowsEngineering(int offset)
        {
            Tag t = Doc.TagAt(offset);
            return t != null && !Doc.Definition.IsBits && Doc.View.FormatAt(offset).Scalable
                   && (t.IsScaled || t.Unit.Length > 0 || t.Decimals >= 0);
        }

        void PaintValues()
        {
            if (Values == null) return;
            PollDefinition d = Doc.Definition;
            ViewOptions v = Doc.View;
            _painting = true;
            foreach (var kv in Render.Values(d, Values, v))
            {
                Point p = CellOf(kv.Key);
                if (p.X >= Grid.ColumnCount) continue;
                DataGridViewCell cell = Grid[p.X, p.Y];
                if (cell.IsInEditMode) continue;
                string text = kv.Value;
                if (text.Length > 0 && ShowsEngineering(kv.Key))
                {
                    double? eng = Engineering(kv.Key);
                    if (eng.HasValue) text = Doc.TagAt(kv.Key).Format(eng.Value);
                }
                if (v.AddressInCell) text = Addressing.Label(d.Function, d.Address + kv.Key, v.PlcAddresses) + " = " + text;
                cell.Value = text;
                AlarmLevel a;
                Alarms.TryGetValue(kv.Key, out a);
                cell.Style.BackColor = a == AlarmLevel.HighHigh || a == AlarmLevel.LowLow ? AlarmHighBack
                                       : a != AlarmLevel.None ? AlarmWarnBack : Color.Empty;
                cell.Style.ForeColor = Stale ? StaleFore : Color.Empty;
                cell.ToolTipText = a != AlarmLevel.None ? a.Code() + " alarm" : Stale ? "Stale: last good value, latest poll failed" : "";
            }
            _painting = false;
        }

        void EvaluateAlarms(DateTime utc)
        {
            foreach (var kv in Doc.Tags)
            {
                if (!kv.Value.HasAlarms) continue;
                double? eng = Engineering(kv.Key);
                if (!eng.HasValue) continue;
                AlarmLevel before;
                Alarms.TryGetValue(kv.Key, out before);
                AlarmLevel now = AlarmEvaluator.Evaluate(kv.Value, eng.Value, before);
                if (now == before) continue;
                Alarms[kv.Key] = now;
                var ev = new AlarmEvent { TimestampUtc = utc, Offset = kv.Key, TagName = kv.Value.Name, From = before, To = now, Value = eng.Value };
                AlarmHistory.Add(ev);
                if (AlarmHistory.Count > 1000) AlarmHistory.RemoveAt(0);
                if (_logger != null)
                    try { _logger.LogEvent(utc, "ALARM " + ev.Describe()); } catch (IOException) { }
                var h = AlarmRaised;
                if (h != null) h(this, ev);
            }
        }

        public int ActiveAlarmCount { get { return Alarms.Values.Count(a => a != AlarmLevel.None); } }

        void UpdateStatus()
        {
            PollDefinition d = Doc.Definition;
            PollStats st = Engine.Stats(PollId);
            string kpi = st.RecentCount == 0 ? "" :
                "  |  OK " + st.SuccessPercent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%"
                + (st.LastRttMs >= 0 ? "  RTT " + st.LastRttMs.ToString("0") + "/" + st.AvgRttMs.ToString("0") + "/" + st.MaxRttMs.ToString("0") + " ms" : "")
                + (ActiveAlarmCount > 0 ? "  ALARMS " + ActiveAlarmCount : "");
            _status.Text = "Unit " + d.SlaveId + " | FC " + d.Function.ToString("D2") + " | Interval " + d.ScanRateMs + " ms"
                           + (d.Enabled ? "" : " (PAUSED)") + "\nRequests " + st.Tx + " | Errors " + st.Errors + kpi;
            _statusTip.SetToolTip(_status, _status.Text);
        }

        public string StatusText { get { return _status.Text; } }
        public string ErrorText { get { return _error.Text; } }
        public void SetErrorText(string t)
        {
            if (t == Status.NoConnection.Text()) ShowFailure(Status.NoConnection, "");
            else _error.Text = t;
        }

        /// <summary>Show a failure on the error line with the "Why? / Fix…" link (connect errors, which produce no poll result).</summary>
        public void ShowFailure(Status status, string detail)
        {
            LastFailure = new PollResult { Status = status, Detail = detail ?? "", Definition = Doc.Definition };
            _error.Text = status.Text() + (!string.IsNullOrEmpty(detail) && detail != status.Text() ? " (" + detail + ")" : "");
            _fix.Visible = true;
        }

        public bool FixVisible { get { return _fix.Visible; } }

        // -- results ------------------------------------------------------------------

        void OnResult(PollResult r)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)(() => Apply(r))); }
            catch (InvalidOperationException) { } // window closing
        }

        void Apply(PollResult r)
        {
            if (IsDisposed || !ReferenceEquals(r.Definition, Doc.Definition)) return; // stale result
            LastStatus = r.Status;
            if (r.Status == Status.Ok)
            {
                Values = r.Values;
                Stale = false;
                _error.Text = "";
                _fix.Visible = false;
                LastFailure = null;
                EvaluateAlarms(r.Timestamp);
                PaintValues();
            }
            else
            {
                _error.Text = r.Status.Text() + (r.Detail.Length > 0 && r.Detail != r.Status.Text() ? " (" + r.Detail + ")" : "");
                LastFailure = r;
                _fix.Visible = true;
                if (Values != null && !Stale)
                {
                    Stale = true;
                    PaintValues();
                }
            }
            if (_logger != null)
            {
                try { _logger.Log(r); }
                catch (IOException ex)
                {
                    StopLog();
                    Ui.Error(this, ex);
                }
            }
            UpdateStatus();
            var h = ResultApplied;
            if (h != null) h(this, r);
        }

        // -- definition ---------------------------------------------------------------

        public void ApplyDefinition(PollDefinition d, ViewOptions v)
        {
            PollDefinition old = Doc.Definition;
            bool reshape = d.Function != old.Function || d.Address != old.Address || d.Quantity != old.Quantity;
            Doc.Definition = d;
            Doc.View = v;
            if (reshape)
            {
                Values = null;
                Alarms.Clear();
                // the same offsets mean the same registers only while address and quantity stay; a function switch keeps them
                if (d.Address != old.Address || d.Quantity != old.Quantity) v.CellFormats.Clear();
                foreach (int off in Doc.Tags.Keys.Where(k => k >= d.Quantity).ToList()) Doc.Tags.Remove(off);
            }
            Engine.UpdatePoll(PollId, d);
            Rebuild();
            MarkDirty();
            Fire();
        }

        /// <summary>Move/resize the window's register range while keeping aliases, tags and cell formats on the same
        /// absolute register (offset-keyed metadata would otherwise slide onto other registers).</summary>
        public void Reframe(int address, int quantity)
        {
            PollDefinition d = Doc.Definition.Clone();
            int shift = d.Address - address;
            Func<int, bool> inside = o => o + shift >= 0 && o + shift < quantity;
            var aliases = Doc.Aliases.Where(kv => inside(kv.Key)).ToDictionary(kv => kv.Key + shift, kv => kv.Value);
            var tags = Doc.Tags.Where(kv => inside(kv.Key)).ToDictionary(kv => kv.Key + shift, kv => kv.Value);
            var formats = Doc.View.CellFormats.Where(kv => inside(kv.Key)).ToDictionary(kv => kv.Key + shift, kv => kv.Value);
            d.Address = address;
            d.Quantity = quantity;
            d.Validate();
            ApplyDefinition(d, Doc.View);
            Doc.Aliases = aliases;
            Doc.Tags = tags;
            Doc.View.CellFormats = formats;
            Rebuild();
        }

        public void OpenDefinition()
        {
            using (var dlg = new DefinitionDialog(Doc.Definition, Doc.View, ApplyDefinition, () => Engine.ReadOnce(PollId)))
                dlg.ShowDialog(this);
        }

        public void SetEnabled(bool on)
        {
            PollDefinition d = Doc.Definition.Clone();
            d.Enabled = on;
            Doc.Definition = d;
            Engine.UpdatePoll(PollId, d);
            UpdateStatus();
            MarkDirty();
            Fire();
        }

        public void ReadOnce() { Engine.ReadOnce(PollId); }

        /// <summary>Applies to the selected value cells, or to the whole window when none/all are selected.</summary>
        public void SetDisplay(DisplayFormat fmt)
        {
            List<int> offsets = SelectedOffsets();
            if (offsets.Count > 0 && offsets.Count < Doc.Definition.Quantity)
            {
                foreach (int o in offsets) Doc.View.CellFormats[o] = fmt;
            }
            else
            {
                Doc.View.Display = fmt;
                Doc.View.CellFormats.Clear();
            }
            Rebuild();
            MarkDirty();
            Fire();
        }

        /// <summary>Use a format for one offset (Interpret As → "Use for cell").</summary>
        public void SetCellFormat(int offset, DisplayFormat fmt)
        {
            if (offset < 0 || offset >= Doc.Definition.Quantity) return;
            Doc.View.CellFormats[offset] = fmt;
            Rebuild();
            MarkDirty();
            Fire();
        }

        /// <summary>Pick any type × byte order (strings, BCD, bit fields, 64-bit…) for the selection or the window.</summary>
        public void ChooseFormat()
        {
            List<int> sel = SelectedOffsets();
            DisplayFormat current = sel.Count > 0 ? Doc.View.FormatAt(sel[0]) : Doc.View.Display;
            using (var dlg = new FormatDialog(current))
                if (dlg.ShowDialog(this) == DialogResult.OK) SetDisplay(dlg.Result);
        }

        /// <summary>Raised from the context menu; MainForm shows the Interpret As panel for this window.</summary>
        public event Action<PollWindow> InterpretRequested;

        /// <summary>Registers from the first selected offset (or 0) for the Interpret As panel; at most 64.</summary>
        public int[] RegistersAtSelection(out int offset)
        {
            List<int> sel = SelectedOffsets();
            offset = sel.Count > 0 ? sel[0] : 0;
            if (Values == null || Doc.Definition.IsBits || offset >= Values.Length) return null;
            return Values.Skip(offset).Take(DisplayFormat.MaxStringRegisters).ToArray();
        }

        public void SetPlcAddresses(bool on)
        {
            Doc.View.PlcAddresses = on;
            Rebuild();
            MarkDirty();
            Fire();
        }

        public void ResetCounters()
        {
            Engine.ResetStats(PollId);
            UpdateStatus();
        }

        void Fire()
        {
            var h = StateChanged;
            if (h != null) h();
        }

        // -- editing ------------------------------------------------------------------

        public List<int> SelectedOffsets()
        {
            return Grid.SelectedCells.Cast<DataGridViewCell>().Select(c => OffsetAt(c.ColumnIndex, c.RowIndex))
                       .Where(o => o >= 0).Distinct().OrderBy(o => o).ToList();
        }

        void OnCellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            // Value cells start editing from the bare value, without "40001 = ".
            if (e.ColumnIndex % 2 == 1 && Doc.View.AddressInCell)
            {
                string t = Convert.ToString(Grid[e.ColumnIndex, e.RowIndex].Value);
                int i = t.IndexOf('=');
                if (i >= 0) Grid[e.ColumnIndex, e.RowIndex].Value = t.Substring(i + 1).Trim();
            }
        }

        void OnCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (_painting) return;
            DataGridViewCell cell = Grid[e.ColumnIndex, e.RowIndex];
            string text = Convert.ToString(cell.Value);
            if (e.ColumnIndex % 2 == 0)
            {
                int aliasOffset = (e.ColumnIndex / 2) * Doc.View.Rows + e.RowIndex;
                if (aliasOffset < Doc.Definition.Quantity)
                {
                    Doc.Aliases[aliasOffset] = text;
                    MarkDirty();
                }
                return;
            }
            int offset = OffsetAt(e.ColumnIndex, e.RowIndex);
            if (offset < 0) return;
            PaintValues(); // show the last read value until the slave confirms
            try { WriteCell(offset, text); }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException) { Ui.Error(this, ex); }
        }

        /// <summary>Build the write PDU for a grid edit. Engineering input is converted back to raw through the tag
        /// (raw = (value - offset) / scale, rounded for integer types) and checked against the tag's write limits.
        /// A bit field is merged into the register's last read value and sent with FC06.</summary>
        public byte[] BuildCellWrite(int offset, string text, out string description)
        {
            PollDefinition d = Doc.Definition;
            int address = d.Address + offset;
            string label = Addressing.Label(d.Function, address, Doc.View.PlcAddresses);
            if (d.Function == (int)FC.ReadCoils)
            {
                int bit = Formats.ParseCoil(text);
                description = label + " := " + bit + (Values != null && offset < Values.Length ? "  (now " + Values[offset] + ")" : "");
                return Pdu.WriteSingleCoil(address, bit != 0);
            }
            if (d.Function != (int)FC.ReadHoldingRegisters) throw new FormatException("Discrete inputs and input registers are read-only");
            DisplayFormat fmt = Doc.View.FormatAt(offset);
            if (offset + fmt.Width > d.Quantity)
                throw new FormatException(fmt.Label + " needs " + fmt.Width + " registers; only " + (d.Quantity - offset) + " are in this window");
            Tag t = Doc.TagAt(offset);
            bool engineering = ShowsEngineering(offset);
            int[] words;
            if (engineering)
            {
                double input = Formats.ParseNumber(t.Unit.Length > 0 ? text.Replace(t.Unit, "") : text);
                if (t.WriteMin.HasValue && input < t.WriteMin.Value || t.WriteMax.HasValue && input > t.WriteMax.Value)
                    throw new FormatException(input.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is outside the tag write limits "
                                              + (t.WriteMin.HasValue ? t.WriteMin.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-∞") + " .. "
                                              + (t.WriteMax.HasValue ? t.WriteMax.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "+∞"));
                double raw = t.ToRaw(input);
                if (!fmt.IsFloat) raw = Math.Round(raw, MidpointRounding.AwayFromZero);
                words = Formats.Encode(raw, fmt);
            }
            else
            {
                words = Formats.Parse(text, fmt);
                double? raw = fmt.Type == DataType.String ? null : Formats.Number(words, 0, fmt);
                if (t != null && raw.HasValue && (t.WriteMin.HasValue && raw < t.WriteMin.Value || t.WriteMax.HasValue && raw > t.WriteMax.Value))
                    throw new FormatException(raw.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is outside the tag write limits");
            }
            string merged = "";
            if (fmt.Type == DataType.BitField)
            {
                if (Values == null || Stale || offset >= Values.Length)
                    throw new FormatException("A bit field write needs a current value of the register; wait for a successful poll");
                int mask = Formats.BitMask(fmt);
                words = new[] { (Values[offset] & ~mask) | (words[0] & mask) };
                merged = "  (other bits from last read 0x" + Values[offset].ToString("X4") + ")";
            }
            string now = Values != null && offset + fmt.Width <= Values.Length ? ValueText(offset) : "?";
            description = label + (t != null && t.Name.Length > 0 ? " (" + t.Name + ")" : "") + " := " + text.Trim()
                          + (engineering || fmt.Type == DataType.BitField ? "  → raw " + string.Join(" ", words) : "") + "  (now " + now + ")" + merged;
            return words.Length == 1 ? Pdu.WriteSingleRegister(address, words[0]) : Pdu.WriteMultipleRegisters(address, words);
        }

        /// <summary>Optional confirmation hook, set by MainForm when "Confirm writes" is on. Returns false to cancel.</summary>
        public Func<IWin32Window, string, bool> ConfirmWrite;

        async void WriteCell(int offset, string text)
        {
            byte[] pdu;
            string description;
            try { pdu = BuildCellWrite(offset, text, out description); }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException) { Ui.Error(this, ex); return; }
            if (ConfirmWrite != null && !ConfirmWrite(this, "Slave " + Doc.Definition.SlaveId + ": " + description)) return;
            try
            {
                await Engine.Write(Doc.Definition.SlaveId, pdu);
                Engine.ReadOnce(PollId);
            }
            catch (Exception ex)
            {
                if (!IsDisposed) Ui.Error(this, ex);
            }
        }

        void OnGridKeyDown(object sender, KeyEventArgs e)
        {
            // Space toggles coils, as in the original.
            if (e.KeyCode == Keys.Space && Doc.Definition.Function == (int)FC.ReadCoils && Values != null)
            {
                foreach (int o in SelectedOffsets())
                    if (o < Values.Length) WriteCell(o, Values[o] != 0 ? "0" : "1");
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Delete && Grid.CurrentCell != null && Grid.CurrentCell.ColumnIndex % 2 == 0)
            {
                foreach (DataGridViewCell c in Grid.SelectedCells)
                    if (c.ColumnIndex % 2 == 0 && !c.ReadOnly)
                    {
                        c.Value = "";
                        Doc.Aliases.Remove((c.ColumnIndex / 2) * Doc.View.Rows + c.RowIndex);
                    }
                MarkDirty();
                e.Handled = true;
            }
        }

        public void CopySelection()
        {
            DataObject data = Grid.GetClipboardContent();
            if (data != null) Clipboard.SetDataObject(data);
        }

        /// <summary>Paste newline/tab text into alias cells only (value cells cannot be pasted).</summary>
        public void PasteAliases()
        {
            if (!Clipboard.ContainsText() || Grid.CurrentCell == null || Grid.CurrentCell.ColumnIndex % 2 != 0) return;
            string[] lines = Clipboard.GetText().Replace("\r\n", "\n").Split('\n');
            int col = Grid.CurrentCell.ColumnIndex, row = Grid.CurrentCell.RowIndex;
            for (int i = 0; i < lines.Length && row + i < Grid.RowCount; i++)
            {
                if (i == lines.Length - 1 && lines[i].Length == 0) break;
                int offset = (col / 2) * Doc.View.Rows + row + i;
                if (offset >= Doc.Definition.Quantity) break;
                string alias = lines[i].Split('\t')[0];
                Grid[col, row + i].Value = alias;
                Doc.Aliases[offset] = alias;
            }
            MarkDirty();
        }

        ContextMenuStrip BuildContextMenu()
        {
            var m = new ContextMenuStrip();
            m.Items.Add("&Poll Setup…", null, (o, e) => OpenDefinition());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("&Copy", null, (o, e) => CopySelection());
            m.Items.Add("&Paste (aliases)", null, (o, e) => PasteAliases());
            m.Items.Add("Select &All", null, (o, e) => Grid.SelectAll());
            m.Items.Add(new ToolStripSeparator());
            var display = new ToolStripMenuItem("Display");
            foreach (DisplayFormat f in DisplayFormat.Presets)
                display.DropDownItems.Add(f.Label, null, (o, e) => SetDisplay(f));
            display.DropDownItems.Add(new ToolStripSeparator());
            display.DropDownItems.Add("Other &Format…", null, (o, e) => ChooseFormat());
            display.DropDownItems.Add("&Interpret As…", null, (o, e) => { var h = InterpretRequested; if (h != null) h(this); });
            m.Items.Add(display);
            m.Items.Add("&Foreground Color…", null, (o, e) => PickColor(true));
            m.Items.Add("&Background Color…", null, (o, e) => PickColor(false));
            m.Items.Add("F&ont…", null, (o, e) => PickFont());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("&Tag / Scaling / Alarms…", null, (o, e) => EditTag());
            m.Items.Add("Add to &Trend", null, (o, e) => { var h = TrendRequested; if (h != null) h(this, SelectedOffsets()); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("&Diagnose Last Error…", null, (o, e) => { var h = DiagnoseRequested; if (h != null) h(this); });
            m.Items.Add("Device &Settings…", null, (o, e) => { var h = SettingsRequested; if (h != null) h(this); });
            return m;
        }

        /// <summary>Raised with the selected offsets when the user picks "Add to Trend".</summary>
        public event Action<PollWindow, List<int>> TrendRequested;
        /// <summary>Raised from the context menu; MainForm opens Device Settings for the window's profile.</summary>
        public event Action<PollWindow> SettingsRequested;

        /// <summary>Edit the tag of the first selected offset; with several cells selected the result is applied to all
        /// of them (names are kept per cell).</summary>
        public void EditTag()
        {
            List<int> sel = SelectedOffsets();
            if (sel.Count == 0)
            {
                MessageBox.Show(this, "Select one or more value cells first.", "MultiPoll Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Tag current = Doc.TagAt(sel[0]) ?? new Tag();
            string label = Addressing.Label(Doc.Definition.Function, Doc.Definition.Address + sel[0], Doc.View.PlcAddresses)
                           + (sel.Count > 1 ? " (+" + (sel.Count - 1) + " more)" : "");
            using (var dlg = new TagDialog(label, current, sel.Count == 1))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                Tag t = dlg.Result;
                foreach (int o in sel)
                {
                    if (dlg.Clear) { Doc.Tags.Remove(o); Alarms.Remove(o); continue; }
                    Tag copy = t.Clone();
                    if (sel.Count > 1) copy.Name = Doc.TagAt(o) != null ? Doc.TagAt(o).Name : "";
                    Doc.Tags[o] = copy;
                    Alarms.Remove(o);
                }
            }
            if (Values != null) EvaluateAlarms(DateTime.UtcNow);
            PaintValues();
            UpdateStatus();
            MarkDirty();
        }

        public void ImportTagMap()
        {
            using (var d = new OpenFileDialog { Filter = "Tag map (*.csv)|*.csv|All files (*.*)|*.*" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { ImportTagMap(d.FileName); }
                catch (Exception ex) when (ex is IOException || ex is FormatException || ex is UnauthorizedAccessException)
                {
                    Ui.Error(this, new FormatException(System.IO.Path.GetFileName(d.FileName) + ": " + ex.Message));
                }
            }
        }

        /// <summary>Replace the tag map from CSV; names also become aliases where the alias cell is empty. Rows with a
        /// "format" set the cell format (other cell formats are kept).</summary>
        public void ImportTagMap(string path)
        {
            Dictionary<int, DisplayFormat> formats;
            Dictionary<int, Tag> tags = TagMapCsv.Load(path, Doc.Definition.Quantity, out formats);
            Doc.Tags = tags;
            foreach (var kv in formats) Doc.View.CellFormats[kv.Key] = kv.Value;
            foreach (var kv in tags)
            {
                string alias;
                if (kv.Value.Name.Length > 0 && (!Doc.Aliases.TryGetValue(kv.Key, out alias) || string.IsNullOrEmpty(alias)))
                    Doc.Aliases[kv.Key] = kv.Value.Name;
            }
            Alarms.Clear();
            Rebuild();
            if (Values != null) { EvaluateAlarms(DateTime.UtcNow); PaintValues(); }
            MarkDirty();
        }

        public void ExportTagMap()
        {
            using (var d = new SaveFileDialog { Filter = "Tag map (*.csv)|*.csv", FileName = "tagmap.csv" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { TagMapCsv.Save(d.FileName, ExportTags(), Doc.View.CellFormats); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Ui.Error(this, ex); }
            }
        }

        /// <summary>Tags for export; aliases without a tag become name-only rows so the map documents the whole window.</summary>
        public Dictionary<int, Tag> ExportTags()
        {
            var all = Doc.Tags.ToDictionary(kv => kv.Key, kv => kv.Value.Clone());
            foreach (var kv in Doc.Aliases)
            {
                if (string.IsNullOrEmpty(kv.Value) || kv.Key >= Doc.Definition.Quantity) continue;
                if (!all.ContainsKey(kv.Key)) all[kv.Key] = new Tag { Name = kv.Value };
                else if (all[kv.Key].Name.Length == 0) all[kv.Key].Name = kv.Value;
            }
            return all;
        }

        void PickColor(bool foreground)
        {
            using (var d = new ColorDialog())
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                foreach (DataGridViewCell c in Grid.SelectedCells)
                    if (foreground) c.Style.ForeColor = d.Color; else c.Style.BackColor = d.Color;
            }
        }

        void PickFont()
        {
            using (var d = new FontDialog { Font = Grid.DefaultCellStyle.Font })
                if (d.ShowDialog(this) == DialogResult.OK) Grid.DefaultCellStyle.Font = d.Font;
        }

        // -- function dialogs ---------------------------------------------------------------

        public void OpenFunction(FC fc)
        {
            if (!Engine.Running)
            {
                Ui.Error(this, new ModbusException(Status.NoConnection));
                return;
            }
            PollDefinition d = Doc.Definition;
            ViewOptions v = Doc.View;
            List<int> sel = SelectedOffsets();
            int off = sel.Count > 0 ? sel[0] : 0;
            int addr = d.Address + off;
            Form dlg;
            switch (fc)
            {
                case FC.WriteSingleCoil:
                    dlg = new WriteSingleCoilDialog(Engine, d.SlaveId, addr, v.PlcAddresses,
                                                    Values != null && off < Values.Length ? Values[off] : 1);
                    break;
                case FC.WriteSingleRegister:
                {
                    DisplayFormat fmt = d.IsBits ? DisplayFormat.Signed : v.FormatAt(off);
                    if (fmt.Type == DataType.BitField) fmt = DisplayFormat.Unsigned; // the dialog writes whole registers
                    string value = Values != null && !d.IsBits && off + fmt.Width <= Values.Length ? Formats.Format(Values, off, fmt) : null;
                    dlg = new WriteRegisterDialog(Engine, d.SlaveId, addr, v.PlcAddresses, fmt, value);
                    break;
                }
                case FC.WriteMultipleCoils:
                    dlg = new WriteCoilsDialog(Engine, d.SlaveId, d.Address, v.PlcAddresses, Math.Min(d.Quantity, Pdu.MaxWriteBits), Values);
                    break;
                case FC.WriteMultipleRegisters:
                    dlg = new WriteRegistersDialog(Engine, d.SlaveId, d.Address, v.PlcAddresses,
                                                   Math.Min(d.Quantity, Pdu.MaxWriteRegisters), d.IsBits ? null : Values);
                    break;
                case FC.MaskWriteRegister:
                    dlg = new MaskWriteDialog(Engine, d.SlaveId, addr, v.PlcAddresses);
                    break;
                case FC.ReadWriteMultipleRegisters:
                    dlg = new ReadWriteRegistersDialog(Engine, d.SlaveId, d.Address, v.PlcAddresses,
                                                       Math.Min(d.Quantity, Pdu.MaxRwWriteRegisters), d.IsBits ? null : Values);
                    break;
                default:
                    return;
            }
            dlg.Text += " — ID " + d.SlaveId + " | " + Engine.Settings.Describe();
            using (dlg) dlg.ShowDialog(this);
            Engine.ReadOnce(PollId);
        }

        // -- logging ----------------------------------------------------------------------

        public bool Logging { get { return _logger != null; } }

        public void StartLog()
        {
            using (var dlg = new LogDialog())
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                StartLog(dlg.Result());
            }
        }

        public void StartLog(LogSettings s)
        {
            StopLog();
            try { _logger = new DataLogger(s, Doc.View, Doc.TagAt); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) { Ui.Error(this, ex); }
            Fire();
        }

        public void StopLog()
        {
            if (_logger == null) return;
            _logger.Dispose();
            _logger = null;
            Fire();
        }

        // -- persistence ---------------------------------------------------------------------

        public bool Save(string path = null)
        {
            path = path ?? Path;
            if (path == null)
            {
                using (var d = new SaveFileDialog { Filter = MainForm.DocFilter, FileName = UntitledName + ".mpoll" })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return false;
                    path = d.FileName;
                }
            }
            try { Persistence.SaveDocument(path, Doc); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Ui.Error(this, ex); return false; }
            Path = path;
            MarkDirty(false);
            return true;
        }

        /// <summary>Returns false when the user cancels.</summary>
        public bool MaybeSave()
        {
            if (!Dirty) return true;
            DialogResult r = MessageBox.Show(this, "Save changes to " + DisplayTitle.TrimEnd(' ', '*') + "?", "MultiPoll Studio",
                                             MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            return r == DialogResult.No || Save();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !MaybeSave()) e.Cancel = true;
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopLog();
            Engine.RemovePoll(PollId);
            _statusTip.Dispose();
            base.OnFormClosed(e);
        }

        public void PrintTo(System.Drawing.Printing.PrintPageEventArgs e)
        {
            using (var bmp = new Bitmap(Width, Height))
            {
                DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                Rectangle area = e.MarginBounds;
                float scale = Math.Min(1f, Math.Min((float)area.Width / bmp.Width, (float)area.Height / bmp.Height));
                e.Graphics.DrawImage(bmp, area.Left, area.Top, bmp.Width * scale, bmp.Height * scale);
            }
        }
    }
}
