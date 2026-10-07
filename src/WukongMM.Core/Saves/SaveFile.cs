using System.Text;
using WukongMM.Core.Protobuf;

namespace WukongMM.Core.Saves;

/// <summary>
/// 一个 .sav 文件：外层 protobuf { 1: 存档头 ArchiveInfo(明文), 2: 主体(XOR 加密的 protobuf) }。
/// 主体被修改时才重新编码、加密，并按游戏算法重算存档头的 Md5Str；否则原样输出，保证无损。
/// </summary>
public sealed class SaveFile
{
    public const int HeaderFieldNumber = 1;
    public const int BodyFieldNumber = 2;

    /// <summary>ArchiveInfo.Md5Str 的字段号。</summary>
    public const int HeaderChecksumField = 1;

    private readonly ProtoField _bodyField;

    private SaveFile(string? path, byte[] original, ProtoMessage outer, ProtoMessage header, ProtoField bodyField, ProtoMessage body, bool checksumValid)
    {
        SourcePath = path;
        OriginalBytes = original;
        Outer = outer;
        Header = header;
        _bodyField = bodyField;
        Body = body;
        OriginalChecksumValid = checksumValid;
    }

    /// <summary>读入的文件本身校验值是否正确（false 说明文件已损坏或被别的工具改过，游戏会拒绝读档）。</summary>
    public bool OriginalChecksumValid { get; }

    public string? SourcePath { get; }

    public byte[] OriginalBytes { get; }

    public ProtoMessage Outer { get; }

    public ProtoMessage Header { get; }

    /// <summary>解密后的主体。</summary>
    public ProtoMessage Body { get; }

    public bool IsDirty => Header.IsDirty || Body.IsDirty;

    public static SaveFile Load(string path) => Parse(File.ReadAllBytes(path), path);

    public static SaveFile Parse(byte[] data, string? path = null)
    {
        if (!ProtoMessage.TryParse(data, out var outer, out var err))
            throw new InvalidDataException(L.T($"不是有效的存档文件（外层解析失败：{err}）", $"Not a valid save file (outer layer could not be parsed: {err})"));
        var headerField = outer.Get(HeaderFieldNumber);
        var bodyField = outer.Get(BodyFieldNumber);
        if (headerField is null || bodyField is null || bodyField.WireType != WireType.LengthDelimited)
            throw new InvalidDataException(L.T("不是有效的存档文件（缺少存档头或主体）", "Not a valid save file (missing header or body)"));
        if (!headerField.TryGetMessage(out var header))
            throw new InvalidDataException(L.T("存档头格式错误", "Invalid save header"));

        var encrypted = bodyField.Bytes;
        bool checksumValid = ReadChecksum(header) == SaveChecksum.Compute(encrypted.Span);

        byte[] plain = encrypted.ToArray();
        SaveCrypto.Xor(plain);
        if (!ProtoMessage.TryParse(plain, out var body, out err))
            throw new InvalidDataException(L.T($"存档主体解密后无法解析（可能游戏更新改了加密方式）：{err}", $"The decrypted save body cannot be parsed (a game update may have changed the encryption): {err}"));

        return new SaveFile(path, data, outer, header, bodyField, body, checksumValid);
    }

    /// <summary>存档头里记录的校验字符串。</summary>
    public string? StoredChecksum => ReadChecksum(Header);

    /// <summary>按当前内容重新计算的校验字符串（与 <see cref="StoredChecksum"/> 相等才能被游戏读取）。</summary>
    public string ComputeChecksum() => SaveChecksum.Compute(EncryptedBody());

    /// <summary>序列化为完整的 .sav 字节。主体有改动时会重新加密并更新存档头的 Md5Str。</summary>
    public byte[] ToByteArray()
    {
        if (Body.IsDirty)
        {
            byte[] encrypted = EncryptedBody();
            _bodyField.SetBytes(encrypted);
            string checksum = SaveChecksum.Compute(encrypted);
            if (Header.Get(HeaderChecksumField) is { WireType: WireType.LengthDelimited } f)
                f.SetBytes(Encoding.UTF8.GetBytes(checksum));
            else
                Header.InsertOrdered(ProtoField.CreateBytes(HeaderChecksumField, Encoding.UTF8.GetBytes(checksum)));
        }
        return Outer.ToByteArray();
    }

    private byte[] EncryptedBody()
    {
        if (!Body.IsDirty)
            return _bodyField.Bytes.ToArray();
        byte[] plain = Body.ToByteArray();
        SaveCrypto.Xor(plain);
        return plain;
    }

    private static string? ReadChecksum(ProtoMessage header) =>
        header.Get(HeaderChecksumField) is { WireType: WireType.LengthDelimited } f ? f.AsString() : null;

    public SaveHeaderInfo GetHeaderInfo() => SaveHeaderInfo.From(Header);

    /// <summary>测试用：强制主体所有字段重新编码。</summary>
    internal void ForceReencodeBody(bool recursive) => Body.ForceReencode(recursive);
}

/// <summary>存档头里已知的字段（docs/SAVE_FORMAT.md §3）。</summary>
public sealed record SaveHeaderInfo(
    string? Checksum,
    DateTimeOffset? SavedAt,
    DateTimeOffset? CreatedAt,
    string? SteamId,
    ulong UnknownField4)
{
    public static SaveHeaderInfo From(ProtoMessage header)
    {
        static DateTimeOffset? Ts(ProtoMessage m, int n) =>
            m.Get(n) is { WireType: WireType.Varint } f && f.Varint > 0 && f.Varint < 4_000_000_000
                ? DateTimeOffset.FromUnixTimeSeconds((long)f.Varint).ToLocalTime()
                : null;
        static string? Str(ProtoMessage m, int n) =>
            m.Get(n) is { WireType: WireType.LengthDelimited } f ? f.AsString() : null;

        return new SaveHeaderInfo(
            Str(header, 1),
            Ts(header, 5),
            Ts(header, 6),
            Str(header, 12),
            header.GetVarint(4));
    }
}
