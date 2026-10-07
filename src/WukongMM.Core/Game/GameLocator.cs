using System.Text;
using Microsoft.Win32;

namespace WukongMM.Core.Game;

/// <summary>自动查找黑神话的安装目录（Steam 库 → 常见路径兜底）。</summary>
public static class GameLocator
{
    public const string SteamAppId = "2358720";

    public static string? Find()
    {
        foreach (var candidate in Candidates())
            if (GamePaths.IsValidGameRoot(candidate))
                return Path.GetFullPath(candidate);
        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        foreach (var lib in SteamLibraries())
        {
            string steamapps = Path.Combine(lib, "steamapps");
            string installDir = ReadInstallDir(Path.Combine(steamapps, $"appmanifest_{SteamAppId}.acf")) ?? "BlackMythWukong";
            yield return Path.Combine(steamapps, "common", installDir);
        }

        // 兜底：各盘常见位置
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady)
                continue;
            string r = drive.RootDirectory.FullName;
            yield return Path.Combine(r, "SteamLibrary", "steamapps", "common", "BlackMythWukong");
            yield return Path.Combine(r, "Steam", "steamapps", "common", "BlackMythWukong");
            yield return Path.Combine(r, "Program Files (x86)", "Steam", "steamapps", "common", "BlackMythWukong");
            yield return Path.Combine(r, "Games", "BlackMythWukong");
        }
    }

    /// <summary>所有 Steam 库根目录（包括 Steam 安装目录本身）。</summary>
    public static IReadOnlyList<string> SteamLibraries()
    {
        var result = new List<string>();
        var steam = SteamInstallPath();
        if (steam is null)
            return result;
        result.Add(steam);
        string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (var path in ReadVdfValues(File.ReadAllText(vdf, Encoding.UTF8), "path"))
            {
                var full = path.Replace("\\\\", "\\");
                if (!result.Contains(full, StringComparer.OrdinalIgnoreCase))
                    result.Add(full);
            }
        }
        return result;
    }

    private static string? SteamInstallPath()
    {
        try
        {
            using var cu = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (cu?.GetValue("SteamPath") is string p && Directory.Exists(p))
                return Path.GetFullPath(p);
            using var lm = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            if (lm?.GetValue("InstallPath") is string q && Directory.Exists(q))
                return Path.GetFullPath(q);
        }
        catch (Exception)
        {
            // 注册表不可读时走兜底路径
        }
        return null;
    }

    private static string? ReadInstallDir(string acfPath)
    {
        if (!File.Exists(acfPath))
            return null;
        return ReadVdfValues(File.ReadAllText(acfPath, Encoding.UTF8), "installdir").FirstOrDefault();
    }

    /// <summary>极简 VDF(KeyValues) 扫描：返回所有键名为 <paramref name="key"/> 的字符串值。</summary>
    internal static IEnumerable<string> ReadVdfValues(string text, string key)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        sb.Append(text[i]).Append(text[i + 1]);
                        i += 2;
                        continue;
                    }
                    sb.Append(text[i++]);
                }
                i++;
                tokens.Add(sb.ToString());
            }
            else if (c is '{' or '}')
            {
                tokens.Add(c.ToString());
                i++;
            }
            else
            {
                i++;
            }
        }
        for (int t = 0; t + 1 < tokens.Count; t++)
            if (string.Equals(tokens[t], key, StringComparison.OrdinalIgnoreCase) && tokens[t + 1] is not ("{" or "}"))
                yield return tokens[t + 1];
    }
}
