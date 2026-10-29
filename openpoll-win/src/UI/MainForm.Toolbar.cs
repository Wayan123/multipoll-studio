using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace MultiPollStudio.UI
{
    sealed partial class MainForm
    {
        readonly Dictionary<string, ToolStripMenuItem> _commands = new Dictionary<string, ToolStripMenuItem>();
        readonly Dictionary<string, string> _commandHelp = new Dictionary<string, string>();
        readonly List<Image> _toolbarImages = new List<Image>();
        ToolStrip _toolbar;
        ToolStripLabel _targetLabel;
        bool _contextHelp;

        ToolStrip BuildToolbar()
        {
            _toolbar = new ToolStrip { Name = "CommandToolbar", Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden,
                ImageScalingSize = new Size(20, 20), ShowItemToolTips = true, CanOverflow = true, AccessibleName = "Command toolbar" };
            Tool("new", "New", "Open a new polling window. Configure its unit ID and register range with Poll Setup.");
            Tool("open", "Open", "Open a saved document. Its ID, register map, tags and optional connection are restored.");
            Tool("save", "Save", "Save the active window, including its device ID, tags and connection.");
            Tool("print", "Print", "Open the print dialog for the active window. Nothing is printed until you confirm.");
            Tool("close", "Close", "Close only the active window. Unsaved changes prompt before closing.");
            Break();
            Tool("connect", "Connect", "Connect the active window's configured port or host. Windows sharing that connection also reconnect; other connections are unchanged.");
            Tool("disconnect", "Disconnect", "Disconnect the active window's connection. All windows sharing that connection stop communicating; other connections keep running. F4 disconnects ALL connections.");
            Break();
            Tool("definition", "Poll Setup", "Set the active window's unit ID, read function, register range, interval and display.");
            Tool("read", "Read once", "Send one read for the active window, including while its periodic polling is paused.");
            Tool("enabled", "Poll", "Toggle periodic polling for only the active window. Pausing does not disconnect or cancel a request already in flight.");
            Break();
            Tool("fc05", "05", "FC05: open Write Single Coil for the active ID and connection. No write occurs until Send; confirmation applies when enabled.");
            Tool("fc06", "06", "FC06: open Write Single Register for the active ID and connection. No write occurs until Send; confirmation applies when enabled.");
            Tool("fc15", "15", "FC15: open Write Multiple Coils for the active ID and connection. Check the address and values before Send.");
            Tool("fc16", "16", "FC16: open Write Multiple Registers for the active ID and connection. Check the address and values before Send.");
            Tool("fc22", "22", "FC22: open Mask Write Register. The AND/OR masks change selected bits. Check the active target before Send.");
            Tool("fc23", "23", "FC23: open Read/Write Multiple Registers. This function WRITES as well as reads and is blocked by write protection.");
            Break();
            Tool("traffic", "Traffic", "Show traffic from ALL connections. This is an observation window; opening it sends no request.");
            Tool("test", "Frame Console", "Open the raw-frame console on the active window's connection. Include the intended unit ID and correct framing. Raw sends do not use the write confirmation dialog; write protection and audit still apply.");
            Break();
            Tool("help", "Help", "Open the troubleshooting guide: status codes, likely causes and safe remedies.");
            Tool("context", "What's this?", "Enter help mode, then click a toolbar command to read its purpose without executing it. Escape cancels help mode.");
            _targetLabel = new ToolStripLabel { Name = "target", ForeColor = SystemColors.GrayText, Overflow = ToolStripItemOverflow.AsNeeded };
            _toolbar.Items.Add(new ToolStripSeparator());
            _toolbar.Items.Add(_targetLabel);
            FormClosed += (o, e) => { foreach (Image image in _toolbarImages) image.Dispose(); };
            return _toolbar;
        }

        void Break() { _toolbar.Items.Add(new ToolStripSeparator()); }

        void Tool(string id, string label, string help)
        {
            _commandHelp.Add(id, help);
            ToolStripMenuItem command = _commands[id];
            var button = new ToolStripButton(label) { Name = id, AccessibleName = command.Text.Replace("&", ""),
                AutoToolTip = false, DisplayStyle = id.StartsWith("fc") ? ToolStripItemDisplayStyle.Text : ToolStripItemDisplayStyle.Image,
                AutoSize = true, Padding = new Padding(3), Tag = command };
            if (!id.StartsWith("fc"))
            {
                Image icon = DrawToolIcon(id);
                _toolbarImages.Add(icon);
                button.Image = icon;
            }
            button.Click += (o, e) => command.PerformClick();
            _toolbar.Items.Add(button);
        }

        string TargetText(PollWindow w)
        {
            return "ID " + w.Doc.Definition.SlaveId + " | " + ConnectionOf(w).Describe();
        }

        void SyncToolbar()
        {
            if (_toolbar == null) return;
            PollWindow w = Active;
            string target = w == null ? "No active device" : TargetText(w);
            _targetLabel.Text = target;
            _targetLabel.ToolTipText = target;
            foreach (ToolStripButton button in _toolbar.Items.OfType<ToolStripButton>())
            {
                string id = button.Name;
                ToolStripMenuItem command = _commands[id];
                bool needsConnection = id == "read" || id == "test" || id.StartsWith("fc");
                if (needsConnection) command.Enabled = w != null && w.Engine.Running;
                if (id == "connect") command.Enabled = w != null && !w.Engine.Running;
                if (id == "disconnect") command.Enabled = w != null && w.Engine.Running;
                button.Enabled = command.Enabled;
                button.Checked = id == "enabled" ? w != null && w.Doc.Definition.Enabled : id == "context" && _contextHelp;
                string shortcut = command.ShortcutKeyDisplayString;
                if (string.IsNullOrEmpty(shortcut) && command.ShortcutKeys != Keys.None)
                    shortcut = new KeysConverter().ConvertToString(command.ShortcutKeys);
                bool targetsDevice = _windowItems.Contains(command);
                button.ToolTipText = command.Text.Replace("&", "") + (string.IsNullOrEmpty(shortcut) ? "" : " (" + shortcut + ")")
                    + (targetsDevice ? "\n" + target : "") + "\n" + _commandHelp[id]
                    + (id.StartsWith("fc") && w != null && w.Engine.WriteProtected ? "\nWRITE PROTECTED: Send will be refused." : "");
            }
        }

        void ConnectTarget(PollWindow w)
        {
            try { w.Engine.Start(ConnectionOf(w)); }
            catch (ModbusException ex)
            {
                foreach (PollWindow x in Windows.Where(x => x.Engine == w.Engine)) x.ShowFailure(ex.Status, ex.Detail);
            }
            SyncMenu();
        }

        void DisconnectTarget(PollWindow w)
        {
            w.Engine.Stop();
            foreach (PollWindow x in Windows.Where(x => x.Engine == w.Engine)) x.SetErrorText(Status.NoConnection.Text());
            SyncMenu();
        }

        void SetContextHelp(bool on)
        {
            _contextHelp = on;
            Cursor = on ? Cursors.Help : Cursors.Default;
            _hint.Text = on ? "Help mode: click a toolbar command; Escape cancels. No command will run." : "For Help, press F1.";
            SyncToolbar();
        }

        void ShowCommandHelp(string id)
        {
            SetContextHelp(false);
            string target = Active == null ? "No active device" : TargetText(Active);
            var dialog = new Form { Name = "CommandHelp", Text = _commands[id].Text.Replace("&", ""), Size = new Size(560, 270),
                StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, Font = Font };
            var text = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical,
                BackColor = SystemColors.Window, Text = _commandHelp[id] + "\r\n\r\nActive target: " + target
                + "\r\n\r\nWrite protection applies to writes, and non-read raw frames are audited. Standard write dialogs use confirmation when enabled; raw Frame Console sends do not." };
            dialog.Controls.Add(text);
            dialog.Show(this);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_contextHelp && keyData == Keys.Escape) { SetContextHelp(false); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        static Image DrawToolIcon(string id)
        {
            var image = new Bitmap(20, 20);
            using (Graphics g = Graphics.FromImage(image))
            using (var pen = new Pen(SystemColors.ControlText, 1.5f))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                switch (id)
                {
                    case "new": g.DrawRectangle(pen, 4, 2, 12, 16); g.DrawLine(pen, 7, 7, 13, 7); break;
                    case "open": g.DrawLines(pen, new[] { new Point(2, 16), new Point(2, 5), new Point(8, 5), new Point(10, 7), new Point(18, 7), new Point(15, 16), new Point(2, 16) }); break;
                    case "save": g.DrawRectangle(pen, 3, 2, 14, 16); g.DrawRectangle(pen, 6, 2, 8, 5); g.DrawRectangle(pen, 6, 11, 8, 7); break;
                    case "print": g.DrawRectangle(pen, 3, 7, 14, 8); g.DrawRectangle(pen, 6, 2, 8, 5); g.DrawRectangle(pen, 6, 11, 8, 7); break;
                    case "close": g.DrawLine(pen, 4, 4, 16, 16); g.DrawLine(pen, 16, 4, 4, 16); break;
                    case "connect": case "disconnect":
                        g.DrawRectangle(pen, 5, 6, 10, 7); g.DrawLine(pen, 7, 2, 7, 6); g.DrawLine(pen, 13, 2, 13, 6); g.DrawLine(pen, 10, 13, 10, 18);
                        if (id == "disconnect") g.DrawLine(pen, 2, 17, 18, 2); break;
                    case "definition": g.DrawRectangle(pen, 2, 3, 16, 14); for (int y = 7; y <= 13; y += 3) { g.DrawLine(pen, 5, y, 7, y); g.DrawLine(pen, 10, y, 15, y); } break;
                    case "read": g.DrawLines(pen, new[] { new Point(2, 15), new Point(6, 15), new Point(6, 4), new Point(13, 4), new Point(13, 15), new Point(18, 15) }); break;
                    case "enabled": g.DrawPolygon(pen, new[] { new Point(6, 3), new Point(16, 10), new Point(6, 17) }); break;
                    case "traffic": g.DrawLine(pen, 2, 6, 17, 6); g.DrawLine(pen, 13, 2, 17, 6); g.DrawLine(pen, 13, 10, 17, 6); g.DrawLine(pen, 3, 14, 18, 14); g.DrawLine(pen, 3, 14, 7, 10); g.DrawLine(pen, 3, 14, 7, 18); break;
                    case "test": g.DrawRectangle(pen, 2, 3, 16, 14); g.DrawLines(pen, new[] { new Point(5, 7), new Point(8, 10), new Point(5, 13) }); g.DrawLine(pen, 10, 13, 15, 13); break;
                    case "help": case "context":
                        g.DrawEllipse(pen, 2, 2, 16, 16);
                        using (var font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold))
                        using (var brush = new SolidBrush(SystemColors.ControlText)) g.DrawString("?", font, brush, 5, 2);
                        if (id == "context") g.DrawLines(pen, new[] { new Point(1, 10), new Point(1, 19), new Point(6, 15), new Point(1, 10) }); break;
                }
            }
            return image;
        }
    }
}
