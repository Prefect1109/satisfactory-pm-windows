using System.IO;

namespace SFTracker.Services;

public static class AuthService
{
    private static readonly string TokenPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SFTracker", "token.txt");

    private static readonly string LastWorldPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SFTracker", "last_world.txt");

    public static string? LoadToken()
    {
        try
        {
            if (!File.Exists(TokenPath)) return null;
            var t = File.ReadAllText(TokenPath).Trim();
            return string.IsNullOrEmpty(t) ? null : t;
        }
        catch { return null; }
    }

    public static void SaveToken(string token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);
        File.WriteAllText(TokenPath, token);
    }

    public static void ClearToken()
    {
        try { File.Delete(TokenPath); } catch { }
    }

    public static string? LoadLastWorld()
    {
        try
        {
            if (!File.Exists(LastWorldPath)) return null;
            var s = File.ReadAllText(LastWorldPath).Trim();
            return string.IsNullOrEmpty(s) ? null : s;
        }
        catch { return null; }
    }

    public static void SaveLastWorld(string inviteCode)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LastWorldPath)!);
        File.WriteAllText(LastWorldPath, inviteCode);
    }

    private static readonly string SkipConfirmPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SFTracker", "skip_confirm.txt");

    public static bool LoadSkipConfirm()
    {
        try { return File.Exists(SkipConfirmPath) && File.ReadAllText(SkipConfirmPath).Trim() == "1"; }
        catch { return false; }
    }

    public static void SaveSkipConfirm(bool skip)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SkipConfirmPath)!);
        File.WriteAllText(SkipConfirmPath, skip ? "1" : "0");
    }

    private static readonly string AutoSyncPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SFTracker", "auto_sync.txt");

    public static bool LoadAutoSync()
    {
        try { return File.Exists(AutoSyncPath) && File.ReadAllText(AutoSyncPath).Trim() == "1"; }
        catch { return false; }
    }

    public static void SaveAutoSync(bool value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AutoSyncPath)!);
        File.WriteAllText(AutoSyncPath, value ? "1" : "0");
    }

    private static readonly string AutoStartAskedPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SFTracker", "autostart_asked.txt");

    public static bool WasAutoStartAsked()
    {
        try { return File.Exists(AutoStartAskedPath); }
        catch { return false; }
    }

    public static void MarkAutoStartAsked()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AutoStartAskedPath)!);
        File.WriteAllText(AutoStartAskedPath, "1");
    }

    private static readonly string RefreshTokenPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SFTracker", "refresh_token.txt");

    public static string? LoadRefreshToken()
    {
        try
        {
            if (!File.Exists(RefreshTokenPath)) return null;
            var t = File.ReadAllText(RefreshTokenPath).Trim();
            return string.IsNullOrEmpty(t) ? null : t;
        }
        catch { return null; }
    }

    public static void SaveRefreshToken(string token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RefreshTokenPath)!);
        File.WriteAllText(RefreshTokenPath, token);
    }

    public static void ClearRefreshToken()
    {
        try { File.Delete(RefreshTokenPath); } catch { }
    }
}
