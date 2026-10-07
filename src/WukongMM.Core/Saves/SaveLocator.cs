using System.Text.RegularExpressions;
using WukongMM.Core.Game;

namespace WukongMM.Core.Saves;

/// <summary>一个存档槽位文件。</summary>
public sealed record SaveSlot(string Path, string SteamId, int SlotNumber, DateTime LastWriteTime, long Size)
{
    public string Directory => System.IO.Path.GetDirectoryName(Path)!;

    /// <summary>同目录下的共享存档（备份时一起备份）。</summary>
    public string ShareFilePath => System.IO.Path.Combine(Directory, "ShareArchiveSaveFile.sav");
}

public static partial class SaveLocator
{
    [GeneratedRegex(@"^ArchiveSaveFile\.(\d+)\.sav$", RegexOptions.IgnoreCase)]
    private static partial Regex SlotFileRegex();

    /// <summary>列出游戏目录下所有 SteamID 的所有存档槽位，按最后写入时间倒序。</summary>
    public static IReadOnlyList<SaveSlot> FindSlots(GamePaths paths)
    {
        var result = new List<SaveSlot>();
        if (!System.IO.Directory.Exists(paths.SaveGames))
            return result;
        foreach (var dir in System.IO.Directory.EnumerateDirectories(paths.SaveGames))
        {
            string steamId = System.IO.Path.GetFileName(dir);
            foreach (var file in System.IO.Directory.EnumerateFiles(dir, "ArchiveSaveFile.*.sav"))
            {
                var m = SlotFileRegex().Match(System.IO.Path.GetFileName(file));
                if (!m.Success)
                    continue;
                var info = new FileInfo(file);
                result.Add(new SaveSlot(file, steamId, int.Parse(m.Groups[1].Value), info.LastWriteTime, info.Length));
            }
        }
        result.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
        return result;
    }

    /// <summary>该 SteamID 是否开启了 Steam 云存档（目录里有 steam_autocloud.vdf）。</summary>
    public static bool IsSteamCloudEnabled(SaveSlot slot) =>
        File.Exists(System.IO.Path.Combine(slot.Directory, "steam_autocloud.vdf"));
}
