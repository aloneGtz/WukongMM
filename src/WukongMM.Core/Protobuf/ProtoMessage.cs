using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace WukongMM.Core.Protobuf;

/// <summary>
/// 无 schema 的 protobuf 消息：有序字段列表。保留原始字段顺序与未知字段，
/// 未修改的部分序列化时逐字节不变。
/// </summary>
public sealed class ProtoMessage
{
    /// <summary>protobuf 允许的最大字段号 (2^29 - 1)。</summary>
    public const int MaxFieldNumber = 536_870_911;

    private readonly List<ProtoField> _fields;
    private bool _structureDirty;

    /// <summary>新建一个空消息（视为已修改）。</summary>
    public ProtoMessage()
    {
        _fields = [];
        _structureDirty = true;
    }

    private ProtoMessage(List<ProtoField> fields) => _fields = fields;

    public IReadOnlyList<ProtoField> Fields => _fields;

    public bool IsDirty
    {
        get
        {
            if (_structureDirty)
                return true;
            foreach (var f in _fields)
                if (f.IsDirty)
                    return true;
            return false;
        }
    }

    // ---------- 解析 ----------

    public static ProtoMessage Parse(ReadOnlyMemory<byte> data) =>
        TryParse(data, out var msg, out var error) ? msg : throw new InvalidDataException(error);

    public static bool TryParse(ReadOnlyMemory<byte> data, [NotNullWhen(true)] out ProtoMessage? message) =>
        TryParse(data, out message, out _);

    public static bool TryParse(ReadOnlyMemory<byte> data, [NotNullWhen(true)] out ProtoMessage? message, out string? error)
    {
        var span = data.Span;
        var fields = new List<ProtoField>();
        int pos = 0;
        message = null;
        while (pos < span.Length)
        {
            int start = pos;
            if (!ProtoWriter.TryReadVarint(span, ref pos, out ulong tag))
            {
                error = L.T($"偏移 {start}: tag 截断", $"offset {start}: truncated tag");
                return false;
            }
            ulong number = tag >> 3;
            var wireType = (WireType)(tag & 7);
            if (number == 0 || number > MaxFieldNumber)
            {
                error = L.T($"偏移 {start}: 非法字段号 {number}", $"offset {start}: invalid field number {number}");
                return false;
            }

            ulong scalar = 0;
            ReadOnlyMemory<byte> payload = default;
            switch (wireType)
            {
                case WireType.Varint:
                    if (!ProtoWriter.TryReadVarint(span, ref pos, out scalar))
                    {
                        error = L.T($"偏移 {start}: varint 截断", $"offset {start}: truncated varint");
                        return false;
                    }
                    break;
                case WireType.Fixed64:
                    if (span.Length - pos < 8)
                    {
                        error = L.T($"偏移 {start}: fixed64 截断", $"offset {start}: truncated fixed64");
                        return false;
                    }
                    scalar = BinaryPrimitives.ReadUInt64LittleEndian(span[pos..]);
                    pos += 8;
                    break;
                case WireType.Fixed32:
                    if (span.Length - pos < 4)
                    {
                        error = L.T($"偏移 {start}: fixed32 截断", $"offset {start}: truncated fixed32");
                        return false;
                    }
                    scalar = BinaryPrimitives.ReadUInt32LittleEndian(span[pos..]);
                    pos += 4;
                    break;
                case WireType.LengthDelimited:
                    if (!ProtoWriter.TryReadVarint(span, ref pos, out ulong length) || length > (ulong)(span.Length - pos))
                    {
                        error = L.T($"偏移 {start}: 长度非法", $"offset {start}: invalid length");
                        return false;
                    }
                    payload = data.Slice(pos, (int)length);
                    pos += (int)length;
                    break;
                default:
                    error = L.T($"偏移 {start}: 不支持的 wire type {(int)wireType}", $"offset {start}: unsupported wire type {(int)wireType}");
                    return false;
            }
            fields.Add(new ProtoField((int)number, wireType, data[start..pos], scalar, payload));
        }
        message = new ProtoMessage(fields);
        error = null;
        return true;
    }

    // ---------- 查询 ----------

    /// <summary>第一个指定字段号的字段。</summary>
    public ProtoField? Get(int number)
    {
        foreach (var f in _fields)
            if (f.Number == number)
                return f;
        return null;
    }

    public IEnumerable<ProtoField> GetAll(int number)
    {
        foreach (var f in _fields)
            if (f.Number == number)
                yield return f;
    }

    /// <summary>第一个指定字段号的子消息；不存在或不是消息时返回 null。</summary>
    public ProtoMessage? GetMessage(int number) =>
        Get(number) is { } f && f.TryGetMessage(out var m) ? m : null;

    /// <summary>沿字段号路径取子消息，例如 GetMessagePath(1, 1, 2)。</summary>
    public ProtoMessage? GetMessagePath(params ReadOnlySpan<int> path)
    {
        ProtoMessage? cur = this;
        foreach (int n in path)
        {
            cur = cur.GetMessage(n);
            if (cur is null)
                return null;
        }
        return cur;
    }

    /// <summary>varint 字段的值；字段缺省时 proto3 语义下视为 0，用 <paramref name="defaultValue"/> 返回。</summary>
    public ulong GetVarint(int number, ulong defaultValue = 0) =>
        Get(number) is { WireType: WireType.Varint } f ? f.Varint : defaultValue;

    public bool HasField(int number) => Get(number) is not null;

    public int IndexOf(ProtoField field) => _fields.IndexOf(field);

    // ---------- 修改 ----------

    /// <summary>
    /// 设置 varint 字段。值为 0 且 <paramref name="omitDefault"/> 时按 proto3 规则删除该字段；
    /// 字段不存在时按字段号顺序插入。
    /// </summary>
    public void SetVarint(int number, ulong value, bool omitDefault = true)
    {
        var f = Get(number);
        if (value == 0 && omitDefault)
        {
            if (f is not null)
                Remove(f);
            return;
        }
        if (f is null)
            InsertOrdered(ProtoField.CreateVarint(number, value));
        else
            f.SetVarint(value);
    }

    /// <summary>取子消息，不存在则新建并按字段号顺序插入。</summary>
    public ProtoMessage GetOrAddMessage(int number)
    {
        if (GetMessage(number) is { } existing)
            return existing;
        if (Get(number) is not null)
            throw new InvalidDataException(L.T($"字段 {number} 存在但不是子消息", $"Field {number} exists but is not a sub-message"));
        var child = new ProtoMessage();
        InsertOrdered(ProtoField.CreateMessage(number, child));
        return child;
    }

    /// <summary>插入到最后一个字段号 ≤ 新字段号的字段之后（保持按字段号升序的序列化习惯）。</summary>
    public void InsertOrdered(ProtoField field)
    {
        int index = _fields.FindLastIndex(f => f.Number <= field.Number) + 1;
        Insert(index, field);
    }

    public void Insert(int index, ProtoField field)
    {
        _fields.Insert(index, field);
        _structureDirty = true;
    }

    public void Add(ProtoField field)
    {
        _fields.Add(field);
        _structureDirty = true;
    }

    public bool Remove(ProtoField field)
    {
        bool removed = _fields.Remove(field);
        if (removed)
            _structureDirty = true;
        return removed;
    }

    // ---------- 序列化 ----------

    public byte[] ToByteArray()
    {
        var writer = new ProtoWriter();
        WriteTo(writer);
        return writer.ToArray();
    }

    internal void WriteTo(ProtoWriter writer)
    {
        foreach (var f in _fields)
            f.WriteTo(writer);
    }

    /// <summary>测试用：让所有字段在下次序列化时重新编码，检验编码器是否与原编码逐字节一致。</summary>
    internal void ForceReencode(bool recursive)
    {
        foreach (var f in _fields)
            f.ForceReencode(recursive);
    }
}
