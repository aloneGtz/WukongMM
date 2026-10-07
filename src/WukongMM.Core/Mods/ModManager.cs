using WukongMM.Core.Game;

namespace WukongMM.Core.Mods;

/// <summary>检测到的 mod 加载框架。</summary>
/// <param name="UE4SSDisabled">UE4SS 本体在，但入口 dwmapi.dll 被管理器移进了禁用仓库（在管理器里关掉了）。</param>
public sealed record FrameworkStatus(bool CSharpLoaderInstalled, bool UE4SSInstalled, bool UE4SSDisabled = false)
{
    public static FrameworkStatus Detect(GamePaths p)
    {
        bool ue4ssCore = File.Exists(Path.Combine(p.Win64, "UE4SS.dll")) || File.Exists(Path.Combine(p.Win64, "ue4ss", "UE4SS.dll"));
        bool entryEnabled = File.Exists(Path.Combine(p.Win64, "dwmapi.dll"));
        bool entryDisabled = File.Exists(Path.Combine(p.DisabledStore, ModLayout.Win64, "dwmapi.dll"));
        return new(
            File.Exists(Path.Combine(p.Win64, "version.dll")) && Directory.Exists(Path.Combine(p.B1, ModLayout.CSharpLoaderDir)),
            entryEnabled && ue4ssCore,
            !entryEnabled && entryDisabled && ue4ssCore);
    }

    public string UE4SSText => UE4SSInstalled ? L.T("已安装", "installed") : UE4SSDisabled ? L.T("已安装（在管理器里禁用）", "installed (disabled in the manager)") : L.T("未安装", "not installed");

    /// <summary>某类 mod 依赖的框架是否缺失（缺失时 mod 即使启用也不会生效）。返回提示或 null。</summary>
    public string? MissingFrameworkFor(ModKind kind) => kind switch
    {
        ModKind.CSharpLoader when !CSharpLoaderInstalled => L.T("未检测到 CSharpLoader，这个 mod 不会生效", "CSharpLoader not found; this mod will not work"),
        ModKind.UE4SS when UE4SSDisabled => L.T("UE4SS 框架已禁用，这个 mod 不会生效", "The UE4SS framework is disabled; this mod will not work"),
        ModKind.UE4SS when !UE4SSInstalled => L.T("未检测到 UE4SS，这个 mod 不会生效", "UE4SS not found; this mod will not work"),
        ModKind.LogicMod when UE4SSDisabled => L.T("蓝图 mod 需要 UE4SS（BPModLoaderMod），UE4SS 框架已禁用", "Blueprint mods need UE4SS (BPModLoaderMod); the UE4SS framework is disabled"),
        ModKind.LogicMod when !UE4SSInstalled => L.T("蓝图 mod 需要 UE4SS（BPModLoaderMod），未检测到", "Blueprint mods need UE4SS (BPModLoaderMod), which was not found"),
        _ => null,
    };
}

/// <summary>Mod 管理的门面：扫描 + 启停 + 元数据 + 自定义分组。</summary>
public sealed class ModManager
{
    private readonly ModScanner _scanner;
    private readonly ModToggleService _toggle;

    public ModManager(GamePaths paths, Func<bool>? isGameRunning = null)
    {
        Paths = paths;
        Metadata = new ModMetadataStore(paths);
        _scanner = new ModScanner(paths);
        _toggle = new ModToggleService(paths, isGameRunning);
    }

    public GamePaths Paths { get; }

    public ModMetadataStore Metadata { get; }

    public IReadOnlyList<ModEntry> Mods { get; private set; } = [];

    public FrameworkStatus Frameworks { get; private set; } = new(false, false);

    /// <summary>pak 类 mod 之间的资源冲突（改了相同的游戏文件）。</summary>
    public PakConflictReport PakConflicts { get; private set; } = PakConflictReport.Empty;

    /// <summary>重新读取元数据并扫描。新发现的 mod 会记录首次发现时间。</summary>
    public void Refresh()
    {
        Metadata.Load();
        Frameworks = FrameworkStatus.Detect(Paths);
        Mods = _scanner.Scan(Metadata.Data.CustomMods);
        PakConflicts = PakConflictDetector.Detect(Paths, Mods);
        bool changed = false;
        foreach (var m in Mods)
        {
            if (Metadata.Get(m.Id) is null)
            {
                Metadata.GetOrCreate(m.Id);
                changed = true;
            }
        }
        if (changed && Directory.Exists(Paths.B1))
            Metadata.Save();
    }

    public ModEntry? Find(string id) => Mods.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>启用/禁用一个 mod，然后刷新列表。</summary>
    public void SetEnabled(ModEntry mod, bool enabled)
    {
        try
        {
            _toggle.SetEnabled(mod, enabled);
        }
        finally
        {
            Refresh();
        }
    }

    public string DisplayNameOf(ModEntry mod) => Metadata.DisplayNameOf(mod);

    public string DisplayNameOf(string id) => Find(id) is { } m ? DisplayNameOf(m) : id;

    /// <summary>启用 <paramref name="mod"/> 时会和哪些已启用的 mod 发生资源冲突。</summary>
    public IReadOnlyList<PakOverlap> ConflictsWithEnabled(ModEntry mod) =>
        PakConflicts.For(mod.Id).Where(o => Find(o.Other(mod.Id)) is { State: not ModState.Disabled }).ToList();

    /// <summary>当前都处于启用状态、彼此冲突的 mod 对。</summary>
    public IReadOnlyList<PakOverlap> ActiveConflicts() =>
        PakConflicts.Overlaps.Where(o => Find(o.ModA) is { State: not ModState.Disabled } && Find(o.ModB) is { State: not ModState.Disabled }).ToList();

    public void UpdateMetadata(string id, string? displayName, string? notes, string? sourceUrl)
    {
        // 先重读再改：别的进程（另一个管理器窗口、命令行、安装工具）可能已经改过 mods.json，不能用内存里的旧数据覆盖
        Metadata.Load();
        var meta = Metadata.GetOrCreate(id);
        meta.DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        meta.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        meta.SourceUrl = string.IsNullOrWhiteSpace(sourceUrl) ? null : sourceUrl.Trim();
        Metadata.Save();
    }

    /// <summary>
    /// 新建自定义 mod：把若干文件/文件夹（绝对路径或相对 b1）组成一个可整体启停的 mod。
    /// 只能选当前处于启用位置（游戏目录里）的文件。
    /// </summary>
    public CustomModDefinition AddCustomMod(string name, IEnumerable<string> paths)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(L.T("请填写 mod 名称", "Please enter a mod name"));
        Refresh(); // 先重读元数据并重新扫描（同 UpdateMetadata 的理由），校验也基于最新状态
        var rels = new List<string>();
        foreach (var p in paths)
        {
            string rel = Path.IsPathRooted(p) ? Path.GetRelativePath(Paths.B1, p) : p;
            rel = ModLayout.Normalize(rel);
            if (ModLayout.ValidateMovablePath(Paths.B1, rel) is { } why)
                throw new ArgumentException($"{p}：{why}");
            string abs = Path.Combine(Paths.B1, rel);
            if (!File.Exists(abs) && !Directory.Exists(abs))
                throw new FileNotFoundException(L.T($"找不到：{abs}", $"Not found: {abs}"));
            foreach (var other in Mods)
            {
                if (other.Kind == ModKind.Custom)
                {
                    if (other.RelativePaths.Any(r => ModLayout.IsSameOrUnder(rel, r) || ModLayout.IsSameOrUnder(r, rel)))
                        throw new ArgumentException(L.T($"{p} 已经属于自定义 mod「{DisplayNameOf(other)}」", $"{p} already belongs to the custom mod \"{DisplayNameOf(other)}\""));
                    continue;
                }
                // 不允许选中某个自动识别的文件夹 mod 内部的文件（会和它重叠）
                if (other.RelativePaths.Any(r => !ModLayout.IsSameOrUnder(r, rel) && ModLayout.IsSameOrUnder(rel, r)))
                    throw new ArgumentException(L.T($"{p} 位于已识别的 mod「{DisplayNameOf(other)}」内部，请直接管理那个 mod", $"{p} is inside the detected mod \"{DisplayNameOf(other)}\"; manage that mod instead"));
                // 选中了多文件 pak 组（.pak/.utoc/.ucas）中的一个文件 → 整组一起纳入
                if (other.RelativePaths.Any(r => string.Equals(ModLayout.Normalize(r), rel, StringComparison.OrdinalIgnoreCase)))
                    foreach (var r in other.RelativePaths)
                        AddUnique(rels, ModLayout.Normalize(r));
            }
            AddUnique(rels, rel);
        }
        if (rels.Count == 0)
            throw new ArgumentException(L.T("请至少选择一个文件或文件夹", "Please choose at least one file or folder"));

        var def = new CustomModDefinition { Id = $"custom:{Guid.NewGuid():N}", Name = name.Trim(), RelativePaths = rels };
        Metadata.Data.CustomMods.Add(def);
        Metadata.GetOrCreate(def.Id);
        Metadata.Save();
        Refresh();
        return def;
    }

    private static void AddUnique(List<string> list, string rel)
    {
        if (!list.Contains(rel, StringComparer.OrdinalIgnoreCase))
            list.Add(rel);
    }

    /// <summary>删除自定义分组（文件不删除；若当前是禁用状态，先把文件移回游戏目录）。</summary>
    public void RemoveCustomMod(string id)
    {
        Refresh(); // 先重读再改（同 UpdateMetadata）
        var entry = Find(id);
        if (entry is { Kind: ModKind.Custom, State: ModState.Disabled } && entry.Files.Count > 0)
            _toggle.SetEnabled(entry, true);
        Metadata.Data.CustomMods.RemoveAll(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        Metadata.Data.Mods.Remove(id);
        Metadata.Save();
        Refresh();
    }
}
