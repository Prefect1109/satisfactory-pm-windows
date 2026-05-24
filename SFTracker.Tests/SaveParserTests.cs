using SFTracker.Services;
using Xunit;

namespace SFTracker.Tests;

public class SaveParserTests
{
    // Writes a minimal valid .sav header and returns a temp file path.
    private static string CreateSaveFile(int playTimeSec,
        string mapName = "", string mapOptions = "", string sessionName = "")
    {
        var path = Path.GetTempFileName();
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        bw.Write(0); // saveHeaderVersion
        bw.Write(0); // saveVersion
        bw.Write(0); // buildVersion
        WriteFString(bw, mapName);
        WriteFString(bw, mapOptions);
        WriteFString(bw, sessionName);
        bw.Write(playTimeSec);
        bw.Flush();

        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    private static void WriteFString(BinaryWriter bw, string s)
    {
        if (string.IsNullOrEmpty(s)) { bw.Write(0); return; }
        var bytes = Encoding.UTF8.GetBytes(s + "\0");
        bw.Write(bytes.Length);
        bw.Write(bytes);
    }

    private static string CreateSaveFileUtf16Session(int playTimeSec, string sessionName)
    {
        var path = Path.GetTempFileName();
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        bw.Write(0); bw.Write(0); bw.Write(0); // versions
        bw.Write(0); // mapName (empty)
        bw.Write(0); // mapOptions (empty)

        // UTF-16LE FString: negative len = char count (including null)
        var chars = (sessionName + "\0").ToCharArray();
        bw.Write(-chars.Length);
        foreach (var c in chars) bw.Write((ushort)c);

        bw.Write(playTimeSec);
        bw.Flush();

        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    [Fact]
    public void ReadPlayTimeSec_ValidHeader_ReturnsCorrectValue()
    {
        var path = CreateSaveFile(7200);
        try
        {
            Assert.Equal(7200, SaveParser.ReadPlayTimeSec(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadPlayTimeSec_WithStringFields_ParsesCorrectly()
    {
        var path = CreateSaveFile(3661, "Persistent_Level", "?SessionName=Iron?", "IronMine");
        try
        {
            Assert.Equal(3661, SaveParser.ReadPlayTimeSec(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadPlayTimeSec_Utf16SessionName_ParsesCorrectly()
    {
        var path = CreateSaveFileUtf16Session(1800, "Сесія");
        try
        {
            Assert.Equal(1800, SaveParser.ReadPlayTimeSec(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadPlayTimeSec_EmptyFile_ReturnsZero()
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, []);
        try
        {
            Assert.Equal(0, SaveParser.ReadPlayTimeSec(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadPlayTimeSec_NonExistentFile_ReturnsZero()
    {
        Assert.Equal(0, SaveParser.ReadPlayTimeSec("/no/such/file.sav"));
    }

    [Theory]
    [InlineData(0, "—")]
    [InlineData(-1, "—")]
    [InlineData(59, "0хв")]
    [InlineData(60, "1хв")]
    [InlineData(3600, "1г 0хв")]
    [InlineData(3661, "1г 1хв")]
    [InlineData(7384, "2г 3хв")]
    public void FormatPlayTime_VariousInputs_FormatsCorrectly(int sec, string expected)
    {
        Assert.Equal(expected, SaveParser.FormatPlayTime(sec));
    }
}
