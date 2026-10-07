namespace WukongMM.Core.Game;

/// <summary>由游戏根目录推出的各个关键路径。</summary>
public sealed class GamePaths
{
    public GamePaths(string gameRoot)
    {
        Root = Path.GetFullPath(gameRoot);
        B1 = Path.Combine(Root, "b1");
        Win64 = Path.Combine(B1, "Binaries", "Win64");
        Paks = Path.Combine(B1, "Content", "Paks");
        SaveGames = Path.Combine(B1, "Saved", "SaveGames");
        ManagerData = Path.Combine(B1, "ModManager");
        DisabledStore = Path.Combine(ManagerData, "Disabled");
        SaveBackups = Path.Combine(ManagerData, "SaveBackups");
        ModMetadataFile = Path.Combine(ManagerData, "mods.json");
        GameDataItemsFile = Path.Combine(ManagerData, "GameData", "items.json");
        GameDataLevelsFile = Path.Combine(ManagerData, "GameData", "levels.json");
    }

    /// <summary>游戏内导出 mod（WukongMMDataExport）写出的道具表。</summary>
    public string GameDataItemsFile { get; }

    /// <summary>游戏内导出 mod 写出的等级表（v0.2 起）。</summary>
    public string GameDataLevelsFile { get; }

    /// <summary>游戏根目录（含 b1.exe）。</summary>
    public string Root { get; }

    /// <summary>&lt;根&gt;\b1 —— mod 相对路径的基准目录。</summary>
    public string B1 { get; }

    public string Win64 { get; }

    public string Paks { get; }

    public string SaveGames { get; }

    /// <summary>管理器自己的数据目录 &lt;b1&gt;\ModManager。</summary>
    public string ManagerData { get; }

    /// <summary>禁用 mod 的存放处，内部保留相对 b1 的原始路径。</summary>
    public string DisabledStore { get; }

    public string SaveBackups { get; }

    public string ModMetadataFile { get; }

    public string GameExe => Path.Combine(Win64, "b1-Win64-Shipping.exe");

    /// <summary>目录看起来是否是黑神话的安装目录。</summary>
    public static bool IsValidGameRoot(string? root) =>
        !string.IsNullOrWhiteSpace(root)
        && File.Exists(Path.Combine(root, "b1", "Binaries", "Win64", "b1-Win64-Shipping.exe"));
}
