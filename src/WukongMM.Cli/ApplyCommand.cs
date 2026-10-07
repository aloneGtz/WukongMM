using System.Globalization;
using WukongMM.Core.Game;
using WukongMM.Core.Items;
using WukongMM.Core.Saves;

namespace WukongMM.Cli;

/// <summary>
/// 直接修改真实存档（与界面"保存到存档"走同一套规则和同一条 SaveWriter 流程：
/// ProgressPlanner / ItemRules 校验 → 检查游戏未运行 → 自检 → 自动备份 → 原子替换）。
/// 与界面一样：只拦"改成的"不合规值，存档原有的问题只提示；没有实际修改时不写入、不备份。
/// 区别：命令行不交互，界面里需要确认的"警告"在这里只打印；被禁止的新增道具可用 --force 强行写入（仅供实验）。
/// wmm apply &lt;存档.sav&gt; [--money N] [--level N] [--exp N] [--sparks N] [--grant-sparks] [--item ID=数量]... [--game 根目录] [--force]
/// </summary>
internal static class ApplyCommand
{
    private const string Usage =
        "用法：wmm apply <存档.sav> [--money N] [--level N] [--exp N] [--sparks N] [--grant-sparks] [--item ID=数量]... [--game 根目录] [--force]\n" +
        "  --grant-sparks  升级时按等级表补发灵光点\n" +
        "  --force         新增道具被规则禁止时仍然写入（仅供实验）";

    public static int Run(string[] a)
    {
        if (a.Length < 1)
            throw new ArgumentException(Usage);
        string path = Path.GetFullPath(a[0]);
        string? root = null;
        long? money = null, exp = null;
        int? level = null, sparks = null;
        bool grantSparks = false, force = false;
        var items = new Dictionary<int, long>();
        for (int i = 1; i < a.Length; i++)
        {
            string opt = a[i];
            string Next() => i + 1 < a.Length ? a[++i] : throw new ArgumentException($"{opt} 缺少参数值\n{Usage}");
            switch (opt)
            {
                case "--money": money = Parse(opt, Next(), 0, int.MaxValue); break;
                case "--level": level = (int)Parse(opt, Next(), 0, int.MaxValue); break;
                case "--exp": exp = Parse(opt, Next(), 0, int.MaxValue); break;
                case "--sparks": sparks = (int)Parse(opt, Next(), 0, int.MaxValue); break;
                case "--game": root = Next(); break;
                case "--grant-sparks": grantSparks = true; break;
                case "--force": force = true; break;
                case "--item":
                    string v = Next();
                    var kv = v.Split('=', 2);
                    if (kv.Length != 2)
                        throw new ArgumentException($"--item 的值应为 ID=数量：{v}\n{Usage}");
                    items[(int)Parse("--item 的 ID", kv[0], 1, int.MaxValue)] = Parse("--item 的数量", kv[1], 0, int.MaxValue);
                    break;
                default: throw new ArgumentException($"未知选项 {opt}\n{Usage}");
            }
        }
        root ??= GameLocator.Find() ?? throw new InvalidOperationException("找不到游戏目录，请用 --game 指定");
        var paths = new GamePaths(root);
        var slot = SaveLocator.FindSlots(paths).FirstOrDefault(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase))
                   ?? throw new ArgumentException($"{path} 不是该游戏目录下的存档槽位");

        var save = SaveFile.Load(slot.Path);
        var view = new ArchiveView(save);
        if (!view.IsRecognized)
            throw new InvalidDataException("无法识别存档结构");
        var table = LevelTable.TryLoad(paths.GameDataLevelsFile);
        var catalog = new ItemCatalog();
        catalog.LoadGameData(paths.GameDataItemsFile);
        long origMoney = view.Money ?? 0;
        // 游戏只使用同一 ID 的第一条（PlayerBag.FindBagItem）；ArchiveView 的修改也作用于第一条
        var bag = view.GetItems().GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
        Console.WriteLine($"修改前：等级 {view.Level} 灵蕴 {view.Money} 经验 {view.CurrentExp}/{view.TotalExp} 灵光点 {view.SparkPoints}");
        Console.WriteLine($"规则数据：等级表 {(table is null ? "无" : $"{table.MinLevel}~{table.MaxLevel} 级")}，道具表 {(catalog.HasGameData ? $"schema {catalog.GameDataSchema}" : "无")}");

        // ---- 校验（与界面相同的规则，SAVE_FORMAT §8.3）----
        // 和界面一样总是跑一遍：存档原有的不合规值会得到提示（当前经验超上限会被压回）
        var orig = new ProgressValues(view.Level ?? 0, view.CurrentExp ?? 0, view.TotalExp ?? 0, view.SparkPoints ?? 0);
        var plan = ProgressPlanner.Plan(orig, level ?? orig.Level, exp ?? orig.CurrentExp, sparks ?? orig.SparkPoints, table, grantSparks);
        if (plan.Error is { } err)
            throw new InvalidOperationException(err);
        var notes = new List<string>(plan.Notes);
        var target = plan.Values;

        // 灵蕴上限只拦"改大并超过上限"
        if (money is { } m0 && m0 > origMoney && m0 > ArchiveView.MaxSafeMoney)
            throw new InvalidOperationException($"灵蕴建议不超过 {ArchiveView.MaxSafeMoney:N0}（游戏以后获得灵蕴时不检查上限，接近 21 亿就会溢出）");
        if ((money ?? origMoney) > ArchiveView.MaxSafeMoney)
            notes.Add($"灵蕴 {money ?? origMoney:N0} 超过建议上限 {ArchiveView.MaxSafeMoney:N0}（存档原本就超过），游戏以后获得灵蕴时可能溢出");

        foreach (var (id, count) in items)
        {
            if (!bag.TryGetValue(id, out var entry))
            {
                if (count == 0)
                    throw new InvalidOperationException($"新增道具 {id} 的数量至少为 1（数量 0 的条目对种子等道具有副作用）");
                var check = ItemRules.CheckAdd(catalog, id);
                if (check.IsBlocked && !force)
                    throw new InvalidOperationException($"道具 {id}：{check.Message}（确实要写入请加 --force）");
                if (check.Level != ItemCheckLevel.Ok)
                    notes.Add($"道具 {id}：{check.Message.Replace("\n", "")}");
            }
            else
            {
                // 和界面一样：剧情 / 任务道具、只用于兑换的道具等不在修改范围（界面里根本不显示）
                if (count != entry.Count && !catalog.IsEditable(id) && !force)
                    throw new InvalidOperationException($"道具 {id}（{catalog.CategoryOf(id)}）不在可修改范围内。{ItemRules.EditableScopeText}（确实要写入请加 --force）");
                if (count != entry.Count && ItemRules.CountChangeWarning(catalog, id) is { } w)
                    notes.Add($"道具 {id}：{w}");
                if (entry.StoreNum > 0 && count < entry.Count && catalog.CarryMaxOf(id) is not null)
                    notes.Add($"道具 {id}：仓储里还有 {entry.StoreNum}，在神龛休息时会补进携带数量（命令行不能改仓储，请用界面）");
            }
            var c = ItemRules.CheckCount(catalog, id, count);
            if (c.IsBlocked)
                throw new InvalidOperationException($"道具 {id}：{c.Message}");
            if (c.Level == ItemCheckLevel.Warning)
                notes.Add($"道具 {id}：{c.Message}");
        }
        foreach (var note in notes)
            Console.WriteLine($"注意：{note}");

        // ---- 没有实际修改就不写（界面同样提示"没有需要保存的修改"）----
        bool progressChanged = target != orig;
        bool moneyChanged = money is { } mc && mc != origMoney;
        var changedItems = items.Where(kv => !bag.TryGetValue(kv.Key, out var e) || e.Count != kv.Value).ToList();
        if (!progressChanged && !moneyChanged && changedItems.Count == 0)
        {
            Console.WriteLine("没有需要写入的修改，存档未改动。");
            return 0;
        }

        // ---- 写入 ----
        if (target.Level != orig.Level) view.SetLevel(target.Level);
        if (target.CurrentExp != orig.CurrentExp || target.TotalExp != orig.TotalExp) view.SetExp(target.CurrentExp, target.TotalExp);
        if (target.SparkPoints != orig.SparkPoints) view.SetSparkPoints(target.SparkPoints);
        if (moneyChanged) view.SetMoney(money!.Value);
        foreach (var (id, count) in changedItems)
            view.SetItemCount(id, count);

        var result = SaveWriter.Write(save, slot, new SaveBackupService(paths), v =>
        {
            if (v.Level != target.Level || v.CurrentExp != target.CurrentExp || v.TotalExp != target.TotalExp || v.SparkPoints != target.SparkPoints)
                return "等级/经验/灵光点未生效";
            if (moneyChanged && v.Money != money) return "灵蕴未生效";
            var now = v.GetItems().GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Count);
            foreach (var (id, count) in changedItems)
                if (!now.TryGetValue(id, out long c2) || c2 != count)
                    return $"道具 {id} 未生效";
            return null;
        });
        var after = new ArchiveView(SaveFile.Load(slot.Path));
        Console.WriteLine($"修改后：等级 {after.Level} 灵蕴 {after.Money} 经验 {after.CurrentExp}/{after.TotalExp} 灵光点 {after.SparkPoints}");
        Console.WriteLine($"已写入 {result.BytesWritten:N0} 字节；备份：{result.Backup.Directory}");
        return 0;
    }

    /// <summary>解析整数选项值；格式或范围不对时报出选项名。</summary>
    private static long Parse(string what, string text, long min, long max) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) && v >= min && v <= max
            ? v
            : throw new ArgumentException($"{what} 的值无效：{text}（应为 {min} 到 {max} 之间的整数）\n{Usage}");
}
