using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SFTracker.Models;

namespace SFTracker.Services;

public class ApiService
{
    private const string BaseUrl = "https://satisfactory.kaffka.tech/api";
    private readonly HttpClient _http = new();

    public void SetToken(string token)
    {
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public void ClearToken()
    {
        _http.DefaultRequestHeaders.Authorization = null;
    }

    public async Task<string?> LoginAsync(string connectToken)
    {
        var resp = await _http.PostAsJsonAsync($"{BaseUrl}/auth/exchange", new { token = connectToken });
        if (!resp.IsSuccessStatusCode) return null;
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return doc.GetProperty("token").GetString();
    }

    public async Task<UserInfo?> GetMeAsync()
    {
        try
        {
            var resp = await _http.GetAsync($"{BaseUrl}/me/premium");
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<UserInfo>();
        }
        catch { return null; }
    }

    public async Task<List<World>> GetWorldsAsync()
    {
        try
        {
            var resp = await _http.GetAsync($"{BaseUrl}/worlds");
            if (!resp.IsSuccessStatusCode) return [];
            return await resp.Content.ReadFromJsonAsync<List<World>>() ?? [];
        }
        catch { return []; }
    }

    public async Task<SaveMetadata?> GetSaveMetadataAsync(int worldId)
    {
        try
        {
            var resp = await _http.GetAsync($"{BaseUrl}/worlds/{worldId}/save/metadata");
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<SaveMetadata>();
        }
        catch { return null; }
    }

    public async Task<string?> DownloadSaveAsync(int worldId, string targetDir, IProgress<double>? progress = null, bool uniqueName = false)
    {
        try
        {
            var resp = await _http.GetAsync($"{BaseUrl}/worlds/{worldId}/save/latest",
                HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return null;

            var cd = resp.Content.Headers.ContentDisposition?.FileName?.Trim('"');
            var baseName = string.IsNullOrEmpty(cd) ? $"world_{worldId}.sav" : cd;

            // Ніколи не перезаписуємо — додаємо timestamp якщо файл вже є
            string fullPath;
            if (uniqueName || File.Exists(Path.Combine(targetDir, baseName)))
            {
                var ts = DateTime.Now.ToString("yyyyMMdd-HHmm");
                var nameNoExt = Path.GetFileNameWithoutExtension(baseName);
                fullPath = Path.Combine(targetDir, $"{nameNoExt}_{ts}.sav");
            }
            else
            {
                fullPath = Path.Combine(targetDir, baseName);
            }

            var total = resp.Content.Headers.ContentLength ?? -1;
            long downloaded = 0;

            await using var stream = await resp.Content.ReadAsStreamAsync();
            await using var file = File.Create(fullPath);
            var buf = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buf)) > 0)
            {
                await file.WriteAsync(buf.AsMemory(0, read));
                downloaded += read;
                if (total > 0) progress?.Report((double)downloaded / total);
            }
            return fullPath;
        }
        catch { return null; }
    }

    public async Task<bool> UploadSaveAsync(int worldId, string filePath, IProgress<double>? progress = null)
    {
        try
        {
            await using var fileStream = File.OpenRead(filePath);
            using var content = new MultipartFormDataContent();
            using var streamContent = new StreamContent(fileStream);
            content.Add(streamContent, "file", Path.GetFileName(filePath));

            var resp = await _http.PostAsync($"{BaseUrl}/worlds/{worldId}/save", content);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<VersionInfo?> GetVersionAsync()
    {
        try
        {
            using var http = new HttpClient();
            var resp = await http.GetAsync($"{BaseUrl}/client/version");
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<VersionInfo>();
        }
        catch { return null; }
    }
}
