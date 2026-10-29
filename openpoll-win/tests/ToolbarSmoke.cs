using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MultiPollStudio.UI;

namespace MultiPollStudio.Tests
{
    static partial class UiSmoke
    {
        static async Task Toolbar(MainForm main, string host, int port)
        {
            await Until(() => main.Windows.Any());
            ToolStrip bar = main.Controls.OfType<ToolStrip>().FirstOrDefault(x => x.Name == "CommandToolbar");
            Check("toolbar is available", bar != null);
            if (bar == null) return;
            Func<string, ToolStripItem> button = id => bar.Items[id];
            foreach (PollWindow w in main.Windows.ToList()) { w.Dirty = false; w.Close(); }
            Check("toolbar refuses a missing target", !button("fc06").Enabled && !button("read").Enabled && !button("save").Enabled);
            main.ConnSettings = new ConnectionSettings { Kind = ConnectionKind.SerialOverTcp, Host = host, TcpPort = port, ResponseTimeoutMs = 400, DelayBetweenPollsMs = 5 };
            main.QuickConnect();
            PollWindow soil = main.AddDevice(main.ProfileById("bgt-smps"), 4, false)[0];
            var tcp = new ConnectionSettings { Kind = ConnectionKind.Tcp, Host = host, TcpPort = port + 1, ResponseTimeoutMs = 400, DelayBetweenPollsMs = 5 };
            PollWindow xy = main.AddDevice(main.ProfileById("xy-md02"), 5, false, tcp).First(w => w.Doc.Definition.Function == 4);
            Check("toolbar fixtures answer on separate IDs and transports", await Until(() => soil.LastStatus == Status.Ok && xy.LastStatus == Status.Ok), soil.ErrorText + " / " + xy.ErrorText);
            soil.Activate();
            button("enabled").PerformClick();
            Check("toolbar pauses only active device", !soil.Doc.Definition.Enabled && xy.Doc.Definition.Enabled);
            xy.Activate();
            button("enabled").PerformClick();
            await Task.Delay(200);
            int soilTx = soil.Engine.Stats(soil.PollId).Tx, xyTx = xy.Engine.Stats(xy.PollId).Tx;
            soil.Activate();
            button("read").PerformClick();
            Check("toolbar one-shot reads active ID only", await Until(() => soil.Engine.Stats(soil.PollId).Tx > soilTx) && xy.Engine.Stats(xy.PollId).Tx == xyTx);
            xy.Activate();
            Check("toolbar exposes exact active target", button("fc06").ToolTipText.Contains("ID 5") && button("fc06").ToolTipText.Contains(":" + (port + 1)), button("fc06").ToolTipText);
            button("disconnect").PerformClick();
            Check("toolbar disconnect leaves other bus running", !xy.Engine.Running && soil.Engine.Running);
            Check("toolbar disables disconnected reads", !button("read").Enabled && !button("fc06").Enabled);
            button("connect").PerformClick();
            Check("toolbar reconnects active bus", await Until(() => xy.Engine.Running && button("read").Enabled));

            main.SetWriteProtected(true);
            Confirm.Enabled = false;
            foreach (string id in new[] { "fc05", "fc06", "fc15", "fc16", "fc22", "fc23" })
            {
                bool inspected = false;
                using (var timer = new Timer { Interval = 30 })
                {
                    timer.Tick += (o, e) =>
                    {
                        WriteDialog dialog = Application.OpenForms.OfType<WriteDialog>().FirstOrDefault();
                        if (dialog == null) return;
                        timer.Stop();
                        inspected = Field<PollEngine>(dialog, "Engine") == xy.Engine && Field<NumericUpDown>(dialog, "Slave").Value == 5;
                        if (id == "fc06")
                        {
                            Field<System.Windows.Forms.TextBox>(dialog, "_value").Text = "123";
                            Field<Button>(dialog, "SendButton").PerformClick();
                        }
                        dialog.Close();
                    };
                    timer.Start();
                    button(id).PerformClick();
                }
                Check("toolbar " + id + " targets active engine and ID", inspected);
            }
            var audited = Persistence.Parse(File.ReadAllLines(main.Engine.Audit.Path).Last());
            Check("toolbar protected attempt audited on active bus", Convert.ToInt32(audited["slave"]) == 5 && Convert.ToInt32(audited["fc"]) == 6
                && Convert.ToString(audited["target"]).Contains(":" + (port + 1)) && AuditLog.Verify(main.Engine.Audit.Path) == null);
            int[] config = (int[])await xy.Engine.Submit(m => m.Transact(5, Pdu.ReadRequest(3, 0x101, 2)));
            Check("toolbar protected attempt leaves device unchanged", config.SequenceEqual(new[] { 5, 9600 }));
            bool rawOk = false;
            using (var timer = new Timer { Interval = 30 })
            {
                int ticks = 0;
                timer.Tick += (o, e) =>
                {
                    TestCenterDialog dialog = Application.OpenForms.OfType<TestCenterDialog>().FirstOrDefault();
                    if (dialog == null) return;
                    if (ticks++ == 0)
                    {
                        Check("toolbar Test Center uses active connection", Field<PollEngine>(dialog, "_engine") == xy.Engine);
                        Field<ComboBox>(dialog, "_input").Text = "00 01 00 00 00 06 05 04 00 01 00 02";
                        ((Button)dialog.AcceptButton).PerformClick();
                    }
                    var log = Field<ListBox>(dialog, "_log");
                    string response = log.Items.Cast<string>().FirstOrDefault(s => s.Contains(" | RX | "));
                    if (response != null && !response.EndsWith("(no response)"))
                    {
                        byte[] rx = Hex.ParseSeparated(response.Substring(response.LastIndexOf('|') + 1));
                        rawOk = rx.Length == 13 && rx[6] == 5 && rx[7] == 4 && Pdu.Get16(rx, 9) == 305 && Pdu.Get16(rx, 11) == 546;
                        timer.Stop(); dialog.Close();
                    }
                    else if (ticks > 80) { timer.Stop(); dialog.Close(); }
                };
                timer.Start();
                button("test").PerformClick();
            }
            Check("toolbar Test Center reads XY values on own TCP connection", rawOk);
            xy.Activate();
            button("context").PerformClick();
            button("fc06").PerformClick();
            Check("context help never opens a write dialog", !Application.OpenForms.OfType<WriteDialog>().Any() && Application.OpenForms.Cast<Form>().Any(f => f.Name == "CommandHelp"));
            foreach (Form help in Application.OpenForms.Cast<Form>().Where(f => f.Name == "CommandHelp").ToList()) help.Close();
            button("enabled").PerformClick();
            soil.Activate(); button("enabled").PerformClick();
            main.LayoutMdi(MdiLayout.TileVertical);
            await Task.Delay(250); // let the native close animation finish before the screen capture
            Shot(main, "toolbar-multidevice.png");
            main.Width = 720;
            Shot(main, "toolbar-narrow.png");
            main.Width = 1100;
            string doc = Path.Combine(_outDir, "toolbar.mpoll");
            soil.Save(doc); soil.Dirty = false;
            button("close").PerformClick();
            PollWindow restored = main.OpenPath(doc);
            Check("renamed document preserves target and engineering units", restored.Doc.Definition.SlaveId == 4 && restored.Doc.TagAt(7).Scale == 0.01);
            main.Disconnect();
            Ui.AutoAnswer = null;
        }
    }
}
