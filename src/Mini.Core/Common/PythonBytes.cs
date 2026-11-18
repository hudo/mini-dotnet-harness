using System.Buffers.Binary;

// Python: none (C#-only: Python's data[a:b] == s and int.from_bytes(data[a:b], ...), slicing that stops at the end of the data)
namespace Mini.Core.Common;

/// <summary>Reads of a byte array the way Python slices it: a slice cut short by the end of the data is shorter, not an error.</summary>
internal static class PythonBytes
{
    // data[at:at+len(s)] == s, for a Latin-1 string s; false if the data is too short.
    internal static bool Is(byte[] data, int at, string s)
    {
        if (data.Length < at + s.Length) return false;
        for (var k = 0; k < s.Length; k++) if (data[at + k] != s[k]) return false;
        return true;
    }

    internal static long Le(byte[] data, int a, int b) => Int(data, a, b, big: false);
    internal static long Be(byte[] data, int a, int b) => Int(data, a, b, big: true);

    // int.from_bytes(data[a:b], ...), unsigned: exact 2- and 4-byte reads use BinaryPrimitives; 3 bytes, or a slice cut
    // short by the end of the data, read as the bytes there are, like Python.
    private static long Int(byte[] data, int a, int b, bool big)
    {
        if (b <= data.Length)
        {
            var s = data.AsSpan(a, b - a);
            if (s.Length == 4) return big ? BinaryPrimitives.ReadUInt32BigEndian(s) : BinaryPrimitives.ReadUInt32LittleEndian(s);
            if (s.Length == 2) return big ? BinaryPrimitives.ReadUInt16BigEndian(s) : BinaryPrimitives.ReadUInt16LittleEndian(s);
        }
        long v = 0;
        for (var k = a; k < Math.Min(b, data.Length); k++) v = big ? v << 8 | data[k] : v | (long)data[k] << 8 * (k - a);
        return v;
    }
}
