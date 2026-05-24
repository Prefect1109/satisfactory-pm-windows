using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using SFTracker.Models;

namespace SFTracker.Services;

public enum UpdateMode { None, Optional, Force }

public static class UpdateService
{
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 3, 0);

    public static bool IsNewer(string remoteVersion)
    {
        return Version.TryParse(remoteVersion, out var remote) && remote > CurrentVersion;
    }

    public static UpdateMode GetUpdateMode(VersionInfo? info, Version? current = null)
    {
        if (info == null) return UpdateMode.None;
        var cur = current ?? CurrentVersion;
        if (!Version.TryParse(info.Version, out var remote) || remote <= cur) return UpdateMode.None;
        return info.ForceUpdate ? UpdateMode.Force : UpdateMode.Optional;
    }

    public static async Task<bool> DownloadUpdateAsync(string url, IProgress<double>? progress = null)
    {
        try
        {
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath)!;
            var tmpPath = Path.Combine(exeDir, "SFTracker_new.exe");

            using var http = new HttpClient();
            using var resp = await http.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();

            var total = resp.Content.Headers.ContentLength ?? -1;
            long downloaded = 0;

            await using var stream = await resp.Content.ReadAsStreamAsync();
            await using var file = File.Create(tmpPath);
            var buf = new byte[65536];
            int read;
            while ((read = await stream.ReadAsync(buf)) > 0)
            {
                await file.WriteAsync(buf.AsMemory(0, read));
                downloaded += read;
                if (total > 0) progress?.Report((double)downloaded / total);
            }
            return true;
        }
        catch { return false; }
    }

    public static void ApplyUpdateAndRestart()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath)!;
        var exePath = Environment.ProcessPath!;
        var newPath = Path.Combine(exeDir, "SFTracker_new.exe");

        var script = $"""
            Start-Sleep -Seconds 2
            Move-Item -Force "{newPath}" "{exePath}"
            Start-Process "{exePath}"
            """;

        var tmpScript = Path.Combine(Path.GetTempPath(), "sft_update.ps1");
        File.WriteAllText(tmpScript, script);

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-ExecutionPolicy Bypass -NonInteractive -WindowStyle Hidden -File \"{tmpScript}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });

        Application.Current.Shutdown();
    }
}
