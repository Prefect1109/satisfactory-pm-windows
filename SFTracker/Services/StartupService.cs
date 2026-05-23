using Microsoft.Win32;

namespace SFTracker.Services;

public static class StartupService
{
    private const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "SFTracker";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegKey, false);
            return key?.GetValue(AppName) != null;
        }
        catch { return false; }
    }

    public static void Enable()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath == null) return;
            using var key = Registry.CurrentUser.OpenSubKey(RegKey, true);
            key?.SetValue(AppName, $"\"{exePath}\"");
        }
        catch { }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegKey, true);
            key?.DeleteValue(AppName, false);
        }
        catch { }
    }
}
