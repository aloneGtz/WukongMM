using System.Security.Cryptography;
using System.Text;

namespace WukongMM.Core.Saves;

/// <summary>
/// 存档头 Md5Str 的计算方式（来自游戏代码 b1.BGW_GameArchiveMgr::CalcByteArrayMD5 / PackArchiveFileData）：
/// 小写十六进制 MD5( 加密后的主体字节 + UTF8(ARCHIVE_MD5_MAGIC_SALT) )。
/// 读档时 ReadArchiveData 比对不一致即返回错误码 3（"存档完整性校验失败"）。见 docs/SAVE_FORMAT.md §3.1。
/// </summary>
public static class SaveChecksum
{
    /// <summary>b1.BGW_GameArchiveMgr::ARCHIVE_MD5_MAGIC_SALT</summary>
    public const string MagicSalt = "lhx2tkh6lj1wj8jmrgs3k1xb2brusehx";

    private static readonly byte[] SaltBytes = Encoding.UTF8.GetBytes(MagicSalt);

    /// <summary>对（已加密的）主体字节计算游戏使用的校验字符串。</summary>
    public static string Compute(ReadOnlySpan<byte> encryptedBody)
    {
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        md5.AppendData(encryptedBody);
        md5.AppendData(SaltBytes);
        return Convert.ToHexStringLower(md5.GetHashAndReset());
    }
}
