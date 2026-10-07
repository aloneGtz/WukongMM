using WukongMM.Core.Game;
using WukongMM.Core.Mods;

namespace WukongMM.Cli;

/// <summary>
/// wmm mods [list]                 列出游戏目录下的所有 mod
/// wmm mods enable  &lt;ID 或序号&gt;     启用
/// wmm mods disable &lt;ID 或序号&gt;     禁用
/// 可选 --game &lt;游戏根目录&gt;（默认自动查找）
/// </summary>
internal static class ModsCommand
{
    public static int Run(string[] a)
    {
        string? root = null;
        var rest = new List<string>();
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == "--game" && i + 1 < a.Length)
                root = a[++i];
            else
                rest.Add(a[i]);
        }
        root ??= GameLocator.Find() ?? throw new InvalidOperationException("找不到游戏目录，请用 --game 指定");
        var mm = new ModManager(new GamePaths(root));
        mm.Refresh();

        string sub = rest.Count > 0 ? rest[0].ToLowerInvariant() : "list";
        switch (sub)
        {
            case "list":
                Print(mm);
                return 0;
            case "enable":
            case "disable":
                if (rest.Count < 2)
                    throw new ArgumentException($"用法：wmm mods {sub} <ID 或序号>");
                var mod = Resolve(mm, rest[1]);
                Console.WriteLine($"{(sub == "enable" ? "启用" : "禁用")}：{mm.DisplayNameOf(mod)}（{mod.Id}）");
                mm.SetEnabled(mod, sub == "enable");
                Print(mm);
                return 0;
            case "meta":
                if (rest.Count < 2)
                    throw new ArgumentException("用法：wmm mods meta <ID 或序号> [--name 显示名] [--url 来源链接] [--notes 备注]");
                var target = Resolve(mm, rest[1]);
                var meta = mm.Metadata.Get(target.Id);
                string? name = meta?.DisplayName, url = meta?.SourceUrl, notes = meta?.Notes;
                for (int i = 2; i + 1 < rest.Count; i += 2)
                {
                    switch (rest[i])
                    {
                        case "--name": name = rest[i + 1]; break;
                        case "--url": url = rest[i + 1]; break;
                        case "--notes": notes = rest[i + 1]; break;
                        default: throw new ArgumentException($"未知选项 {rest[i]}");
                    }
                }
                mm.UpdateMetadata(target.Id, name, notes, url);
                mm.Refresh();
                Print(mm);
                return 0;
            case "conflicts":
                PrintConflicts(mm);
                return 0;
            default:
                throw new ArgumentException($"未知子命令 {sub}");
        }
    }

    /// <summary>wmm mods conflicts：每个 pak 类 mod 的文件数，以及改了相同游戏文件的 mod 对。</summary>
    private static void PrintConflicts(ModManager mm)
    {
        var r = mm.PakConflicts;
        Console.WriteLine($"含 pak 的 mod：{r.Contents.Count} 个");
        foreach (var c in r.Contents.Values)
        {
            Console.WriteLine($"  {mm.DisplayNameOf(c.ModId)}：{c.PakCount} 个 pak，{c.Assets.Count} 个游戏资源");
            foreach (var e in c.Errors)
                Console.WriteLine($"    ⚠ {e}");
        }
        if (r.Overlaps.Count == 0)
        {
            Console.WriteLine("没有资源冲突。");
            return;
        }
        Console.WriteLine($"资源冲突：{r.Overlaps.Count} 对");
        foreach (var o in r.Overlaps)
        {
            var a = mm.Find(o.ModA);
            var b = mm.Find(o.ModB);
            bool active = a is { State: not ModState.Disabled } && b is { State: not ModState.Disabled };
            Console.WriteLine($"  {(active ? "[都已启用] " : "")}{mm.DisplayNameOf(o.ModA)} ⟂ {mm.DisplayNameOf(o.ModB)}：{o.SharedAssets.Count} 个相同资源");
            foreach (var s in o.SharedAssets.Take(10))
                Console.WriteLine($"      {PakConflictDetector.ShortName(s)}");
            if (o.SharedAssets.Count > 10)
                Console.WriteLine($"      …另有 {o.SharedAssets.Count - 10} 个");
        }
    }

    private static ModEntry Resolve(ModManager mm, string key)
    {
        if (int.TryParse(key, out int n) && n >= 1 && n <= mm.Mods.Count)
            return mm.Mods[n - 1];
        return mm.Find(key) ?? throw new ArgumentException($"找不到 mod：{key}");
    }

    private static void Print(ModManager mm)
    {
        var fw = mm.Frameworks;
        Console.WriteLine($"游戏目录：{mm.Paths.Root}");
        Console.WriteLine($"框架：CSharpLoader {(fw.CSharpLoaderInstalled ? "已安装" : "未安装")}，UE4SS {fw.UE4SSText}");
        if (mm.Mods.Count == 0)
        {
            Console.WriteLine("（没有发现 mod）");
            return;
        }
        int i = 0;
        foreach (var m in mm.Mods)
        {
            i++;
            string state = m.State switch { ModState.Enabled => "启用", ModState.Disabled => "禁用", _ => "冲突" };
            string warn = fw.MissingFrameworkFor(m.Kind) is { } w ? $"  ⚠ {w}" : "";
            Console.WriteLine($"{i,3}. [{state}] {mm.DisplayNameOf(m)}  <{m.Kind.ToDisplay()}{(m.IsFrameworkBuiltin ? "（框架内置）" : "")}>  {m.SizeBytes / 1024.0 / 1024.0:F2} MB{warn}");
            Console.WriteLine($"       {m.Id}");
            if (mm.Metadata.Get(m.Id) is { } meta)
            {
                if (!string.IsNullOrEmpty(meta.SourceUrl))
                    Console.WriteLine($"       来源：{meta.SourceUrl}");
                if (!string.IsNullOrEmpty(meta.Notes))
                    Console.WriteLine($"       备注：{meta.Notes}");
            }
            foreach (var rel in m.RelativePaths)
                Console.WriteLine($"         - {rel}");
        }
    }
}
