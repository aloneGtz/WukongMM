using WukongMM.Core.Game;

namespace WukongMM.Core.Mods;

/// <summary>
/// 扫描已安装（启用位置 = &lt;b1&gt;）和已禁用（禁用仓库，保留相同相对路径）的 mod，合并出状态。
/// </summary>
public sealed class ModScanner(GamePaths paths)
{
    private const int MaxListedFiles = 300;

    /// <summary>buildid 21393610 的原版 pak（Paks 根目录）。即使缺了 .sig 也不当作 mod。</summary>
    private static readonly HashSet<string> VanillaPakStems = new(StringComparer.OrdinalIgnoreCase)
    {
        "pakchunk0-Windows", "pakchunk1-Windows", "pakchunk2-Windows", "pakchunk3-Windows", "pakchunk4-Windows",
        "pakchunk5-Windows", "pakchunk6-Windows", "pakchunk7-Windows", "pakchunk8-Windows", "pakchunk9-Windows",
        "pakchunk10-Windows", "pakchunk11-Windows", "pakchunk12-Windows", "pakchunk13-Windows", "pakchunk14-Windows",
        "pakchunk15-Windows", "pakchunk16-Windows", "pakchunk17-Windows", "pakchunk20-Windows",
        "pakchunk23-Windows-WinStoreSteam", "pakchunk26-Windows_P",
    };

    private sealed record ScanItem(string Id, ModKind Kind, string Name, List<string> RelativePaths, bool Builtin);

    public IReadOnlyList<ModEntry> Scan(IReadOnlyList<CustomModDefinition> customMods)
    {
        var live = ScanBase(paths.B1);
        var disabled = ScanBase(paths.DisabledStore);

        bool CoveredByCustom(ScanItem item) =>
            item.RelativePaths.All(rel => customMods.Any(c => c.RelativePaths.Any(p => ModLayout.IsSameOrUnder(rel, p))));

        var result = new List<ModEntry>();
        var ids = live.Keys.Union(disabled.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var id in ids)
        {
            live.TryGetValue(id, out var l);
            disabled.TryGetValue(id, out var d);
            var item = (l ?? d)!;
            if (CoveredByCustom(item))
                continue;
            var state = l is not null && d is not null ? ModState.Conflict : l is not null ? ModState.Enabled : ModState.Disabled;
            var rels = (l?.RelativePaths ?? []).Union(d?.RelativePaths ?? [], StringComparer.OrdinalIgnoreCase).ToList();
            string where = state == ModState.Disabled ? paths.DisabledStore : paths.B1;
            result.Add(Build(item.Id, item.Kind, item.Name, rels, state, where, item.Builtin));
        }

        foreach (var c in customMods)
        {
            var rels = c.RelativePaths.Select(ModLayout.Normalize).ToList();
            bool anyLive = rels.Any(r => Exists(Path.Combine(paths.B1, r)));
            bool anyDisabled = rels.Any(r => Exists(Path.Combine(paths.DisabledStore, r)));
            var state = anyLive && anyDisabled ? ModState.Conflict : anyLive ? ModState.Enabled : ModState.Disabled;
            string where = state == ModState.Disabled ? paths.DisabledStore : paths.B1;
            result.Add(Build(c.Id, ModKind.Custom, c.Name, rels, state, where, builtin: false));
        }

        result.Sort((a, b) =>
        {
            int k = a.Kind.CompareTo(b.Kind);
            return k != 0 ? k : string.Compare(a.DefaultName, b.DefaultName, StringComparison.CurrentCultureIgnoreCase);
        });
        return result;
    }

    private Dictionary<string, ScanItem> ScanBase(string baseDir)
    {
        var found = new Dictionary<string, ScanItem>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(baseDir))
            return found;

        // Pak：Paks 根目录（无 .sig 的非原版 pak）+ 子目录
        string paksAbs = Path.Combine(baseDir, ModLayout.Paks);
        if (Directory.Exists(paksAbs))
        {
            AddStemGroups(found, paksAbs, ModLayout.Paks, ModKind.Pak, rootOfPaks: true);
            foreach (var sub in SafeDirs(paksAbs))
            {
                string name = Path.GetFileName(sub);
                string rel = Path.Combine(ModLayout.Paks, name);
                if (name.Equals("LogicMods", StringComparison.OrdinalIgnoreCase))
                    ScanPakContainer(found, sub, rel, ModKind.LogicMod);
                else if (name.StartsWith('~') || name.Equals("Mods", StringComparison.OrdinalIgnoreCase))
                    ScanPakContainer(found, sub, rel, ModKind.Pak);
                else if (ContainsPak(sub))
                    AddFolder(found, ModKind.Pak, rel, name);
            }
        }

        // CSharpLoader
        string cs = Path.Combine(baseDir, ModLayout.CSharpLoaderModsDir);
        foreach (var sub in SafeDirs(cs))
        {
            string name = Path.GetFileName(sub);
            if (!name.Equals(ModLayout.CSharpCommonDir, StringComparison.OrdinalIgnoreCase))
                AddFolder(found, ModKind.CSharpLoader, Path.Combine(ModLayout.CSharpLoaderModsDir, name), name);
        }

        // UE4SS（新旧两种布局）
        foreach (var modsRel in new[] { ModLayout.UE4SSModsDir, ModLayout.UE4SSLegacyModsDir })
        {
            foreach (var sub in SafeDirs(Path.Combine(baseDir, modsRel)))
            {
                string name = Path.GetFileName(sub);
                if (ModLayout.UE4SSNonModDirs.Contains(name, StringComparer.OrdinalIgnoreCase))
                    continue;
                AddFolder(found, ModKind.UE4SS, Path.Combine(modsRel, name), name,
                    builtin: ModLayout.UE4SSBuiltinMods.Contains(name));
            }
        }
        return found;
    }

    /// <summary>~mods / LogicMods 这类容器：直接放的文件按主干名分组，每个子文件夹算一个 mod。</summary>
    private static void ScanPakContainer(Dictionary<string, ScanItem> found, string dirAbs, string dirRel, ModKind kind)
    {
        AddStemGroups(found, dirAbs, dirRel, kind, rootOfPaks: false);
        foreach (var sub in SafeDirs(dirAbs))
        {
            if (ContainsPak(sub))
            {
                string name = Path.GetFileName(sub);
                AddFolder(found, kind, Path.Combine(dirRel, name), name);
            }
        }
    }

    private static void AddStemGroups(Dictionary<string, ScanItem> found, string dirAbs, string dirRel, ModKind kind, bool rootOfPaks)
    {
        var groups = SafeFiles(dirAbs)
            .Where(f => ModLayout.PakExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .GroupBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
        {
            var exts = g.Select(f => Path.GetExtension(f).ToLowerInvariant()).ToHashSet();
            if (!exts.Contains(".pak") && !exts.Contains(".utoc"))
                continue;
            if (rootOfPaks && (exts.Contains(".sig") || VanillaPakStems.Contains(g.Key)))
                continue; // 原版
            var rels = g.Select(f => Path.Combine(dirRel, Path.GetFileName(f))).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            string id = ModLayout.ToId(kind, Path.Combine(dirRel, g.Key));
            found[id] = new ScanItem(id, kind, g.Key, rels, false);
        }
    }

    private static void AddFolder(Dictionary<string, ScanItem> found, ModKind kind, string rel, string name, bool builtin = false)
    {
        string id = ModLayout.ToId(kind, rel);
        found[id] = new ScanItem(id, kind, name, [rel], builtin);
    }

    private static ModEntry Build(string id, ModKind kind, string name, List<string> rels, ModState state, string baseDir, bool builtin)
    {
        long size = 0;
        DateTime latest = DateTime.MinValue;
        var files = new List<string>();
        foreach (var rel in rels)
        {
            string abs = Path.Combine(baseDir, rel);
            IEnumerable<string> list = File.Exists(abs) ? [abs]
                : Directory.Exists(abs) ? SafeFilesRecursive(abs) : [];
            foreach (var f in list)
            {
                try
                {
                    var info = new FileInfo(f);
                    size += info.Length;
                    if (info.LastWriteTime > latest)
                        latest = info.LastWriteTime;
                    if (files.Count < MaxListedFiles)
                        files.Add(Path.GetRelativePath(baseDir, f));
                }
                catch (IOException)
                {
                }
            }
        }
        return new ModEntry
        {
            Id = id,
            Kind = kind,
            DefaultName = name,
            RelativePaths = rels,
            State = state,
            SizeBytes = size,
            LastModified = latest == DateTime.MinValue ? default : latest,
            Files = files,
            IsFrameworkBuiltin = builtin,
        };
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static bool ContainsPak(string dir) =>
        SafeFilesRecursive(dir).Any(f => Path.GetExtension(f).Equals(".pak", StringComparison.OrdinalIgnoreCase)
                                       || Path.GetExtension(f).Equals(".utoc", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try
        {
            return Directory.Exists(dir) ? Directory.GetDirectories(dir) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeFiles(string dir)
    {
        try
        {
            return Directory.Exists(dir) ? Directory.GetFiles(dir) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeFilesRecursive(string dir)
    {
        try
        {
            return Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
