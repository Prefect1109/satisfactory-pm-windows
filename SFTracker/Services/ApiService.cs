using System.IO;
using System.Net;
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

    public event Action? SessionExpired;

    public void SetToken(string token)
    {
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public void ClearToken()
    {
        _http.DefaultRequestHeaders.Authorization = null;
    }

    // Обмінює one-time connect token → session + refresh token
    public async Task<(string session, string refresh)?> LoginAsync(string connectToken)
    {
        var resp = await _http.PostAsJsonAsync($"{BaseUrl}/auth/exchange", new { token = connectToken });
        if (!resp.IsSuccessStatusCode) return null;
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var session = doc.GetProperty("token").GetString();
        var refresh = doc.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        if (session == null) return null;
        return (session, refresh ?? "");
    }

    // Оновлює session token через refresh token (rotation)
    public async Task<(string session, string refresh)?> RefreshAsync(string refreshToken)
    {
        using var http = new HttpClient();
        var resp = await http.PostAsJsonAsync($"{BaseUrl}/auth/refresh", new { refresh_token = refreshToken });
        if (!resp.IsSuccessStatusCode) return null;
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var session = doc.GetProperty("token").GetString();
        var newRefresh = doc.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        if (session == null) return null;
        return (session, newRefresh ?? "");
    }

    public async Task<UserInfo?> GetMeAsync()
        => await SendWithRefresh(async () =>
        {
            var resp = await _http.GetAsync($"{BaseUrl}/me/premium");
            if (!resp.IsSuccessStatusCode) return (null, resp.StatusCode);
            return (await resp.Content.ReadFromJsonAsync<UserInfo>(), resp.StatusCode);
        });

    public async Task<List<World>> GetWorldsAsync()
    {
        try
        {
            var resp = await _http.GetAsync($"{BaseUrl}/worlds");
            if (resp.StatusCode == HttpStatusCode.Unauthorized) { await TryRefresh(); resp = await _http.GetAsync($"{BaseUrl}/worlds"); }
            if (!resp.IsSuccessStatusCode) return [];
            return await resp.Content.ReadFromJsonAsync<List<World>>() ?? [];
        }
        catch { return []; }
    }

    public async Task<SaveMetadata?> GetSaveMetadataAsync(string inviteCode)
    {
        try
        {
            var resp = await _http.GetAsync($"{BaseUrl}/worlds/{inviteCode}/save/metadata");
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<SaveMetadata>();
        }
        catch { return null; }
    }

    public async Task<string?> DownloadSaveAsync(string inviteCode, string targetFilePath, IProgress<double>? progress = null)
    {
        try
        {
            var resp = await _http.GetAsync($"{BaseUrl}/worlds/{inviteCode}/save/latest",
                HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return null;

            Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath)!);
            var total = resp.Content.Headers.ContentLength ?? -1;
            long downloaded = 0;

            await using var stream = await resp.Content.ReadAsStreamAsync();
            await using var file = File.Create(targetFilePath);
            var buf = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buf)) > 0)
            {
                await file.WriteAsync(buf.AsMemory(0, read));
                downloaded += read;
                if (total > 0) progress?.Report((double)downloaded / total);
            }
            return targetFilePath;
        }
        catch { return null; }
    }

    public async Task<bool> UploadSaveAsync(string inviteCode, string filePath, string uploadAsName, IProgress<double>? progress = null)
    {
        try
        {
            await using var fileStream = File.OpenRead(filePath);
            using var content = new MultipartFormDataContent();
            using var streamContent = new StreamContent(fileStream);
            content.Add(streamContent, "file", uploadAsName);

            var resp = await _http.PostAsync($"{BaseUrl}/worlds/{inviteCode}/save", content);
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

    // Намагається оновити session через refresh token. Якщо не вдалося — кидає SessionExpired.
    private async Task<bool> TryRefresh()
    {
        var rt = AuthService.LoadRefreshToken();
        if (string.IsNullOrEmpty(rt)) { SessionExpired?.Invoke(); return false; }

        var result = await RefreshAsync(rt);
        if (result == null) { SessionExpired?.Invoke(); return false; }

        var (newSession, newRefresh) = result.Value;
        AuthService.SaveToken(newSession);
        AuthService.SaveRefreshToken(newRefresh);
        SetToken(newSession);
        return true;
    }

    // Helper: виконує запит, при 401 намагається refresh і повторює один раз
    private async Task<T?> SendWithRefresh<T>(Func<Task<(T? result, HttpStatusCode status)>> send)
    {
        try
        {
            var (result, status) = await send();
            if (status != HttpStatusCode.Unauthorized) return result;
            if (!await TryRefresh()) return default;
            (result, _) = await send();
            return result;
        }
        catch { return default; }
    }
}
