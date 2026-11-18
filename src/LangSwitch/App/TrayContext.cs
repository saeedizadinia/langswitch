using LangSwitch.Conversion;
using LangSwitch.Windows;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Security;
using System.Windows.Forms;

namespace LangSwitch.App;

internal sealed class TrayContext : ApplicationContext
{
    private static readonly TimeSpan ExitGracePeriod = TimeSpan.FromSeconds(2);

    private readonly Control dispatcher = new();
    private readonly ContextMenuStrip menu = new();
    private readonly NotifyIcon icon;
    private readonly Icon activeIcon;
    private readonly Icon pausedIcon;
    private readonly ToolStripMenuItem startupItem;
    private readonly DesktopInput input;
    private readonly ConversionCoordinator conversion;
    private Task latestConversion = Task.CompletedTask;

    public TrayContext(bool skipStartupRegistration)
    {
        conversion = new ConversionCoordinator(dispatcher.Handle);
        input = new DesktopInput(QueueConversion);
        activeIcon = LoadTrayIcon("LangSwitch.Tray.Active.ico");
        pausedIcon = LoadTrayIcon("LangSwitch.Tray.Paused.ico");
        icon = new NotifyIcon { Icon = activeIcon, Text = "LangSwitch", ContextMenuStrip = menu };

        var pause = new ToolStripMenuItem("Pause") { CheckOnClick = true };
        pause.CheckedChanged += (_, _) =>
        {
            input.Paused = pause.Checked;
            icon.Icon = pause.Checked ? pausedIcon : activeIcon;
            icon.Text = pause.Checked ? "LangSwitch (paused)" : "LangSwitch";
        };
        startupItem = new ToolStripMenuItem("Start at sign-in");
        startupItem.Click += (_, _) => UpdateStartup(() => Startup.SetEnabled(!Startup.IsEnabled()));
        menu.Items.AddRange([pause, startupItem, new ToolStripSeparator(), new ToolStripMenuItem("Exit", null, (_, _) => CloseAsync())]);
        icon.Visible = true;
        UpdateStartup(skipStartupRegistration ? static () => { } : Startup.Initialize);
    }

    private static Icon LoadTrayIcon(string resourceName)
    {
        using var stream = typeof(TrayContext).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing tray icon resource {resourceName}.");
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    private void QueueConversion(DesktopTarget origin, long generation)
    {
        dispatcher.BeginInvoke(() =>
        {
            latestConversion = ConvertAsync(origin, generation);
        });
    }

    private async Task ConvertAsync(DesktopTarget origin, long generation)
    {
        try
        {
            await conversion.ConvertAfterSwitchAsync(origin, () => input.IsCurrent(generation));
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
        await Task.WhenAny(latestConversion, Task.Delay(ExitGracePeriod));
        ExitThread();
    }

    private void UpdateStartup(Action change)
    {
        try
        {
            change();
            startupItem.Checked = Startup.IsEnabled();
        }
        catch (Exception error) when (error is UnauthorizedAccessException or SecurityException or IOException)
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
            activeIcon.Dispose();
            pausedIcon.Dispose();
            menu.Dispose();
            dispatcher.Dispose();
        }
        base.Dispose(disposing);
    }
}
