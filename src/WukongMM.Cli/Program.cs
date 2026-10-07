using System.Text;
using WukongMM.Cli;
using WukongMM.Core.Game;
using WukongMM.Core.Saves;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

try
{
    return args[0].ToLowerInvariant() switch
    {
        "locate" => Locate(),
        "info" => Info(args.Skip(1).ToArray()),
        "roundtrip" => RoundTrip.Run(args.Skip(1).ToArray()),
        "set" => SetCommand.Run(args.Skip(1).ToArray()),
        "mods" => ModsCommand.Run(args.Skip(1).ToArray()),
        "apply" => ApplyCommand.Run(args.Skip(1).ToArray()),
        "pak" => PakList(args.Skip(1).ToArray()),
        "items" => ItemsList(args.Skip(1).ToArray()),
        "demo" => DemoCommand.Run(args.Skip(1).ToArray()),
        "selftest" => SelfTest.Run(),
        _ => Unknown(args[0]),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"错误：{ex.Message}");
    return 2;
}

static int Unknown(string cmd)
{
    Console.Error.WriteLine($"未知命令：{cmd}");
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("""
        wmm — 黑神话：悟空 Mod 管理器 命令行工具（开发/测试用）

          wmm locate                         自动查找游戏目录
          wmm info <存档.sav>                显示存档摘要（等级/灵蕴/经验/道具）
          wmm roundtrip <文件或目录>...       无损往返测试（目录会递归查找 *.sav）
          wmm set <存档.sav> --out <新文件> [--money N] [--level N] [--exp N] [--sparks N] [--item ID=数量]...
                                             生成修改后的副本（不会改动原文件）
          wmm apply <存档.sav> [--money N] [--level N] [--exp N] [--sparks N] [--grant-sparks] [--item ID=数量]... [--force]
                                             直接修改真实存档（与界面同一套规则；自动备份，游戏运行时拒绝）
          wmm mods [list|enable|disable|meta <ID或序号>] [--game <目录>]
                                             列出 / 启用 / 禁用 mod
          wmm mods conflicts [--game <目录>] 列出改了相同游戏文件的 pak 类 mod
          wmm pak <文件.pak>...              列出 pak 里的文件（只读索引）
          wmm items [--game <目录>]          列出存档页可修改的道具（按细分类别），并核对内置分类与游戏配方表是否一致
          wmm demo <目录> --save <存档.sav> [--lang zh|en]
                                             生成演示用的假游戏目录（截图用：示例 mod + 改了角色名的存档副本）
          wmm selftest                       运行内置单元测试
        """);
}

static int ItemsList(string[] a)
{
    string? root = a.Length >= 2 && a[0] == "--game" ? a[1] : GameLocator.Find();
    if (root is null)
        throw new InvalidOperationException("找不到游戏目录，请用 --game 指定");
    var paths = new GamePaths(root);
    var catalog = new WukongMM.Core.Items.ItemCatalog();
    if (!catalog.LoadGameData(paths.GameDataItemsFile))
        throw new InvalidOperationException($"没有道具表：{paths.GameDataItemsFile}");
    Console.WriteLine($"{catalog.GameDataInfo}；用途表 {(catalog.Usage is { } u ? $"导出于 {u.ExportedAt}" : "无（使用内置分类）")}；英文名 {(catalog.HasEnglishNames ? "有" : "无")}");
    var all = catalog.GameItems.ToList();
    int editable = 0;
    foreach (var cls in WukongMM.Core.Items.ItemClassifier.EditableClasses)
    {
        var list = all.Where(g => catalog.ClassOf(g.Id) == cls).ToList();
        editable += list.Count;
        Console.WriteLine($"== {WukongMM.Core.Items.ItemClassifier.DisplayName(cls)}（{list.Count}）");
        foreach (var g in list)
        {
            WukongMM.Core.L.English = true;
            string en = catalog.GameNameOf(g.Id) ?? "";
            WukongMM.Core.L.English = false;
            Console.WriteLine($"  {g.Id,5}  {catalog.GameNameOf(g.Id)}  /  {en}  （上限 {g.CarryMax}）");
        }
    }
    Console.WriteLine($"可修改 {editable} 种，不可修改 {all.Count - editable} 种");
    if (catalog.Usage is not null)
    {
        // 内置分类（没有 usage.json 时用）应与按配方表的结果一致
        var diff = all.Where(g => catalog.ClassOf(g.Id) != WukongMM.Core.Items.ItemClassifier.Classify(g.Id, g, true, null)).ToList();
        Console.WriteLine(diff.Count == 0 ? "内置分类与配方表一致。" : $"内置分类与配方表不一致 {diff.Count} 项：");
        foreach (var g in diff)
            Console.WriteLine($"  {g.Id} {g.Name}：配方表 {catalog.ClassOf(g.Id)}，内置 {WukongMM.Core.Items.ItemClassifier.Classify(g.Id, g, true, null)}");
        return diff.Count == 0 ? 0 : 1;
    }
    return 0;
}

static int PakList(string[] a)
{
    if (a.Length < 1)
        throw new ArgumentException("用法：wmm pak <文件.pak>...");
    int bad = 0;
    foreach (var path in a)
    {
        var list = WukongMM.Core.Mods.PakIndexReader.Read(path);
        Console.WriteLine($"{path}（v{list.Version}）：{(list.Ok ? $"{list.Files.Count} 个文件" : $"读取失败：{list.Error}")}");
        foreach (var f in list.Files)
            Console.WriteLine($"  {f}");
        if (!list.Ok)
            bad++;
    }
    return bad == 0 ? 0 : 1;
}

static int Locate()
{
    var root = GameLocator.Find();
    Console.WriteLine(root is null ? "未找到游戏目录" : root);
    foreach (var lib in GameLocator.SteamLibraries())
        Console.WriteLine($"  Steam 库：{lib}");
    if (root is not null)
    {
        foreach (var slot in SaveLocator.FindSlots(new GamePaths(root)))
            Console.WriteLine($"  存档：{slot.SteamId} 槽位{slot.SlotNumber}  {slot.LastWriteTime:yyyy-MM-dd HH:mm:ss}  {slot.Size:N0} 字节");
    }
    return root is null ? 1 : 0;
}

static int Info(string[] a)
{
    if (a.Length < 1)
        throw new ArgumentException("用法：wmm info <存档.sav>");
    var save = SaveFile.Load(a[0]);
    var h = save.GetHeaderInfo();
    var v = new ArchiveView(save);
    Console.WriteLine($"文件：{a[0]}");
    Console.WriteLine($"存档头：保存于 {h.SavedAt:yyyy-MM-dd HH:mm:ss}，创建于 {h.CreatedAt:yyyy-MM-dd HH:mm:ss}，SteamID {h.SteamId}，校验字段 {h.Checksum}");
    Console.WriteLine($"校验值：{(save.OriginalChecksumValid ? "正确" : $"不匹配！游戏会拒绝读取（存 {save.StoredChecksum}，算 {save.ComputeChecksum()}）")}");
    Console.WriteLine($"结构识别：{(v.IsRecognized ? "正常" : "无法识别")}");
    Console.WriteLine($"名称：{v.PlayerName}");
    Console.WriteLine($"等级：{v.Level}");
    Console.WriteLine($"灵蕴：{v.Money}");
    Console.WriteLine($"经验：当前 {v.CurrentExp} / 累计 {v.TotalExp}");
    Console.WriteLine($"灵光点：{v.SparkPoints}");
    if (GameLocator.Find() is { } root && LevelTable.TryLoad(new GamePaths(root).GameDataLevelsFile) is { } table && v.Level is { } lv)
    {
        long s = table.CumulativeExp(lv);
        long diff = (v.TotalExp ?? 0) - (v.CurrentExp ?? 0);
        string row = !table.Contains(lv) ? "不在表里！"
            : lv == table.MaxLevel ? $"是最高等级（经验最多 {table.MaxExpAt(lv)}）"
            : $"升级需 {table.Row(lv)!.NextLevelExp}";
        Console.WriteLine($"等级表：{table.MinLevel}~{table.MaxLevel} 级；{lv} 级{row}；" +
                          $"S({lv}) = {s}，累计−当前 = {diff}（{(s == diff ? "一致" : $"相差 {diff - s}")}）；" +
                          $"1~{lv} 级应得灵光点 {table.TalentBetween(table.MinLevel, lv)}");
    }
    var items = v.GetItems();
    Console.WriteLine($"道具：{items.Count} 种");
    foreach (var it in items.OrderByDescending(i => i.Count).Take(15))
        Console.WriteLine($"  ID {it.Id,-6} 数量 {it.Count,-6} 仓储 {it.StoreNum}");
    return 0;
}
