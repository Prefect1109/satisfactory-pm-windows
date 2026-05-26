using SFTracker.Models;
using SFTracker.Services;
using Xunit;

namespace SFTracker.Tests;

public class AutoUpdateTests
{
    private static readonly Version V100 = new(1, 0, 0);
    private static readonly Version V200 = new(2, 0, 0);

    [Fact]
    public void GetUpdateMode_NullInfo_ReturnsNone()
    {
        Assert.Equal(UpdateMode.None, UpdateService.GetUpdateMode(null, V100));
    }

    [Fact]
    public void GetUpdateMode_SameVersion_ReturnsNone()
    {
        var info = new VersionInfo { Version = "1.0.0", Url = "http://x", ForceUpdate = false };
        Assert.Equal(UpdateMode.None, UpdateService.GetUpdateMode(info, V100));
    }

    [Fact]
    public void GetUpdateMode_OlderRemote_ReturnsNone()
    {
        var info = new VersionInfo { Version = "0.9.0", Url = "http://x", ForceUpdate = false };
        Assert.Equal(UpdateMode.None, UpdateService.GetUpdateMode(info, V100));
    }

    [Fact]
    public void GetUpdateMode_NewerRemote_ForceTrue_ReturnsForce()
    {
        var info = new VersionInfo { Version = "2.0.0", Url = "http://x", ForceUpdate = true };
        Assert.Equal(UpdateMode.Force, UpdateService.GetUpdateMode(info, V100));
    }

    [Fact]
    public void GetUpdateMode_NewerRemote_ForceFalse_ReturnsOptional()
    {
        var info = new VersionInfo { Version = "2.0.0", Url = "http://x", ForceUpdate = false };
        Assert.Equal(UpdateMode.Optional, UpdateService.GetUpdateMode(info, V100));
    }

    [Fact]
    public void GetUpdateMode_InvalidVersionString_ReturnsNone()
    {
        var info = new VersionInfo { Version = "not-semver", Url = "http://x", ForceUpdate = true };
        Assert.Equal(UpdateMode.None, UpdateService.GetUpdateMode(info, V100));
    }

    [Fact]
    public void GetUpdateMode_EmptyVersionString_ReturnsNone()
    {
        var info = new VersionInfo { Version = "", Url = "http://x", ForceUpdate = false };
        Assert.Equal(UpdateMode.None, UpdateService.GetUpdateMode(info, V100));
    }

    [Fact]
    public void IsNewer_FourPartVersion_HandledCorrectly()
    {
        Assert.False(UpdateService.IsNewer($"{UpdateService.CurrentVersion}"));
    }

    [Theory]
    [InlineData("1.0.0.0")]
    [InlineData("1.0.0")]
    [InlineData("1.0")]
    public void IsNewer_VariousFormatsSameOrOlder_ReturnsFalse(string version)
    {
        var cur = new Version(2, 0, 0);
        var info = new VersionInfo { Version = version, Url = "http://x", ForceUpdate = false };
        Assert.Equal(UpdateMode.None, UpdateService.GetUpdateMode(info, cur));
    }

    [Fact]
    public void VersionInfo_Deserialize_ForceUpdateTrue()
    {
        var json = """{"version":"1.4.7","url":"https://example.com/SFT.exe","force_update":true}""";
        var info = System.Text.Json.JsonSerializer.Deserialize<VersionInfo>(json);
        Assert.NotNull(info);
        Assert.Equal("1.4.7", info.Version);
        Assert.Equal("https://example.com/SFT.exe", info.Url);
        Assert.True(info.ForceUpdate);
    }

    [Fact]
    public void VersionInfo_Deserialize_ForceUpdateFalse()
    {
        var json = """{"version":"2.0.0","url":"https://example.com/SFT.exe","force_update":false}""";
        var info = System.Text.Json.JsonSerializer.Deserialize<VersionInfo>(json);
        Assert.NotNull(info);
        Assert.False(info.ForceUpdate);
    }

    [Fact]
    public void VersionInfo_Deserialize_MissingForceUpdate_DefaultsFalse()
    {
        var json = """{"version":"2.0.0","url":"https://example.com/SFT.exe"}""";
        var info = System.Text.Json.JsonSerializer.Deserialize<VersionInfo>(json);
        Assert.NotNull(info);
        Assert.False(info.ForceUpdate);
    }

    [Fact]
    public void Scenario_CicdRelease_OldClientSeesOptionalUpdate()
    {
        var info = new VersionInfo { Version = "1.4.7", Url = "https://github.com/.../SFT.exe", ForceUpdate = false };
        var oldClient = new Version(1, 3, 5);
        Assert.Equal(UpdateMode.Optional, UpdateService.GetUpdateMode(info, oldClient));
    }

    [Fact]
    public void Scenario_CicdRelease_CurrentClientSeesNoUpdate()
    {
        var info = new VersionInfo { Version = "1.4.7", Url = "https://github.com/.../SFT.exe", ForceUpdate = false };
        var currentClient = new Version(1, 4, 7);
        Assert.Equal(UpdateMode.None, UpdateService.GetUpdateMode(info, currentClient));
    }

    [Fact]
    public void Scenario_ForceRollout_OldClientForcedUpdate()
    {
        var info = new VersionInfo { Version = "1.5.0", Url = "https://github.com/.../SFT.exe", ForceUpdate = true };
        var oldClient = new Version(1, 3, 5);
        Assert.Equal(UpdateMode.Force, UpdateService.GetUpdateMode(info, oldClient));
    }
}
