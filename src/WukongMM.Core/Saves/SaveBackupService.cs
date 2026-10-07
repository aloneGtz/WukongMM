using System.Text.Json;
using WukongMM.Core.Game;

namespace WukongMM.Core.Saves;

/// <summary>一份存档备份（一个目录）。</summary>
public sealed record SaveBackup(
    string Directory,
    string SteamId,
    DateTime CreatedAt,
    string Reason,
    IReadOnlyList<string> Files,
    string SourceDirectory)
{
    public string DisplayText => $"{CreatedAt:yyyy-MM-dd HH:mm:ss}  {ReasonDisplay}  [{string.Join(", ", Files)}]";

    /// <summary>备份原因（backup.json 里存的是中文，英文界面下把已知的几种翻译过来）。</summary>
    public string ReasonDisplay
    {
        get
        {
            if (!L.English)
                return Reason;
            if (Reason.StartsWith(SaveBackupService.ReasonBeforeEdit, StringComparison.Ordinal))
                return "Auto backup before editing the save";
            if (Reason.StartsWith(SaveBackupService.ReasonBeforeRestore, StringComparison.Ordinal))
                return "Auto backup before restoring a backup";
            return Reason;
        }
    }
}

/// <summary>
/// 存档备份：&lt;b1&gt;\ModManager\SaveBackups\&lt;SteamID&gt;\&lt;yyyyMMdd-HHmmss-fff&gt;_&lt;标记&gt;\
/// 每份包含被修改的槽位文件 + ShareArchiveSaveFile.sav + backup.json。
/// </summary>
public sealed class SaveBackupService(GamePaths paths)
{
    private const string MetaFile = "backup.json";

    /// <summary>备份原因（写进 backup.json，固定用中文，界面按语言翻译）。</summary>
    public const string ReasonBeforeEdit = "修改存档前自动备份";

    public const string ReasonBeforeRestore = "还原备份前自动备份";
    private const string ShareFile = "ShareArchiveSaveFile.sav";

    private sealed record BackupMeta(DateTime CreatedAt, string Reason, string SourceDirectory, List<string> Files);

    public string RootFor(string steamId) => Path.Combine(paths.SaveBackups, steamId);

    /// <summary>写存档用的临时目录（&lt;b1&gt;\ModManager\Temp，与存档同卷）。</summary>
    public string TempDirectory => Path.Combine(paths.ManagerData, "Temp");

    /// <summary>备份一个槽位（槽位文件 + 共享存档）。</summary>
    public SaveBackup Create(SaveSlot slot, string reason) =>
        CreateFromFiles(slot.SteamId, slot.Directory, [Path.GetFileName(slot.Path), ShareFile], reason, $"slot{slot.SlotNumber}");

    private SaveBackup CreateFromFiles(string steamId, string sourceDir, IEnumerable<string> names, string reason, string tag)
    {
        var now = DateTime.Now;
        string dir = Path.Combine(RootFor(steamId), $"{now:yyyyMMdd-HHmmss-fff}_{tag}");
        Directory.CreateDirectory(dir);
        var files = new List<string>();
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string src = Path.Combine(sourceDir, name);
            if (!File.Exists(src))
                continue;
            File.Copy(src, Path.Combine(dir, name), overwrite: false);
            files.Add(name);
        }
        var meta = new BackupMeta(now, reason, sourceDir, files);
        File.WriteAllText(Path.Combine(dir, MetaFile), JsonSerializer.Serialize(meta, JsonOptions.Indented));
        return new SaveBackup(dir, steamId, now, reason, files, sourceDir);
    }

    public IReadOnlyList<SaveBackup> List(string steamId)
    {
        var result = new List<SaveBackup>();
        string root = RootFor(steamId);
        if (!Directory.Exists(root))
            return result;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            try
            {
                var meta = JsonSerializer.Deserialize<BackupMeta>(File.ReadAllText(Path.Combine(dir, MetaFile)));
                if (meta is not null)
                    result.Add(new SaveBackup(dir, steamId, meta.CreatedAt, meta.Reason, meta.Files, meta.SourceDirectory));
            }
            catch (Exception)
            {
                // 损坏或手动放入的目录，跳过
            }
        }
        result.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
        return result;
    }

    /// <summary>把备份还原回原存档目录。还原前先把将被覆盖的文件再备份一次。</summary>
    public SaveBackup Restore(SaveBackup backup)
    {
        if (GameProcess.IsRunning())
            throw new GameRunningException();
        if (!Directory.Exists(backup.SourceDirectory))
            throw new DirectoryNotFoundException(L.T($"原存档目录不存在：{backup.SourceDirectory}", $"The original save folder does not exist: {backup.SourceDirectory}"));
        var safety = CreateFromFiles(backup.SteamId, backup.SourceDirectory, backup.Files,
            $"{ReasonBeforeRestore}（还原的是 {backup.CreatedAt:yyyy-MM-dd HH:mm:ss} 的备份）", "pre-restore");
        foreach (var name in backup.Files)
            File.Copy(Path.Combine(backup.Directory, name), Path.Combine(backup.SourceDirectory, name), overwrite: true);
        return safety;
    }
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
