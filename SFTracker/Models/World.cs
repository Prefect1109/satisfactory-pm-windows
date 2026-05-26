using System.Text.Json.Serialization;

namespace SFTracker.Models;

public class World
{
    [JsonPropertyName("invite_code")]
    public string InviteCode { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    public override string ToString() => Name;
}

public class SaveMetadata
{
    [JsonPropertyName("exists")]
    public bool Exists { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("hash")]
    public string? Hash { get; set; }

    [JsonPropertyName("session_name")]
    public string? SessionName { get; set; }

    [JsonPropertyName("play_time_sec")]
    public int PlayTimeSec { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }
}

public class UserInfo
{
    [JsonPropertyName("active")]
    public bool IsPremium { get; set; }

    [JsonPropertyName("until")]
    public string? Until { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("storage_used")]
    public long StorageUsed { get; set; }

    [JsonPropertyName("storage_limit")]
    public long StorageLimit { get; set; }
}

public class VersionInfo
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("force_update")]
    public bool ForceUpdate { get; set; }
}
