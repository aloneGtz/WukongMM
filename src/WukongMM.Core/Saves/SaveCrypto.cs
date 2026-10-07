using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace WukongMM.Core.Saves;

/// <summary>
/// 存档主体的加密：固定 8 字节循环 XOR（见 docs/SAVE_FORMAT.md §4）。加密与解密是同一个操作。
/// </summary>
public static class SaveCrypto
{
    public static ReadOnlySpan<byte> Key => [0x7B, 0x5C, 0xDA, 0x91, 0x3E, 0xFC, 0xDA, 0x37];

    /// <summary>原地异或。<paramref name="data"/> 的第 0 字节对应密钥第 0 字节。</summary>
    public static void Xor(Span<byte> data)
    {
        ulong key64 = BinaryPrimitives.ReadUInt64LittleEndian(Key);
        int blocks = data.Length / 8;
        var words = MemoryMarshal.Cast<byte, ulong>(data[..(blocks * 8)]);
        if (!BitConverter.IsLittleEndian)
            key64 = BinaryPrimitives.ReverseEndianness(key64);
        for (int i = 0; i < words.Length; i++)
            words[i] ^= key64;
        var key = Key;
        for (int i = blocks * 8; i < data.Length; i++)
            data[i] ^= key[i & 7];
    }
}
