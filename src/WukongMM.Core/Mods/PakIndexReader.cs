using System.Buffers.Binary;
using System.Text;

namespace WukongMM.Core.Mods;

/// <summary>读一个 pak 的文件清单的结果。</summary>
/// <param name="Files">pak 里的文件路径（挂载点 + 目录 + 文件名，去掉开头的 ../，正斜杠），例如 b1/Content/00Main/X.uasset。</param>
/// <param name="Error">读不出来的原因（索引加密、版本不支持、文件损坏等）；成功时为 null。</param>
public sealed record PakFileList(IReadOnlyList<string> Files, int Version, string? Error)
{
    public bool Ok => Error is null;

    /// <summary>临时性错误（文件被占用、打不开），下次可能就能读了，不要缓存。</summary>
    public bool Transient { get; init; }

    public static PakFileList Fail(string error, int version = 0) => new([], version, error);
}

/// <summary>
/// 只读解析 UE pak 的索引，列出里面的文件路径（不解压任何内容）。
/// 支持 UE5 的 v10/v11 索引（主索引 + 完整目录索引）。黑神话的 pak 尾部比标准格式在版本号后多 2 个字节，两种都认。
/// 索引加密的 pak（原版 pakchunk）读不了，mod 的 pak 一般不加密。格式说明见 docs/MOD_LAYOUT.md §5。
/// </summary>
public static class PakIndexReader
{
    private const uint Magic = 0x5A6F12E1;
    private const int TailBytes = 512;
    private const int MaxIndexBytes = 256 * 1024 * 1024;

    public static PakFileList Read(string pakPath)
    {
        try
        {
            using var fs = new FileStream(pakPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Read(fs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return PakFileList.Fail(L.T($"无法打开：{ex.Message}", $"Cannot open: {ex.Message}")) with { Transient = true };
        }
    }

    public static PakFileList Read(Stream s)
    {
        long len = s.Length;
        if (len < 64)
            return PakFileList.Fail(L.T("文件太小，不是 pak", "File too small to be a pak"));
        int tailLen = (int)Math.Min(TailBytes, len);
        var tail = ReadAt(s, len - tailLen, tailLen);

        // 从后往前找 magic（尾部后面还有压缩方法名表，长度随版本不同）
        int m = -1;
        for (int i = tailLen - 4; i >= 17; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) == Magic)
            {
                m = i;
                break;
            }
        }
        if (m < 0 || m + 8 > tailLen)
            return PakFileList.Fail(L.T("找不到 pak 文件尾（不是 UE pak 或文件不完整）", "Pak footer not found (not a UE pak, or the file is incomplete)"));

        long magicPos = len - tailLen + m;
        long footerStart = magicPos - 17; // 16 字节加密密钥 GUID + 1 字节"索引已加密"
        bool encrypted = tail[m - 1] != 0;
        int version = BinaryPrimitives.ReadInt32LittleEndian(tail.AsSpan(m + 4));

        // 标准：magic, version, offset(8), size(8)；黑神话：version 后多 2 字节
        long off = -1, size = -1;
        foreach (int skip in new[] { 8, 10 })
        {
            if (m + skip + 16 > tailLen)
                continue;
            long o = BinaryPrimitives.ReadInt64LittleEndian(tail.AsSpan(m + skip));
            long z = BinaryPrimitives.ReadInt64LittleEndian(tail.AsSpan(m + skip + 8));
            if (o >= 0 && z > 0 && o + z <= footerStart)
            {
                off = o;
                size = z;
                break;
            }
        }
        if (off < 0)
            return PakFileList.Fail(L.T("pak 文件尾里的索引位置无效（文件可能损坏）", "Invalid index location in the pak footer (the file may be damaged)"), version);
        if (encrypted)
            return PakFileList.Fail(L.T("索引已加密，无法读取文件清单", "The index is encrypted; the file list cannot be read"), version);
        if (version < 10)
            return PakFileList.Fail(L.T($"不支持的 pak 版本 {version}（只支持 UE5 的 v10/v11）", $"Unsupported pak version {version} (only UE5 v10/v11)"), version);
        if (size > MaxIndexBytes)
            return PakFileList.Fail(L.T("索引太大", "Index too large"), version);

        try
        {
            var index = new Reader(ReadAt(s, off, (int)size));
            string mount = index.FString();
            index.Int32(); // NumEntries
            index.Skip(8); // PathHashSeed
            if (index.Int32() != 0)
                index.Skip(8 + 8 + 20); // 路径哈希索引（offset, size, SHA1）——只有哈希没有名字，不用
            if (index.Int32() == 0)
                return PakFileList.Fail(L.T("pak 没有完整目录索引，无法列出文件名", "The pak has no full directory index; file names cannot be listed"), version);
            long dirOff = index.Int64();
            long dirSize = index.Int64();
            if (dirOff < 0 || dirSize <= 0 || dirOff + dirSize > footerStart || dirSize > MaxIndexBytes)
                return PakFileList.Fail(L.T("目录索引位置无效（文件可能损坏）", "Invalid directory index location (the file may be damaged)"), version);

            var dir = new Reader(ReadAt(s, dirOff, (int)dirSize));
            var files = new List<string>();
            int dirCount = dir.Count();
            for (int d = 0; d < dirCount; d++)
            {
                string dirName = dir.FString();
                int fileCount = dir.Count();
                for (int f = 0; f < fileCount; f++)
                {
                    string fileName = dir.FString();
                    dir.Int32(); // 条目在编码表里的位置
                    files.Add(Combine(mount, dirName, fileName));
                }
            }
            return new PakFileList(files, version, null);
        }
        catch (FormatException ex)
        {
            return PakFileList.Fail(L.T($"索引格式无法识别：{ex.Message}", $"Unrecognized index format: {ex.Message}"), version);
        }
    }

    /// <summary>挂载点 + 目录 + 文件名 → 去掉开头 ../ 和 / 的正斜杠路径。</summary>
    internal static string Combine(string mount, string dir, string file)
    {
        string full = mount.Replace('\\', '/');
        if (dir != "/")
            full = AppendSegment(full, dir.Replace('\\', '/'));
        full = AppendSegment(full, file.Replace('\\', '/'));
        while (true)
        {
            if (full.StartsWith("../", StringComparison.Ordinal))
                full = full[3..];
            else if (full.StartsWith("./", StringComparison.Ordinal))
                full = full[2..];
            else if (full.StartsWith('/'))
                full = full[1..];
            else
                break;
        }
        return full;
    }

    private static string AppendSegment(string a, string b)
    {
        if (a.Length == 0)
            return b;
        if (a.EndsWith('/'))
            return b.StartsWith('/') ? a + b[1..] : a + b;
        return b.StartsWith('/') ? a + b : a + "/" + b;
    }

    private static byte[] ReadAt(Stream s, long offset, int count)
    {
        var buf = new byte[count];
        s.Position = offset;
        s.ReadExactly(buf);
        return buf;
    }

    /// <summary>小端读取器；越界或数据不合理时抛 FormatException。</summary>
    private sealed class Reader(byte[] data)
    {
        private int _pos;

        private ReadOnlySpan<byte> Take(int n)
        {
            if (n < 0 || _pos + n > data.Length)
                throw new FormatException(L.T("数据提前结束", "unexpected end of data"));
            var span = data.AsSpan(_pos, n);
            _pos += n;
            return span;
        }

        public void Skip(int n) => Take(n);

        public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

        public long Int64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));

        /// <summary>数组/字典的元素个数：每个元素至少 4 字节，超过剩余长度的视为损坏。</summary>
        public int Count()
        {
            int n = Int32();
            if (n < 0 || (long)n * 4 > data.Length - _pos)
                throw new FormatException(L.T($"元素个数不合理：{n}", $"implausible element count: {n}"));
            return n;
        }

        /// <summary>UE FString：正数 = 含结尾 0 的 ANSI 字节数，负数 = 含结尾 0 的 UTF-16 字符数。</summary>
        public string FString()
        {
            int n = Int32();
            if (n == 0)
                return "";
            if (n > 0)
            {
                var bytes = Take(n);
                return Encoding.Latin1.GetString(bytes[..^1]);
            }
            if (n == int.MinValue)
                throw new FormatException(L.T("字符串长度不合理", "implausible string length"));
            var utf16 = Take(-n * 2);
            return Encoding.Unicode.GetString(utf16[..^2]);
        }
    }
}
