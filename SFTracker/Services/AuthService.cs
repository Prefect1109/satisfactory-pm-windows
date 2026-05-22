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

    public static int? LoadLastWorld()
    {
        try
        {
            if (!File.Exists(LastWorldPath)) return null;
            var s = File.ReadAllText(LastWorldPath).Trim();
            return int.TryParse(s, out var id) ? id : null;
        }
        catch { return null; }
    }

    public static void SaveLastWorld(int worldId)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LastWorldPath)!);
        File.WriteAllText(LastWorldPath, worldId.ToString());
    }
}
