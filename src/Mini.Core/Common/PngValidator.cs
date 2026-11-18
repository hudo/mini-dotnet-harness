using static Mini.Core.Common.PythonBytes;

// Python: tools.py png_damage
namespace Mini.Core.Common;

/// <summary>Checks a PNG's chunks before it is sent to the model.</summary>
public static class PngValidator
{
    /// <summary>What makes this PNG unreadable: a chunk failing its CRC, or no IEND. Checked before it's sent, since an image
    /// the API refuses fails every later request too (the transcript is never edited).</summary>
    // Python: tools.py png_damage
    public static string? Damage(byte[] data)
    {
        long i = 8;
        while (i + 12 <= data.Length)
        {
            var n = Be(data, (int)i, (int)i + 4);
            var kind = System.Text.Encoding.Latin1.GetString(data, (int)i + 4, 4);
            // Python's slices stop at the end of the data, so a length past it reads what is there (and a CRC of 0 bytes as 0).
            int body = (int)Math.Min(i + 8 + n, data.Length), crcEnd = (int)Math.Min(i + 12 + n, data.Length);
            if (Crc32(data.AsSpan((int)i + 4, body - ((int)i + 4))) != Be(data, body, crcEnd))
                return $"chunk {kind} at byte {i} fails its CRC check";
            if (kind == "IEND") return null;
            i += 12 + n;
        }
        return "it ends before its IEND chunk";
    }

    // zlib.crc32, by hand: System.IO.Hashing would be a new package.
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(k =>
    {
        var c = (uint)k;
        for (var b = 0; b < 8; b++) c = (c & 1) != 0 ? 0xEDB88320 ^ c >> 1 : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc32(ReadOnlySpan<byte> s)
    {
        var c = 0xFFFFFFFF;
        foreach (var b in s) c = CrcTable[(c ^ b) & 0xFF] ^ c >> 8;
        return c ^ 0xFFFFFFFF;
    }
}
