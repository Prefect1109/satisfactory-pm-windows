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

            // Header layout:
            // int32 saveHeaderVersion
            // int32 saveVersion
            // int32 buildVersion
            // FString mapName
            // FString mapOptions
            // FString sessionName
            // int32 playDurationSeconds  ← this
            br.ReadInt32(); // saveHeaderVersion
            br.ReadInt32(); // saveVersion
            br.ReadInt32(); // buildVersion

            SkipFString(br); // mapName
            SkipFString(br); // mapOptions
            SkipFString(br); // sessionName

            return br.ReadInt32();
        }
        catch { return 0; }
    }

    // FString: positive length = UTF-8 (includes null), negative = UTF-16LE (abs chars, includes null)
    private static void SkipFString(BinaryReader br)
    {
        var len = br.ReadInt32();
        if (len == 0) return;
        if (len > 0)
            br.BaseStream.Seek(len, SeekOrigin.Current);
        else
            br.BaseStream.Seek(-len * 2, SeekOrigin.Current); // UTF-16LE chars
    }
}
