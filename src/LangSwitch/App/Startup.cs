using Microsoft.Win32;
using System.Windows.Forms;

namespace LangSwitch.App;

internal static class Startup
{
    private const string Preferences = @"Software\LangSwitch";
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "LangSwitch";
    private const string StartupInitialized = "StartupInitialized";

    public static void Initialize()
    {
        using var settings = Registry.CurrentUser.CreateSubKey(Preferences);
        if (settings.GetValue(StartupInitialized) is null)
        {
            SetEnabled(true);
            settings.SetValue(StartupInitialized, 1);
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
