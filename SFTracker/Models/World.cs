using System.Text.Json.Serialization;

namespace SFTracker.Models;

public class World
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("owner_id")]
    public int OwnerId { get; set; }

    [JsonPropertyName("owner_name")]
    public string? OwnerName { get; set; }

    [JsonPropertyName("member_count")]
    public int MemberCount { get; set; }
}

public class SaveMetadata
{
    [JsonPropertyName("world_id")]
    public int WorldId { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("md5")]
    public string? Md5 { get; set; }

    [JsonPropertyName("uploaded_at")]
    public string? UploadedAt { get; set; }

    [JsonPropertyName("uploaded_by")]
    public string? UploadedBy { get; set; }
}

public class UserInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("is_premium")]
    public bool IsPremium { get; set; }

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
