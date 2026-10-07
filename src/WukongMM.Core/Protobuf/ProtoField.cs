using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace WukongMM.Core.Protobuf;

/// <summary>
/// protobuf 消息中的一个字段（无 schema）。
/// 从字节解析而来的字段会记住自己的原始编码；只要没被修改，序列化时就原样拷贝，保证无损。
/// length-delimited 字段可以按需"懒解析"成子消息。
/// </summary>
public sealed class ProtoField
{
    private ReadOnlyMemory<byte> _raw;      // 原始编码（tag + 值），仅在未修改时有效
    private ulong _scalar;                  // varint / fixed64 / fixed32 的值
    private ReadOnlyMemory<byte> _payload;  // length-delimited 的内容
    private ProtoMessage? _child;
    private bool _childParseFailed;
    private bool _dirty;

    internal ProtoField(int number, WireType wireType, ReadOnlyMemory<byte> raw, ulong scalar, ReadOnlyMemory<byte> payload)
    {
        Number = number;
        WireType = wireType;
        _raw = raw;
        _scalar = scalar;
        _payload = payload;
    }

    public int Number { get; }

    public WireType WireType { get; }

    /// <summary>自身或子消息是否被修改过。</summary>
    public bool IsDirty => _dirty || (_child?.IsDirty ?? false);

    public static ProtoField CreateVarint(int number, ulong value) =>
        new(number, WireType.Varint, default, value, default) { _dirty = true };

    public static ProtoField CreateFixed32(int number, uint value) =>
        new(number, WireType.Fixed32, default, value, default) { _dirty = true };

    public static ProtoField CreateFixed64(int number, ulong value) =>
        new(number, WireType.Fixed64, default, value, default) { _dirty = true };

    public static ProtoField CreateBytes(int number, ReadOnlyMemory<byte> payload) =>
        new(number, WireType.LengthDelimited, default, 0, payload) { _dirty = true };

    public static ProtoField CreateMessage(int number, ProtoMessage message) =>
        new(number, WireType.LengthDelimited, default, 0, default) { _child = message, _dirty = true };

    // ---------- 读取 ----------

    public ulong Varint
    {
        get
        {
            Require(WireType.Varint);
            return _scalar;
        }
    }

    /// <summary>按 int64 解释 varint（负数为 10 字节补码）。</summary>
    public long Int64 => unchecked((long)Varint);

    public uint Fixed32Raw
    {
        get
        {
            Require(WireType.Fixed32);
            return (uint)_scalar;
        }
    }

    public ulong Fixed64Raw
    {
        get
        {
            Require(WireType.Fixed64);
            return _scalar;
        }
    }

    public float Float => BitConverter.UInt32BitsToSingle(Fixed32Raw);

    public double Double => BitConverter.UInt64BitsToDouble(Fixed64Raw);

    /// <summary>length-delimited 字段的当前内容（若子消息被修改，返回重新编码后的字节）。</summary>
    public ReadOnlyMemory<byte> Bytes
    {
        get
        {
            Require(WireType.LengthDelimited);
            // 新建的消息字段没有 payload，或子消息被改过：都以子消息的当前编码为准
            return _child is not null && (_child.IsDirty || _payload.IsEmpty) ? _child.ToByteArray() : _payload;
        }
    }

    public string AsString() => Encoding.UTF8.GetString(Bytes.Span);

    /// <summary>把内容当作子消息解析（结果会缓存）。不是合法 protobuf 时返回 false。</summary>
    public bool TryGetMessage([NotNullWhen(true)] out ProtoMessage? message)
    {
        if (WireType != WireType.LengthDelimited)
        {
            message = null;
            return false;
        }
        if (_child is not null)
        {
            message = _child;
            return true;
        }
        if (!_childParseFailed && ProtoMessage.TryParse(_payload, out var parsed))
        {
            _child = parsed;
            message = parsed;
            return true;
        }
        _childParseFailed = true;
        message = null;
        return false;
    }

    public ProtoMessage Message =>
        TryGetMessage(out var m) ? m : throw new InvalidDataException(L.T($"字段 {Number} 不是合法的子消息", $"Field {Number} is not a valid sub-message"));

    // ---------- 修改 ----------

    public void SetVarint(ulong value)
    {
        Require(WireType.Varint);
        if (_scalar == value)
            return;
        _scalar = value;
        _dirty = true;
    }

    public void SetFixed32(uint value)
    {
        Require(WireType.Fixed32);
        if ((uint)_scalar == value)
            return;
        _scalar = value;
        _dirty = true;
    }

    public void SetFloat(float value) => SetFixed32(BitConverter.SingleToUInt32Bits(value));

    public void SetFixed64(ulong value)
    {
        Require(WireType.Fixed64);
        if (_scalar == value)
            return;
        _scalar = value;
        _dirty = true;
    }

    /// <summary>直接替换 length-delimited 内容（会丢弃已解析的子消息）。</summary>
    public void SetBytes(ReadOnlyMemory<byte> payload)
    {
        Require(WireType.LengthDelimited);
        _payload = payload;
        _child = null;
        _childParseFailed = false;
        _dirty = true;
    }

    // ---------- 序列化 ----------

    internal void WriteTo(ProtoWriter writer)
    {
        if (!IsDirty && !_raw.IsEmpty)
        {
            writer.WriteRaw(_raw.Span);
            return;
        }

        writer.WriteTag(Number, WireType);
        switch (WireType)
        {
            case WireType.Varint:
                writer.WriteVarint(_scalar);
                break;
            case WireType.Fixed64:
                writer.WriteFixed64(_scalar);
                break;
            case WireType.Fixed32:
                writer.WriteFixed32((uint)_scalar);
                break;
            case WireType.LengthDelimited:
                if (_child is not null && (_child.IsDirty || _payload.IsEmpty))
                {
                    var bytes = _child.ToByteArray();
                    writer.WriteVarint((ulong)bytes.Length);
                    writer.WriteRaw(bytes);
                }
                else
                {
                    writer.WriteVarint((ulong)_payload.Length);
                    writer.WriteRaw(_payload.Span);
                }
                break;
            default:
                throw new InvalidOperationException(L.T($"不支持的 wire type {WireType}", $"Unsupported wire type {WireType}"));
        }
    }

    /// <summary>强制下次序列化时重新编码（用于测试编码器与游戏编码是否一致）。</summary>
    internal void ForceReencode(bool recursive)
    {
        _dirty = true;
        if (recursive && TryGetMessage(out var child))
            child.ForceReencode(recursive: true);
    }

    private void Require(WireType expected)
    {
        if (WireType != expected)
            throw new InvalidOperationException(L.T($"字段 {Number} 的类型是 {WireType}，不是 {expected}", $"Field {Number} has type {WireType}, not {expected}"));
    }

    public override string ToString() => WireType switch
    {
        WireType.Varint => $"#{Number} varint {_scalar}",
        WireType.Fixed32 => $"#{Number} fixed32 {(uint)_scalar}",
        WireType.Fixed64 => $"#{Number} fixed64 {_scalar}",
        _ => $"#{Number} bytes[{_payload.Length}]",
    };
}
