using Microsoft.Win32;
using System.ComponentModel;
using System.Drawing;
using System.IO;
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

        ApplicationConfiguration.Initialize();
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

internal sealed class TrayContext : ApplicationContext
{
    private readonly Control dispatcher = new();
    private readonly ContextMenuStrip menu = new();
    private readonly NotifyIcon icon;
    private readonly ToolStripMenuItem startupItem;
    private readonly DesktopInput input;
    private readonly Conversion conversion;
    private Task latest = Task.CompletedTask;

    public TrayContext(bool skipStartupRegistration)
    {
        nint clipboardWindow = dispatcher.Handle;
        conversion = new Conversion(clipboardWindow);
        input = new DesktopInput((origin, generation) =>
            dispatcher.BeginInvoke(new Action(() => latest = ConvertAsync(origin, generation))));

        var pause = new ToolStripMenuItem("Pause") { CheckOnClick = true };
        pause.CheckedChanged += (_, _) =>
        {
            input.Paused = pause.Checked;
            icon!.Text = pause.Checked ? "LangSwitch (paused)" : "LangSwitch";
        };
        startupItem = new ToolStripMenuItem("Start at sign-in");
        startupItem.Click += (_, _) => UpdateStartup(() => Startup.SetEnabled(!Startup.IsEnabled()));
        menu.Items.AddRange([pause, startupItem, new ToolStripSeparator(), new ToolStripMenuItem("Exit", null, (_, _) => CloseAsync())]);
        icon = new NotifyIcon { Icon = SystemIcons.Application, Text = "LangSwitch", ContextMenuStrip = menu, Visible = true };
        UpdateStartup(skipStartupRegistration ? static () => { } : Startup.Initialize);
    }

    private async Task ConvertAsync(DesktopTarget origin, long generation)
    {
        try
        {
            await conversion.RunAsync(origin, () => input.IsCurrent(generation));
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or TimeoutException)
        {
            // An editor can close or stop responding while a conversion is in flight.
        }
    }

    private async void CloseAsync()
    {
        input.Paused = true;
        icon.Visible = false;
        await Task.WhenAny(latest, Task.Delay(2000));
        ExitThread();
    }

    private void UpdateStartup(Action change)
    {
        try
        {
            change();
            startupItem.Checked = Startup.IsEnabled();
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            icon.ShowBalloonTip(3000, "LangSwitch", "Windows could not update the sign-in setting.", ToolTipIcon.Warning);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            input.Dispose();
            icon.Dispose();
            menu.Dispose();
            dispatcher.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal static class Startup
{
    private const string Preferences = @"Software\LangSwitch";
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "LangSwitch";

    public static void Initialize()
    {
        using var settings = Registry.CurrentUser.CreateSubKey(Preferences);
        if (settings.GetValue("StartupInitialized") is null)
        {
            SetEnabled(true);
            settings.SetValue("StartupInitialized", 1);
        }
        else if (IsEnabled())
        {
            SetEnabled(true);
        }
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Run);
        return key?.GetValue(Name) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Run);
        if (enabled)
            key.SetValue(Name, $"\"{Application.ExecutablePath}\"");
        else
            key.DeleteValue(Name, throwOnMissingValue: false);
    }
}
