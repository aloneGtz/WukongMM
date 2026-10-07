using System.Buffers;
using System.Buffers.Binary;

namespace WukongMM.Core.Protobuf;

/// <summary>protobuf 编码的底层写入器（总是输出规范编码）。</summary>
internal sealed class ProtoWriter
{
    private readonly ArrayBufferWriter<byte> _buffer;

    public ProtoWriter(int initialCapacity = 4096) => _buffer = new ArrayBufferWriter<byte>(initialCapacity);

    public int Length => _buffer.WrittenCount;

    public void WriteRaw(ReadOnlySpan<byte> bytes) => _buffer.Write(bytes);

    public void WriteVarint(ulong value)
    {
        Span<byte> tmp = stackalloc byte[10];
        int n = 0;
        while (value >= 0x80)
        {
            tmp[n++] = (byte)(value | 0x80);
            value >>= 7;
        }
        tmp[n++] = (byte)value;
        _buffer.Write(tmp[..n]);
    }

    public void WriteTag(int number, WireType wireType) =>
        WriteVarint(((ulong)(uint)number << 3) | (byte)wireType);

    public void WriteFixed32(uint value)
    {
        Span<byte> tmp = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(tmp, value);
        _buffer.Write(tmp);
    }

    public void WriteFixed64(ulong value)
    {
        Span<byte> tmp = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(tmp, value);
        _buffer.Write(tmp);
    }

    public byte[] ToArray() => _buffer.WrittenSpan.ToArray();

    internal static bool TryReadVarint(ReadOnlySpan<byte> data, ref int pos, out ulong value)
    {
        value = 0;
        int shift = 0;
        while (pos < data.Length)
        {
            if (shift > 63)
                return false; // 超过 10 字节
            byte b = data[pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return true;
            shift += 7;
        }
        return false;
    }
}
