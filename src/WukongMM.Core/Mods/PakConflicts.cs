using System.Collections.Concurrent;
using WukongMM.Core.Game;

namespace WukongMM.Core.Mods;

/// <summary>一个 mod 的 pak 里装了哪些游戏文件。</summary>
/// <param name="Assets">去重后的资源路径（.uasset/.uexp/.ubulk 等合并成一个，去掉扩展名），用于比较和展示。</param>
/// <param name="PakCount">读到的 pak 个数。</param>
/// <param name="Errors">读不出来的 pak 及原因。</param>
public sealed record PakModContents(string ModId, IReadOnlyList<string> Assets, int PakCount, IReadOnlyList<string> Errors);

/// <summary>两个 mod 改了相同的游戏文件：同时启用时只有其中一个的修改生效（取决于加载顺序）。</summary>
public sealed record PakOverlap(string ModA, string ModB, IReadOnlyList<string> SharedAssets)
{
    public string Other(string modId) => string.Equals(ModA, modId, StringComparison.OrdinalIgnoreCase) ? ModB : ModA;

    public bool Involves(string modId) =>
        string.Equals(ModA, modId, StringComparison.OrdinalIgnoreCase) || string.Equals(ModB, modId, StringComparison.OrdinalIgnoreCase);
}

/// <summary>所有 pak 类 mod 之间的资源冲突（不论启用与否，界面再按状态区分）。</summary>
public sealed class PakConflictReport
{
    public static readonly PakConflictReport Empty = new(new Dictionary<string, PakModContents>(), []);

    public PakConflictReport(IReadOnlyDictionary<string, PakModContents> contents, IReadOnlyList<PakOverlap> overlaps)
    {
        Contents = contents;
        Overlaps = overlaps;
    }

    /// <summary>键为 mod ID（只含带 pak 的 mod）。</summary>
    public IReadOnlyDictionary<string, PakModContents> Contents { get; }

    public IReadOnlyList<PakOverlap> Overlaps { get; }

    public IEnumerable<PakOverlap> For(string modId) => Overlaps.Where(o => o.Involves(modId));
}

/// <summary>
/// 读每个 pak 类 mod 的索引，找出改了相同游戏文件的 mod 对。
/// pak 的文件清单按（路径, 大小, 修改时间）缓存，反复刷新不会重复读盘。
/// </summary>
public static class PakConflictDetector
{
    /// <summary>同一个资源拆成的几个文件（.uasset 与 .uexp 等）合并成一项比较。</summary>
    private static readonly HashSet<string> AssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".uasset", ".uexp", ".ubulk", ".uptnl", ".umap",
    };

    /// <summary>打包工具顺手带进来的元数据文件，几乎每个 pak 都有，不算冲突。</summary>
    private static readonly HashSet<string> IgnoredFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "AssetRegistry.bin",
    };

    private static readonly ConcurrentDictionary<(string Path, long Length, DateTime Mtime), PakFileList> Cache = new();

    public static PakConflictReport Detect(GamePaths paths, IReadOnlyList<ModEntry> mods)
    {
        var contents = new Dictionary<string, PakModContents>(StringComparer.OrdinalIgnoreCase);
        // 资源键（小写）→ 包含它的 mod 列表（保持出现顺序）
        var owners = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var display = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var mod in mods)
        {
            if (mod.Kind is not (ModKind.Pak or ModKind.LogicMod or ModKind.Custom))
                continue;
            var paks = PakFilesOf(paths, mod);
            if (paks.Count == 0)
                continue;
            var assets = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var errors = new List<string>();
            foreach (var pak in paks)
            {
                var list = ReadCached(pak);
                if (!list.Ok)
                {
                    errors.Add($"{Path.GetFileName(pak)}：{list.Error}");
                    continue;
                }
                foreach (var f in list.Files)
                {
                    if (IgnoredFileNames.Contains(Path.GetFileName(f)))
                        continue;
                    assets.Add(AssetKey(f));
                }
            }
            contents[mod.Id] = new PakModContents(mod.Id, assets.ToList(), paks.Count, errors);
            foreach (var a in assets)
            {
                string key = a.ToLowerInvariant();
                if (!owners.TryGetValue(key, out var list))
                {
                    owners[key] = list = [];
                    display[key] = a;
                }
                list.Add(mod.Id);
            }
        }

        var pairs = new Dictionary<(string, string), List<string>>();
        foreach (var (key, ids) in owners)
        {
            if (ids.Count < 2)
                continue;
            for (int i = 0; i < ids.Count; i++)
            {
                for (int j = i + 1; j < ids.Count; j++)
                {
                    var pair = (ids[i], ids[j]);
                    if (!pairs.TryGetValue(pair, out var shared))
                        pairs[pair] = shared = [];
                    shared.Add(display[key]);
                }
            }
        }
        var overlaps = pairs
            .Select(p => new PakOverlap(p.Key.Item1, p.Key.Item2, p.Value.Order(StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();
        return new PakConflictReport(contents, overlaps);
    }

    /// <summary>资源的比较键：合并同一资源的多个文件（去扩展名）。</summary>
    public static string AssetKey(string file)
    {
        string ext = Path.GetExtension(file);
        return AssetExtensions.Contains(ext) ? file[..^ext.Length] : file;
    }

    /// <summary>展示用的短路径：去掉开头的 b1/Content/。</summary>
    public static string ShortName(string asset)
    {
        foreach (var prefix in new[] { "b1/Content/", "b1/" })
            if (asset.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return asset[prefix.Length..];
        return asset;
    }

    /// <summary>mod 当前所在位置（启用 = b1，禁用 = 禁用仓库；两处都有时看游戏目录那份）下的所有 .pak。</summary>
    public static IReadOnlyList<string> PakFilesOf(GamePaths paths, ModEntry mod)
    {
        string baseDir = mod.State == ModState.Disabled ? paths.DisabledStore : paths.B1;
        var result = new List<string>();
        foreach (var rel in mod.RelativePaths)
        {
            string abs = Path.Combine(baseDir, rel);
            if (File.Exists(abs))
            {
                if (abs.EndsWith(".pak", StringComparison.OrdinalIgnoreCase))
                    result.Add(abs);
            }
            else if (Directory.Exists(abs))
            {
                try
                {
                    result.AddRange(Directory.EnumerateFiles(abs, "*.pak",
                        new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive }));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        return result;
    }

    private static PakFileList ReadCached(string pak)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(pak);
            if (!info.Exists)
                return PakFileList.Fail(L.T("文件不存在", "File not found"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return PakFileList.Fail(ex.Message) with { Transient = true };
        }
        // 禁用/启用只是移动文件，大小和修改时间不变 → 用文件名 + 大小 + 时间做键，移动后仍命中缓存
        var key = (Path.GetFileName(pak).ToLowerInvariant(), info.Length, info.LastWriteTimeUtc);
        if (Cache.TryGetValue(key, out var cached))
            return cached;
        var list = PakIndexReader.Read(pak);
        if (!list.Transient)
            Cache[key] = list;
        return list;
    }
}
