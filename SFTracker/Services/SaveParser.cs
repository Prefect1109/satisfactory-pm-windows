using System.IO;
using System.Text;

namespace SFTracker.Services;

public static class SaveParser
{
    /// <summary>
    /// Reads play duration in seconds from a Satisfactory .sav file header.
    /// Returns 0 if parsing fails or file is unreadable.
    /// </summary>
    public static int ReadPlayTimeSec(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var br = new BinaryReader(fs);
            br.ReadInt32(); br.ReadInt32(); br.ReadInt32(); // saveHeaderVersion, saveVersion, buildVersion
            SkipFString(br); // mapName
            SkipFString(br); // mapOptions
            SkipFString(br); // sessionName
            return br.ReadInt32();
        }
        catch { return 0; }
    }

    public static string? ReadSessionName(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var br = new BinaryReader(fs);
            br.ReadInt32(); br.ReadInt32(); br.ReadInt32(); // saveHeaderVersion, saveVersion, buildVersion
            SkipFString(br); // mapName
            SkipFString(br); // mapOptions
            return ReadFString(br); // sessionName
        }
        catch { return null; }
    }

    // FString: positive length = UTF-8 (includes null), negative = UTF-16LE (abs chars, includes null)
    private static string? ReadFString(BinaryReader br)
    {
        var len = br.ReadInt32();
        if (len == 0) return null;
        if (len > 0)
        {
            var bytes = br.ReadBytes(len);
            return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        }
        else
        {
            var bytes = br.ReadBytes(-len * 2);
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }
    }

    private static void SkipFString(BinaryReader br)
    {
        var len = br.ReadInt32();
        if (len == 0) return;
        br.BaseStream.Seek(len > 0 ? len : -len * 2, SeekOrigin.Current);
    }
}
