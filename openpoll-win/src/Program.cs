using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using MultiPollStudio.UI;

[assembly: AssemblyTitle("MultiPoll Studio")]
[assembly: AssemblyDescription("Open-source Modbus master test tool")]
[assembly: AssemblyProduct("MultiPoll Studio")]
[assembly: AssemblyCopyright("MultiPoll Studio contributors, GPL-3.0-or-later")]
[assembly: AssemblyVersion("0.5.0.0")]
[assembly: AssemblyFileVersion("0.5.0.0")]
[assembly: AssemblyInformationalVersion("0.5.0")]

namespace MultiPollStudio
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--selftest") return SelfTest.Run(args);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (o, e) =>
                MessageBox.Show(e.Exception.ToString(), "MultiPoll Studio: unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            var main = new MainForm();
            main.Shown += (o, e) =>
            {
                foreach (string a in args)
                {
                    if (!File.Exists(a)) continue;
                    if (a.EndsWith(".mpollw", StringComparison.OrdinalIgnoreCase)) main.LoadWorkspace(a);
                    else main.OpenPath(a);
                }
            };
            Application.Run(main);
            return 0;
        }
    }
}
