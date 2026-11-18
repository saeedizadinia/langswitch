using LangSwitch.App;
using System.ComponentModel;
using System.Windows.Forms;

namespace LangSwitch;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var instance = new Mutex(true, @"Local\LangSwitch.Tray", out bool first);
        if (!first)
            return;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            using var tray = new TrayContext(args.Contains("--no-startup", StringComparer.Ordinal));
            Application.Run(tray);
        }
        catch (Win32Exception error)
        {
            MessageBox.Show($"LangSwitch could not start. {error.Message}", "LangSwitch",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
