using SFTracker.Services;
using Xunit;

namespace SFTracker.Tests;

public class UpdateServiceTests
{
    private static readonly Version Current = UpdateService.CurrentVersion;

    [Fact]
    public void IsNewer_PatchBump_ReturnsTrue()
    {
        var newer = new Version(Current.Major, Current.Minor, Current.Build + 1);
        Assert.True(UpdateService.IsNewer(newer.ToString()));
    }

    [Fact]
    public void IsNewer_MinorBump_ReturnsTrue()
    {
        var newer = new Version(Current.Major, Current.Minor + 1, 0);
        Assert.True(UpdateService.IsNewer(newer.ToString()));
    }

    [Fact]
    public void IsNewer_MajorBump_ReturnsTrue()
    {
        var newer = new Version(Current.Major + 1, 0, 0);
        Assert.True(UpdateService.IsNewer(newer.ToString()));
    }

    [Fact]
    public void IsNewer_SameVersion_ReturnsFalse()
    {
        Assert.False(UpdateService.IsNewer(Current.ToString()));
    }

    [Fact]
    public void IsNewer_OlderPatch_ReturnsFalse()
    {
        var build = Math.Max(0, Current.Build - 1);
        var older = new Version(Current.Major, Current.Minor, build);
        Assert.False(UpdateService.IsNewer(older.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-version")]
    [InlineData("abc.def")]
    public void IsNewer_InvalidString_ReturnsFalse(string version)
    {
        Assert.False(UpdateService.IsNewer(version));
    }
}
