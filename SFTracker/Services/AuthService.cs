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

    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SFTracker");

    private static readonly string AutoSyncPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SFTracker", "auto_sync.txt");

    private static readonly string CustomSavePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SFTracker", "custom_save_path.txt");

    public static bool LoadAutoSync()
    {
        try { return !File.Exists(AutoSyncPath) || File.ReadAllText(AutoSyncPath).Trim() == "1"; }
        catch { return true; }
    }

    public static void SaveAutoSync(bool enabled)
    {
        Directory.CreateDirectory(AppDataDir);
        File.WriteAllText(AutoSyncPath, enabled ? "1" : "0");
    }

    public static string? LoadCustomSaveFolder()
    {
        try
        {
            if (!File.Exists(CustomSavePath)) return null;
            var p = File.ReadAllText(CustomSavePath).Trim();
            return string.IsNullOrEmpty(p) ? null : p;
        }
        catch { return null; }
    }

    public static void SaveCustomSaveFolder(string? path)
    {
        Directory.CreateDirectory(AppDataDir);
        File.WriteAllText(CustomSavePath, path ?? "");
    }

    private static readonly string RunInBackgroundPath = Path.Combine(AppDataDir, "run_in_background.txt");

    public static bool LoadRunInBackground()
    {
        try { return File.Exists(RunInBackgroundPath) && File.ReadAllText(RunInBackgroundPath).Trim() == "1"; }
        catch { return false; }
    }

    public static void SaveRunInBackground(bool enabled)
    {
        Directory.CreateDirectory(AppDataDir);
        File.WriteAllText(RunInBackgroundPath, enabled ? "1" : "0");
    }
}
