using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace MultiPollStudio.UI
{
    /// <summary>Real-time trend of up to 12 values from any poll windows. Each point is timestamped when the result is
    /// applied; failed polls leave a gap (empty point) so lost communication is visible instead of a flat line.
    /// The history is a bounded ring per series. Uses the charting assembly that ships with .NET Framework.</summary>
    sealed class TrendWindow : Form
    {
        public const int MaxSeries = 12;

        sealed class Pen
        {
            public PollWindow Window;
            public int Offset;
            public Series Series;
            public string Label;
        }

        readonly Chart _chart = new Chart { Dock = DockStyle.Fill, BackColor = SystemColors.Window };
        readonly ChartArea _area = new ChartArea("main");
        readonly List<Pen> _pens = new List<Pen>();
        readonly ComboBox _window = Ui.Combo(new object[] { "1 min", "5 min", "15 min", "1 h", "8 h", "24 h" }, 0, width: 80);
        readonly CheckBox _pause = new CheckBox { Text = "&Pause", Appearance = Appearance.Button, AutoSize = true, MinimumSize = new Size(70, 26), TextAlign = ContentAlignment.MiddleCenter };
        static readonly int[] WindowSeconds = { 60, 300, 900, 3600, 8 * 3600, 24 * 3600 };
        const int MaxPoints = 20000;

        public TrendWindow()
        {
            Text = "Trend";
            Font = SystemFonts.MessageBoxFont;
            Size = new Size(820, 420);
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            _area.AxisX.LabelStyle.Format = "HH:mm:ss";
            _area.AxisX.IntervalAutoMode = IntervalAutoMode.VariableCount;
            _area.AxisX.MajorGrid.LineColor = Color.Gainsboro;
            _area.AxisY.MajorGrid.LineColor = Color.Gainsboro;
            _area.AxisY.IsStartedFromZero = false;
            _area.CursorX.IsUserEnabled = true;
            _area.CursorX.IsUserSelectionEnabled = true;
            _area.AxisX.ScaleView.Zoomable = true;
            _chart.ChartAreas.Add(_area);
            _chart.Legends.Add(new Legend("legend") { Docking = Docking.Bottom });
            var bar = Ui.Row(new Label { Text = "Window:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) }, _window, _pause,
                             Ui.Button("&Remove selected", (o, e) => RemoveLast()), Ui.Button("&Clear data", (o, e) => ClearData()),
                             Ui.Button("Export &CSV…", (o, e) => ExportCsv()), Ui.Button("Save &PNG…", (o, e) => SavePng()));
            bar.Dock = DockStyle.Top;
            Controls.Add(_chart);
            Controls.Add(bar);
            _window.SelectedIndexChanged += (o, e) => Rescale();
        }

        public int Count { get { return _pens.Count; } }
        public int PointCount(int i) { return _pens[i].Series.Points.Count; }

        /// <summary>Add pens for offsets of a window; duplicates are ignored. Returns how many were added.</summary>
        public int Add(PollWindow w, IEnumerable<int> offsets)
        {
            int added = 0;
            foreach (int off in offsets)
            {
                if (_pens.Count >= MaxSeries) break;
                if (_pens.Any(p => p.Window == w && p.Offset == off)) continue;
                Tag t = w.Doc.TagAt(off);
                string alias;
                w.Doc.Aliases.TryGetValue(off, out alias);
                string name = t != null && t.Name.Length > 0 ? t.Name : !string.IsNullOrEmpty(alias) ? alias
                    : Addressing.Label(w.Doc.Definition.Function, w.Doc.Definition.Address + off, w.Doc.View.PlcAddresses);
                string label = w.DisplayTitle.TrimEnd(' ', '*') + " · " + name + (t != null && t.Unit.Length > 0 ? " [" + t.Unit + "]" : "");
                var s = new Series(label)
                {
                    ChartType = w.Doc.Definition.IsBits ? SeriesChartType.StepLine : SeriesChartType.Line,
                    XValueType = ChartValueType.DateTime,
                    BorderWidth = 2,
                    MarkerStyle = MarkerStyle.Circle,
                    MarkerSize = 4,
                    EmptyPointStyle = { Color = Color.Transparent, MarkerStyle = MarkerStyle.None },
                };
                _chart.Series.Add(s);
                _pens.Add(new Pen { Window = w, Offset = off, Series = s, Label = label });
                added++;
            }
            return added;
        }

        public void RemoveWindow(PollWindow w)
        {
            foreach (Pen p in _pens.Where(p => p.Window == w).ToList())
            {
                _chart.Series.Remove(p.Series);
                _pens.Remove(p);
            }
        }

        void RemoveLast()
        {
            if (_pens.Count == 0) return;
            Pen p = _pens[_pens.Count - 1];
            _chart.Series.Remove(p.Series);
            _pens.Remove(p);
        }

        void ClearData() { foreach (Pen p in _pens) p.Series.Points.Clear(); }

        /// <summary>Feed one applied poll result. Timestamps come from the result (UTC), shown in local time.</summary>
        public void OnResult(PollWindow w, PollResult r)
        {
            if (_pause.Checked) return;
            DateTime local = r.Timestamp.ToLocalTime();
            foreach (Pen p in _pens)
            {
                if (p.Window != w) continue;
                double? v = r.Status == Status.Ok ? w.Engineering(p.Offset) : null;
                var pts = p.Series.Points;
                if (v.HasValue) pts.AddXY(local, v.Value);
                else
                {
                    int i = pts.AddXY(local, 0);
                    pts[i].IsEmpty = true;
                }
                while (pts.Count > MaxPoints) pts.RemoveAt(0);
            }
            Rescale();
        }

        void Rescale()
        {
            DateTime now = DateTime.Now;
            _area.AxisX.Minimum = now.AddSeconds(-WindowSeconds[_window.SelectedIndex]).ToOADate();
            _area.AxisX.Maximum = now.ToOADate();
            _area.RecalculateAxesScale();
        }

        void ExportCsv()
        {
            using (var d = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "trend.csv" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(d.FileName, ToCsv(), new UTF8Encoding(false));
            }
        }

        /// <summary>One row per point: timestamp (ISO-8601 local with offset), series, value (empty for gaps).</summary>
        public string ToCsv()
        {
            var sb = new StringBuilder("timestamp,series,value\n");
            foreach (Pen p in _pens)
                foreach (DataPoint pt in p.Series.Points)
                {
                    DateTime local = DateTime.SpecifyKind(DateTime.FromOADate(pt.XValue), DateTimeKind.Local);
                    sb.Append(DataLogger.Stamp(local.ToUniversalTime(), LogTime.LocalWithOffset)).Append(',')
                      .Append(TagMapCsv.Quote(p.Label)).Append(',')
                      .Append(pt.IsEmpty ? "" : pt.YValues[0].ToString("G7", CultureInfo.InvariantCulture)).Append('\n');
                }
            return sb.ToString();
        }

        void SavePng()
        {
            using (var d = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = "trend.png" })
                if (d.ShowDialog(this) == DialogResult.OK) _chart.SaveImage(d.FileName, ChartImageFormat.Png);
        }

        public void SavePng(string path) { _chart.SaveImage(path, ChartImageFormat.Png); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            base.OnFormClosing(e);
        }
    }
}
