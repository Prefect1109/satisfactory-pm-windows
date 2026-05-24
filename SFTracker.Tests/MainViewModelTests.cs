using System.Text;
using SFTracker.Models;
using SFTracker.ViewModels;
using Xunit;

namespace SFTracker.Tests;

// Tests for MainViewModel.ComparePlayTime (internal)
public class MainViewModelTests
{
    private static string CreateSaveWithPlayTime(int playTimeSec)
    {
        var path = Path.GetTempFileName() + ".sav";
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        bw.Write(0); bw.Write(0); bw.Write(0);
        bw.Write(0); bw.Write(0); bw.Write(0); // 3 empty FStrings
        bw.Write(playTimeSec);
        bw.Flush();
        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    private static SaveMetadata CloudMeta(int playTimeSec, string? updatedAt = null) => new()
    {
        Exists = true,
        PlayTimeSec = playTimeSec,
        UpdatedAt = updatedAt ?? DateTime.UtcNow.AddHours(-1).ToString("O")
    };

    [Fact]
    public void ComparePlayTime_LocalNewer_ReturnsPositive()
    {
        var path = CreateSaveWithPlayTime(7200);
        try
        {
            var (cmp, reason) = MainViewModel.ComparePlayTime(new FileInfo(path), CloudMeta(3600));
            Assert.Equal(1, cmp);
            Assert.Contains("Локальний", reason);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ComparePlayTime_CloudNewer_ReturnsNegative()
    {
        var path = CreateSaveWithPlayTime(1800);
        try
        {
            var (cmp, reason) = MainViewModel.ComparePlayTime(new FileInfo(path), CloudMeta(7200));
            Assert.Equal(-1, cmp);
            Assert.Contains("Хмарний", reason);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ComparePlayTime_Equal_ReturnsZero()
    {
        var path = CreateSaveWithPlayTime(3600);
        try
        {
            var (cmp, _) = MainViewModel.ComparePlayTime(new FileInfo(path), CloudMeta(3600));
            Assert.Equal(0, cmp);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ComparePlayTime_BothZeroPlayTime_FallsBackToDate_LocalNewer()
    {
        // Dedicated server saves have playTimeSec=0, so we fall back to file date
        var path = CreateSaveWithPlayTime(0);
        var fi = new FileInfo(path);
        // Cloud date is in the past
        var cloudDate = fi.LastWriteTime.AddHours(-2).ToString("O");
        try
        {
            var (cmp, reason) = MainViewModel.ComparePlayTime(fi, CloudMeta(0, cloudDate));
            Assert.Equal(1, cmp);
            Assert.Contains("Fallback", reason);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ComparePlayTime_BothZeroPlayTime_FallsBackToDate_CloudNewer()
    {
        var path = CreateSaveWithPlayTime(0);
        var fi = new FileInfo(path);
        var cloudDate = fi.LastWriteTime.AddHours(2).ToString("O");
        try
        {
            var (cmp, reason) = MainViewModel.ComparePlayTime(fi, CloudMeta(0, cloudDate));
            Assert.Equal(-1, cmp);
            Assert.Contains("Fallback", reason);
        }
        finally { File.Delete(path); }
    }
}
