using static Mini.Core.Common.PythonBytes;

// Python: tools.py image_info
namespace Mini.Core.Common;

/// <summary>Tells what image a file is, and how big, from its header alone.</summary>
public static class ImageSniffer
{
    /// <summary>(media type, width, height) from the header, for the formats the API takes; 0x0 if the size can't be read.</summary>
    // Python: tools.py image_info
    public static (string Kind, int W, int H)? Read(byte[] data)
    {
        if (Is(data, 0, "\u0089PNG\r\n\u001a\n")) return ("image/png", Px(Be(data, 16, 20)), Px(Be(data, 20, 24)));
        if (Is(data, 0, "GIF87a") || Is(data, 0, "GIF89a")) return ("image/gif", Px(Le(data, 6, 8)), Px(Le(data, 8, 10)));
        if (Is(data, 0, "RIFF") && Is(data, 8, "WEBP"))  // three layouts: extended, lossy, lossless
        {
            if (Is(data, 12, "VP8X")) return ("image/webp", Px(Le(data, 24, 27) + 1), Px(Le(data, 27, 30) + 1));
            if (Is(data, 12, "VP8 ")) return ("image/webp", Px(Le(data, 26, 28) & 0x3FFF), Px(Le(data, 28, 30) & 0x3FFF));
            if (Is(data, 12, "VP8L"))
            {
                var bits = Le(data, 21, 25);  // packed: the size is masked out of the full 32 bits, so no clamping before this
                return ("image/webp", Px((bits & 0x3FFF) + 1), Px((bits >> 14 & 0x3FFF) + 1));
            }
            return ("image/webp", 0, 0);
        }
        if (!(data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)) return null;
        var i = 2;
        while (i + 9 <= data.Length && data[i] == 0xFF)  // JPEG: walk the segments to the start-of-frame, which has the size
        {
            int m = data[i + 1];
            if (m is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC)) return ("image/jpeg", Px(Be(data, i + 7, i + 9)), Px(Be(data, i + 5, i + 7)));
            i += m == 0xFF ? 1 : 2 + (int)Be(data, i + 2, i + 4);  // 0xFF: a fill byte
        }
        return ("image/jpeg", 0, 0);
    }

    // A size as int, clamped so a huge PNG width can't wrap negative and slip under the caller's size limit.
    // Only sizes go through here; the packed VP8L header is masked first.
    private static int Px(long v) => (int)Math.Min(v, int.MaxValue);
}
