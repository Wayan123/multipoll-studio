using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MultiPollStudio.UI
{
    /// <summary>Per-window connections, device profiles, diagnosis and auto-fix.</summary>
    sealed partial class MainForm
    {
        /// <summary>Engines for windows with their own connection, keyed by port or host:port. One COM port has one
        /// owner, so windows on the same port share an engine and poll in turn; different ports poll in parallel.</summary>
        readonly Dictionary<string, PollEngine> _engines = new Dictionary<string, PollEngine>(StringComparer.OrdinalIgnoreCase);
        List<DeviceProfile> _profiles;
        List<string> _profileErrors = new List<string>();

        public IEnumerable<PollEngine> AllEngines { get { return new[] { Engine }.Concat(_engines.Values); } }

        public static string ConnectionKey(ConnectionSettings c)
        {
            return c.Kind == ConnectionKind.Serial ? c.Port.Trim().ToUpperInvariant() : c.Kind + "|" + c.Host.Trim().ToLowerInvariant() + ":" + c.TcpPort;
        }

        bool IsMainConnection(ConnectionSettings c) { return ConnectionKey(c) == ConnectionKey(ConnSettings); }

        /// <summary>The engine for a window connection; created and (optionally) started on first use. A connection on
        /// the main connection's port uses the main engine.</summary>
        public PollEngine EngineFor(ConnectionSettings c, bool start)
        {
            if (IsMainConnection(c)) return Engine;
            string key = ConnectionKey(c);
            PollEngine e;
            if (!_engines.TryGetValue(key, out e))
            {
                e = new PollEngine();
                e.OnTraffic = (d, b) => UiPost(() => TrafficWin.Add(d, b));
                string label = c.Describe();
                e.OnConnection = (up, text) => UiPost(() => { if (!up) TrafficWin.Error(label + ": " + text); _hint.Text = (up ? "Connected " : "") + text; SyncMenu(); });
                e.Audit = Engine.Audit;
                e.WriteProtected = Engine.WriteProtected;
                _engines[key] = e;
            }
            if (start && !e.Running)
            {
                try { e.Start(c); }
                catch (ModbusException ex) { _hint.Text = c.Describe() + ": " + ex.Describe(); }
            }
            return e;
        }

        void ReleaseUnusedEngines()
        {
            foreach (var kv in _engines.ToList())
            {
                if (kv.Value.PollCount > 0) continue;
                kv.Value.Stop();
                _engines.Remove(kv.Key);
            }
        }

        /// <summary>The connection a window uses.</summary>
        public ConnectionSettings ConnectionOf(PollWindow w) { return w.Doc.Connection ?? ConnSettings; }

        /// <summary>Change the connection a window uses (and every window sharing it). For the main connection this
        /// reconnects the main engine; for an own connection it restarts that engine.</summary>
        public void ChangeConnection(PollWindow w, ConnectionSettings c)
        {
            if (w.Doc.Connection == null)
            {
                ConnSettings = c;
                SaveConnSettings();
                QuickConnect();
                return;
            }
            string oldKey = ConnectionKey(w.Doc.Connection);
            foreach (PollWindow x in Windows.Where(x => x.Doc.Connection != null && ConnectionKey(x.Doc.Connection) == oldKey))
            {
                x.Doc.Connection = c.Clone();
                x.MarkDirty();
            }
            PollEngine e;
            if (_engines.TryGetValue(oldKey, out e))
            {
                e.Stop();
                _engines.Remove(oldKey);
                _engines[ConnectionKey(c)] = e;
                try { e.Start(c); }
                catch (ModbusException ex) { Ui.Error(this, ex); }
            }
            SyncMenu();
        }

        void EditWindowConnection(PollWindow w)
        {
            using (var d = new WindowConnectionDialog(w.Doc.Connection, ConnSettings))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                SetWindowConnection(w, d.Result);
            }
        }

        /// <summary>Give a window its own connection (null = back to the main connection).</summary>
        public void SetWindowConnection(PollWindow w, ConnectionSettings c)
        {
            w.Doc.Connection = c == null ? null : c.Clone();
            w.Rebind(c == null ? Engine : EngineFor(c, true));
            w.MarkDirty();
            ReleaseUnusedEngines();
            SyncMenu();
        }

        /// <summary>Make every window of the same device (same engine, same slave ID, same profile) poll a new ID.</summary>
        public void SetWindowsSlave(PollWindow w, int id)
        {
            int old = w.Doc.Definition.SlaveId;
            foreach (PollWindow x in Windows.Where(x => x == w || x.Engine == w.Engine && x.Doc.Definition.SlaveId == old && x.Doc.ProfileId == w.Doc.ProfileId))
            {
                PollDefinition d = x.Doc.Definition.Clone();
                d.SlaveId = id;
                if (x.Doc.Title.Length > 0) x.Doc.Title = x.Doc.Title.Replace(" ID " + old + " ", " ID " + id + " ");
                x.ApplyDefinition(d, x.Doc.View);
            }
        }

        // ---------------------------------------------------------------- profiles

        public List<DeviceProfile> DeviceProfiles
        {
            get
            {
                if (_profiles == null) _profiles = Profiles.LoadAll(out _profileErrors);
                return _profiles;
            }
        }

        public DeviceProfile ProfileById(string id) { return DeviceProfiles.FirstOrDefault(p => p.Id == id); }

        void AddDeviceDialog(string preselect)
        {
            _profiles = null; // reload: the user may have added files
            using (var d = new AddDeviceDialog(DeviceProfiles, _profileErrors))
            {
                if (preselect != null) d.Choose(preselect);
                if (d.ShowDialog(this) != DialogResult.OK) return;
                List<PollWindow> ws = AddDevice(d.Profile, d.SlaveId, d.ApplyLine, Active != null ? ConnectionOf(Active) : null);
                if (d.OpenSettings && ws.Count > 0) OpenDeviceSettings(ws[0]);
            }
        }

        /// <summary>Open one window per profile block. With <paramref name="applyLine"/> the connection's baud/parity
        /// are set to the profile defaults. Windows use <paramref name="connection"/> (own connection) or the main one.</summary>
        public List<PollWindow> AddDevice(DeviceProfile p, int slave, bool applyLine, ConnectionSettings connection = null)
        {
            var windows = new List<PollWindow>();
            ConnectionSettings own = connection != null && !IsMainConnection(connection) ? connection.Clone() : null;
            if (applyLine)
            {
                ConnectionSettings c = (own ?? ConnSettings).Clone();
                c.BaudRate = p.Baud; c.Parity = p.Parity; c.DataBits = p.DataBits; c.StopBits = p.StopBits;
                if (own != null) own = c;
                else if (!c.Kind.UsesMbap()) { ConnSettings = c; SaveConnSettings(); if (Engine.Running) QuickConnect(); }
            }
            foreach (PollDocument doc in p.Documents(slave))
            {
                doc.Connection = own;
                PollWindow w = AddWindow(new PollWindow(own != null ? EngineFor(own, true) : Engine, doc));
                w.MarkDirty();
                windows.Add(w);
            }
            LayoutMdi(MdiLayout.Cascade);
            return windows;
        }

        void OpenDeviceSettings(PollWindow w)
        {
            DeviceProfile p = ProfileById(w.Doc.ProfileId);
            if (p == null)
            {
                if (MessageBox.Show(this, "This window was not created from a device profile. Choose a profile now?", "Device Settings",
                                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK) AddDeviceDialog(null);
                return;
            }
            if (p.Settings.Count == 0 && p.Actions.Count == 0 && p.Recovery == null)
            {
                MessageBox.Show(this, p.Title + " has no documented settings.", "Device Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!w.Engine.Running) { Ui.Error(this, new ModbusException(Status.NoConnection)); return; }
            using (var d = new DeviceSettingsDialog(this, w, p)) d.ShowDialog(this);
        }

        void OpenProfileFolder()
        {
            string dir = Profiles.UserFolder();
            Directory.CreateDirectory(dir);
            Process.Start("explorer.exe", "\"" + dir + "\"");
        }

        // ---------------------------------------------------------------- find device

        void OpenFindDevice(PollWindow w)
        {
            ConnectionSettings c = w != null ? ConnectionOf(w) : ConnSettings;
            using (var d = CreateFindDevice(c))
                if (d.ShowDialog(this) == DialogResult.OK) ApplyHit(w, c, d.Chosen);
        }

        public FindDeviceDialog CreateFindDevice(ConnectionSettings c)
        {
            var d = new FindDeviceDialog(c) { Profiles = DeviceProfiles };
            var stopped = new List<PollEngine>();
            d.ReleasePort = port =>
            {
                foreach (PollEngine e in AllEngines.Where(e => e.Running && e.Settings != null && e.Settings.Kind == ConnectionKind.Serial
                                                                && string.Equals(e.Settings.Port, port, StringComparison.OrdinalIgnoreCase)))
                {
                    e.Stop();
                    stopped.Add(e);
                }
                return true;
            };
            d.RestorePort = port =>
            {
                // engines are restarted by ApplyHit (with the found settings) or here (unchanged) when nothing is used
                if (d.DialogResult == DialogResult.OK) { stopped.Clear(); return; }
                foreach (PollEngine e in stopped)
                {
                    try { e.Start(e == Engine ? ConnSettings : e.Settings); } catch (ModbusException) { }
                }
                stopped.Clear();
            };
            d.FormClosed += (o, e) =>
            {
                if (d.DialogResult == DialogResult.OK || stopped.Count == 0) return;
                foreach (PollEngine en in stopped) { try { en.Start(en == Engine ? ConnSettings : en.Settings); } catch (ModbusException) { } }
                stopped.Clear();
            };
            return d;
        }

        /// <summary>Apply a Find Device result: serial settings to the connection the window uses, and the slave ID to
        /// the window (or open a window when none is active).</summary>
        public void ApplyHit(PollWindow w, ConnectionSettings basis, LineHit hit)
        {
            ConnectionSettings c = basis.Clone();
            if (c.Kind != ConnectionKind.SerialOverTcp) c.Kind = ConnectionKind.Serial;
            c.BaudRate = hit.Baud; c.Parity = hit.Parity; c.StopBits = hit.StopBits; c.DataBits = 8;
            if (c.ResponseTimeoutMs < LineSearch.ProbeTimeoutMs(c.BaudRate) * 2) c.ResponseTimeoutMs = SerialTiming.SuggestTimeoutMs(c.BaudRate, 3, 10, c.Mode);
            if (w == null) w = NewWindow();
            if (w.Doc.Connection == null)
            {
                ConnSettings = c;
                SaveConnSettings();
                QuickConnect();
            }
            else ChangeConnection(w, c);
            PollDefinition d = w.Doc.Definition.Clone();
            d.SlaveId = hit.SlaveId;
            w.ApplyDefinition(d, w.Doc.View);
            _hint.Text = "Using " + c.Describe() + ", slave " + hit.SlaveId + (hit.Identified.Length > 0 ? " (" + hit.Identified + ")" : "");
        }

        // ---------------------------------------------------------------- diagnosis and fixes

        void Diagnose(PollWindow w)
        {
            PollResult f = w.LastFailure;
            if (f == null)
            {
                MessageBox.Show(this, "The last poll of this window succeeded; nothing to diagnose.", "Diagnose", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var d = CreateDiagnose(w)) d.ShowDialog(this);
        }

        public DiagnoseDialog CreateDiagnose(PollWindow w)
        {
            PollResult f = w.LastFailure ?? new PollResult { Status = Status.NoConnection, Definition = w.Doc.Definition, Detail = "" };
            ConnectionSettings c = w.Engine.Settings ?? ConnectionOf(w);
            List<Advice> advice = Diagnosis.For(f.Status, f.Detail, c, w.Doc.Definition);
            string error = f.Status.Text() + (f.Detail.Length > 0 && f.Detail != f.Status.Text() ? " (" + f.Detail + ")" : "");
            return new DiagnoseDialog(w.DisplayTitle.TrimEnd(' ', '*'), error, advice, a => RunFix(w, a));
        }

        /// <summary>Execute a one-click fix. None of the fixes writes to the device.</summary>
        public async Task<string> RunFix(PollWindow w, Advice a)
        {
            ConnectionSettings c = ConnectionOf(w).Clone();
            PollDefinition d = w.Doc.Definition.Clone();
            switch (a.Fix)
            {
                case FixKind.Reconnect:
                    if (w.Doc.Connection == null) QuickConnect(); else ChangeConnection(w, c);
                    return await Settled(w, "Reconnected");
                case FixKind.RaiseTimeout:
                    c.ResponseTimeoutMs = Math.Max(c.ResponseTimeoutMs, a.FixValue);
                    ChangeConnection(w, c);
                    return await Settled(w, "Response timeout set to " + c.ResponseTimeoutMs + " ms");
                case FixKind.RemoveEcho:
                    c.RemoveEcho = true;
                    ChangeConnection(w, c);
                    return await Settled(w, "Remove Echo turned on");
                case FixKind.SlowDown:
                    c.DelayBetweenPollsMs += 50;
                    d.ScanRateMs = Math.Min(3600000, d.ScanRateMs * 2);
                    w.ApplyDefinition(d, w.Doc.View);
                    ChangeConnection(w, c);
                    return await Settled(w, "Scan rate " + d.ScanRateMs + " ms, delay " + c.DelayBetweenPollsMs + " ms");
                case FixKind.UseResponder:
                    d.SlaveId = a.FixValue;
                    w.ApplyDefinition(d, w.Doc.View);
                    return await Settled(w, "Now polling slave " + d.SlaveId);
                case FixKind.SwitchFunction:
                    d.Function = d.Function == 3 ? 4 : 3;
                    w.ApplyDefinition(d, w.Doc.View);
                    return await Settled(w, "Now reading with FC" + d.Function.ToString("D2"));
                case FixKind.ShiftAddress:
                    w.Reframe(Math.Max(0, d.Address + a.FixValue), d.Quantity);
                    return await Settled(w, "Start address " + w.Doc.Definition.Address);
                case FixKind.ProbeRange:
                {
                    int slave = d.SlaveId, fc = d.Function, start = d.Address, n = d.Quantity;
                    var ok = (List<int>)await w.Engine.Submit(m =>
                    {
                        var readable = new List<int>();
                        for (int i = 0; i < n; i++)
                        {
                            try { m.Transact(slave, Pdu.ReadRequest(fc, start + i, 1)); readable.Add(start + i); }
                            catch (ModbusException ex) when ((int)ex.Status >= 0x80) { }
                        }
                        return readable;
                    });
                    if (ok.Count == 0) return "No address in " + start + ".." + (start + n - 1) + " can be read with FC" + fc.ToString("D2") + ". Try the other function code.";
                    int first = ok[0], run = 1;
                    while (run < ok.Count && ok[run] == first + run) run++;
                    w.Reframe(first, run);
                    return "Readable: " + string.Join(", ", Ranges(ok)) + ". Window set to " + first + ".." + (first + run - 1) + ". " + await Settled(w, "");
                }
                case FixKind.FindDevice:
                {
                    if (c.Kind != ConnectionKind.Serial)
                    {
                        // through a gateway: scan unit IDs with the current link
                        var hits = (List<ScanHit>)await w.Engine.Submit(m => Discovery.ScanUnits(m, 1, 247, d.Function, d.Address, 20, null, default(System.Threading.CancellationToken)));
                        if (hits.Count == 0) return "No unit ID answered through " + c.Describe() + ".";
                        d.SlaveId = hits[0].SlaveId;
                        w.ApplyDefinition(d, w.Doc.View);
                        return "Unit IDs answering: " + string.Join(", ", hits.Select(h => h.SlaveId)) + ". Now polling " + d.SlaveId + ". " + await Settled(w, "");
                    }
                    using (FindDeviceDialog f = CreateFindDevice(c))
                    {
                        if (f.ShowDialog(this) != DialogResult.OK) return "Search closed without choosing a device.";
                        ApplyHit(w, c, f.Chosen);
                        return "Using " + f.Chosen.Line + ". " + await Settled(w, "");
                    }
                }
                default:
                    return "";
            }
        }

        static IEnumerable<string> Ranges(List<int> addresses)
        {
            for (int i = 0; i < addresses.Count;)
            {
                int j = i;
                while (j + 1 < addresses.Count && addresses[j + 1] == addresses[j] + 1) j++;
                yield return i == j ? addresses[i].ToString() : addresses[i] + ".." + addresses[j];
                i = j + 1;
            }
        }

        /// <summary>Wait for the next poll result after a fix and report whether it worked.</summary>
        async Task<string> Settled(PollWindow w, string done)
        {
            int before = w.Engine.Running ? w.Engine.Stats(w.PollId).Tx : 0;
            w.ReadOnce();
            DateTime end = DateTime.Now.AddMilliseconds(Math.Max(3000, ConnectionOf(w).ResponseTimeoutMs * 2 + 1000));
            while (DateTime.Now < end)
            {
                await Task.Delay(100);
                if (!w.Engine.Running) continue;
                if (w.Engine.Stats(w.PollId).Tx > before && (w.LastStatus == Status.Ok || w.LastFailure != null)) break;
            }
            string prefix = done.Length > 0 ? done + ". " : "";
            if (w.LastStatus == Status.Ok) return prefix + "The device answers now.";
            return prefix + "Still failing: " + (w.LastFailure != null ? w.LastFailure.Status.Text() : Status.NoConnection.Text()) + ".";
        }

        void ShowGuide()
        {
            using (var f = new Form { Text = "Troubleshooting Guide", Size = new System.Drawing.Size(760, 600), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false })
            {
                var t = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = Ui.Mono(),
                                      Text = Diagnosis.Guide().Replace("\n", "\r\n"), BackColor = System.Drawing.SystemColors.Window };
                f.Controls.Add(t);
                f.ShowDialog(this);
            }
        }
    }
}
