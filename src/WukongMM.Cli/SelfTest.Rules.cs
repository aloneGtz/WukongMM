using WukongMM.Core.Game;
using WukongMM.Core.Items;
using WukongMM.Core.Saves;

namespace WukongMM.Cli;

/// <summary>编辑规则（等级表联动、新增道具校验）的测试，规则见 docs/SAVE_FORMAT.md §8.3。</summary>
internal static partial class SelfTest
{
    // 1→2 需 100 经验、给 1 点；2→3 需 200、给 1 点；3→4 需 300、给 2 点；
    // 4 级满级——和真实表一样最后一行 NextLevelExp 不为 0（游戏找不到 L+1 行就不再升级）
    private const string FakeLevelsJson = """
        {"schema": 1, "levels": [
          {"id": 1, "nextLevelExp": 100, "nextLevelGainTalent": 1},
          {"id": 2, "nextLevelExp": 200, "nextLevelGainTalent": 1},
          {"id": 3, "nextLevelExp": 300, "nextLevelGainTalent": 2},
          {"id": 4, "nextLevelExp": 400, "nextLevelGainTalent": 1}
        ]}
        """;

    private const string FakeItemsJsonV2 = """
        {"schema": 2, "culture": "zh-Hans", "exportedAt": "2026-10-07 12:00:00", "items": [
          {"id": 1002, "name": "灵蕴", "itemType": 1, "carryMax": 0},
          {"id": 2001, "name": "椰子酒", "itemType": 4, "carryMax": 0, "consumeType": 1},
          {"id": 2201, "name": "丹药", "itemType": 4, "carryMax": 9, "consumeType": 4},
          {"id": 3201, "name": "甘草", "itemType": 3, "carryMax": 999},
          {"id": 4001, "name": "关卡钥匙", "itemType": 5, "carryMax": 1, "isLevelItem": true},
          {"id": 4003, "name": "醒酒石", "itemType": 5, "carryMax": 9},
          {"id": 6001, "name": "碧藕种子", "itemType": 19, "carryMax": 1},
          {"id": 10101, "name": "旧头饰", "itemType": 6, "carryMax": 1}
        ]}
        """;

    static partial void RegisterRuleTestsImpl()
    {
        Test("等级表：累计经验 / 本级经验上限 / 升级灵光点", () =>
        {
            var t = LevelTable.Parse(FakeLevelsJson);
            Assert(t.MinLevel == 1 && t.MaxLevel == 4, "等级范围");
            Assert(t.CumulativeExp(1) == 0 && t.CumulativeExp(3) == 300 && t.CumulativeExp(4) == 600, "累计经验 S(L)");
            Assert(t.MaxExpAt(2) == 199 && t.MaxExpAt(4) == 399 && t.MaxExpAt(99) == 0, "本级经验上限");
            var zeroLast = LevelTable.Parse(FakeLevelsJson.Replace("\"nextLevelExp\": 400", "\"nextLevelExp\": 0"));
            Assert(zeroLast.MaxExpAt(4) == 0, "NextLevelExp 为 0 时经验只能是 0");
            Assert(t.TalentBetween(1, 4) == 4 && t.TalentBetween(2, 3) == 1 && t.TalentBetween(3, 2) == 0, "升级灵光点");
        });

        Test("改等级：按等级表同步累计经验、补发灵光点、保留原偏差", () =>
        {
            var t = LevelTable.Parse(FakeLevelsJson);
            // 原来 2 级、经验 50、累计 160（比 S(2)+50=150 多 10 的偏差）、灵光点 3
            var orig = new ProgressValues(2, 50, 160, 3);
            var p = ProgressPlanner.Plan(orig, 3, 50, 3, t, grantSparks: true);
            Assert(p.Error is null, p.Error ?? "");
            Assert(p.Values == new ProgressValues(3, 50, 360, 4), $"升到 3 级 {p.Values}");

            p = ProgressPlanner.Plan(orig, 3, 50, 3, t, grantSparks: false);
            Assert(p.Values.SparkPoints == 3 && p.GrantedSparks == 0, "不补发时灵光点不变");

            p = ProgressPlanner.Plan(orig, 2, 120, 3, t, grantSparks: true);
            Assert(p.Error is null && p.Values == new ProgressValues(2, 120, 230, 3), $"只改经验 {p.Values}");
        });

        Test("改等级：经验超出本级上限、等级超出等级表", () =>
        {
            var t = LevelTable.Parse(FakeLevelsJson);
            var orig = new ProgressValues(3, 250, 550, 0);
            // 降到 2 级，没动经验 → 自动压到 199
            var p = ProgressPlanner.Plan(orig, 2, 250, 0, t, grantSparks: true);
            Assert(p.Error is null && p.Values.CurrentExp == 199 && p.Values.TotalExp == 299, $"自动压经验 {p.Values}");
            Assert(p.Notes.Count == 2, "应有调整经验和降级两条说明");
            // 用户自己填了超出上限的经验 → 报错
            Assert(ProgressPlanner.Plan(orig, 3, 300, 0, t, true).Error is not null, "经验 = NextLevelExp 应报错");
            Assert(ProgressPlanner.Plan(orig, 5, 0, 0, t, true).Error is not null, "超过最高等级应报错");
            Assert(ProgressPlanner.Plan(orig, 0, 0, 0, t, true).Error is not null, "等级 0 应报错");
            // 满级：经验最多 NextLevelExp−1，报错文字说明是最高等级而不是"升到下一级"
            p = ProgressPlanner.Plan(orig, 4, 0, 0, t, grantSparks: true);
            Assert(p.Error is null && p.Values.TotalExp == 600 && p.Values.SparkPoints == 2, $"满级 {p.Values}");
            p = ProgressPlanner.Plan(orig, 4, 399, 0, t, grantSparks: true);
            Assert(p.Error is null && p.Values.TotalExp == 999, $"满级经验 399 {p.Values}");
            var err = ProgressPlanner.Plan(orig, 4, 400, 0, t, true).Error ?? "";
            Assert(err.Contains("最高等级") && !err.Contains("升到下一级"), $"满级报错文字：{err}");
        });

        Test("改等级：没有等级表时退回旧行为", () =>
        {
            var orig = new ProgressValues(92, 4695, 410914, 3);
            var p = ProgressPlanner.Plan(orig, 100, 5000, 3, null, grantSparks: true);
            Assert(p.Error is null && p.Values == new ProgressValues(100, 5000, 411219, 3), $"{p.Values}");
            Assert(p.Notes.Count == 1, "应提示没有等级表");
            Assert(ProgressPlanner.Plan(orig, 10000, 0, 0, null, true).Error is not null, "超过 9999 应报错");
        });

        Test("新增道具校验：类型 / 酒 / 关卡道具 / 不存在的 ID", () =>
        {
            var c = new ItemCatalog();
            Assert(ItemRules.CheckAdd(c, 3201).Level == ItemCheckLevel.Warning, "没有道具表时应警告");
            c.LoadGameDataJson(FakeItemsJsonV2);
            Assert(c.HasExtendedFields, "schema 2 应有扩展字段");
            Assert(ItemRules.CheckAdd(c, 3201).Level == ItemCheckLevel.Ok, "材料");
            Assert(ItemRules.CheckAdd(c, 2201).Level == ItemCheckLevel.Ok, "丹药");
            Assert(ItemRules.CheckAdd(c, 2001).IsBlocked, "酒");
            Assert(ItemRules.CheckAdd(c, 4001).IsBlocked, "关卡道具");
            Assert(ItemRules.CheckAdd(c, 10101).IsBlocked, "装备");
            Assert(ItemRules.CheckAdd(c, 1002).IsBlocked, "灵蕴");
            Assert(ItemRules.CheckAdd(c, 5555).IsBlocked, "表里没有的 ID");
            Assert(ItemRules.CheckAdd(c, 0).IsBlocked, "ID 0");
            Assert(ItemRules.CheckAdd(c, 6001).Level == ItemCheckLevel.Warning, "种子应警告（有条目就不再掉落）");
            Assert(ItemRules.CheckAdd(c, 4003).IsBlocked, "任务道具不在修改范围");
            Assert(ItemRules.CountChangeWarning(c, 4003) is not null && ItemRules.CountChangeWarning(c, 3201) is null, "改数量提醒");

            // 旧版表（无 consumeType）：丹药按 ID 段识别，但无法区分酒 → 警告；酒的 ID 段不在可修改范围
            c.LoadGameDataJson(FakeItemsJsonV2.Replace("\"schema\": 2", "\"schema\": 1").Replace(", \"consumeType\": 1", "").Replace(", \"consumeType\": 4", ""));
            Assert(!c.HasExtendedFields, "schema 1 不应有扩展字段");
            Assert(ItemRules.CheckAdd(c, 2201).Level == ItemCheckLevel.Warning, "旧表的丹药应警告");
            Assert(ItemRules.CheckAdd(c, 2001).IsBlocked, "旧表的酒（ID 段）不在修改范围");
        });

        Test("道具分类：按配方用途细分，只用于兑换 / 剧情的道具不可修改", () =>
        {
            const string items = """
                {"schema": 2, "culture": "zh-Hans", "items": [
                  {"id": 2201, "name": "伏虎丸", "itemType": 4, "carryMax": 9, "consumeType": 4},
                  {"id": 2301, "name": "泡酒物", "itemType": 4, "carryMax": 1, "consumeType": 5},
                  {"id": 2401, "name": "如意画轴", "itemType": 4, "carryMax": 1, "consumeType": 0},
                  {"id": 3201, "name": "甘草", "itemType": 3, "carryMax": 999},
                  {"id": 3304, "name": "佛手·左", "itemType": 3, "carryMax": 999},
                  {"id": 3306, "name": "碎金片", "itemType": 3, "carryMax": 999},
                  {"id": 3901, "name": "玉垂牙", "itemType": 3, "carryMax": 1},
                  {"id": 3926, "name": "毒敌钩", "itemType": 3, "carryMax": 1},
                  {"id": 3950, "name": "绵纱", "itemType": 3, "carryMax": 999},
                  {"id": 3951, "name": "蚕丝", "itemType": 3, "carryMax": 999},
                  {"id": 3958, "name": "精石", "itemType": 3, "carryMax": 999},
                  {"id": 3959, "name": "妖核", "itemType": 3, "carryMax": 999},
                  {"id": 3998, "name": "葫芦材料", "itemType": 3, "carryMax": 99},
                  {"id": 4022, "name": "神魂·二将", "itemType": 5, "carryMax": 1},
                  {"id": 6001, "name": "碧藕种子", "itemType": 19, "carryMax": 1}
                ]}
                """;
            const string usage = """
                {"schema": 1,
                 "forge": [
                   {"id": 10302, "position": 1, "cost": [[3950, 3], [3959, 1]], "key": [], "transform": []},
                   {"id": 20101, "position": 5, "cost": [[3958, 5], [3959, 2]], "key": [[3901, 1]], "transform": []}],
                 "alchemy": [{"id": 1127, "target": [2201, 1], "materials": [[3201, 2]], "keyItem": [0, 0], "isAttrItem": false}],
                 "hulu": [{"id": 18014, "cost": [[3998, 1]]}],
                 "shop": [{"itemId": 18014, "cost": [[3304, 1]]}, {"itemId": 11912, "cost": [[3951, 5], [3926, 1]]}],
                 "seeds": [{"id": 6001, "outputs": [3216]}],
                 "medicinal": []}
                """;
            var c = new ItemCatalog();
            c.LoadGameDataJson(items);
            c.SetUsage(ItemUsage.Parse(usage));
            var expected = new Dictionary<int, ItemClass>
            {
                [2201] = ItemClass.Pill, [2301] = ItemClass.Hidden, [2401] = ItemClass.Hidden, [3201] = ItemClass.Herb,
                [3304] = ItemClass.Hidden, [3306] = ItemClass.Hidden, [3901] = ItemClass.RareMaterial, [3926] = ItemClass.RareMaterial, [3951] = ItemClass.ArmorMaterial,
                [3950] = ItemClass.ArmorMaterial, [3958] = ItemClass.WeaponMaterial, [3959] = ItemClass.ForgeMaterial,
                [3998] = ItemClass.UpgradeMaterial, [4022] = ItemClass.Hidden, [6001] = ItemClass.Seed, [7777] = ItemClass.Hidden,
            };
            foreach (var (id, cls) in expected)
                Assert(c.ClassOf(id) == cls, $"{id} 应为 {cls}，实际 {c.ClassOf(id)}");
            Assert(ItemRules.CheckAdd(c, 3304).IsBlocked, "佛手（只用于换葫芦）不能添加");
            Assert(c.CategoryOf(2201) == "丹药" && c.CategoryOf(4022) == "任务道具", $"类别名：{c.CategoryOf(2201)} / {c.CategoryOf(4022)}");

            // 没有 usage.json 时按 ID 段的内置表
            c.SetUsage(null);
            Assert(c.ClassOf(3958) == ItemClass.WeaponMaterial && c.ClassOf(3304) == ItemClass.Hidden && c.ClassOf(3201) == ItemClass.Herb,
                "内置分类");

            // 英文界面：有英文名用英文名，没有时用中文名；类别名跟随语言
            c.SetEnglishName(3201, "Licorice");
            Core.L.English = true;
            try
            {
                Assert(c.NameOf(3201) == "Licorice" && c.NameOf(3950) == "绵纱", $"英文名：{c.NameOf(3201)} / {c.NameOf(3950)}");
                Assert(c.CategoryOf(3201) == "Herbs", $"英文类别：{c.CategoryOf(3201)}");
            }
            finally
            {
                Core.L.English = false;
            }
        });

        Test("道具数量：超过携带上限警告", () =>
        {
            var c = new ItemCatalog();
            c.LoadGameDataJson(FakeItemsJsonV2);
            Assert(ItemRules.CheckCount(c, 3201, 999).Level == ItemCheckLevel.Ok, "等于上限");
            Assert(ItemRules.CheckCount(c, 3201, 1000).Level == ItemCheckLevel.Warning, "超过上限");
            Assert(ItemRules.CheckCount(c, 2001, 50000).Level == ItemCheckLevel.Ok, "上限 0 = 不限");
            Assert(ItemRules.CheckCount(c, 2201, 20).Level == ItemCheckLevel.Ok, "可仓储丹药不受携带上限限制");
            Assert(ItemRules.CheckCount(c, 3201, -1).IsBlocked, "负数");
        });

        Test("灵光点 / 累计经验上限", () =>
        {
            var t = LevelTable.Parse(FakeLevelsJson);
            var orig = new ProgressValues(1, 0, 0, 9998);
            Assert(ProgressPlanner.Plan(orig, 1, 0, 9999, t, true).Error is null, "9999 允许");
            Assert(ProgressPlanner.Plan(orig, 1, 0, 10000, t, true).Error is not null, "10000 应拒绝");
            Assert(ProgressPlanner.Plan(orig, 3, 0, 9998, t, true).Error is not null, "补发后超过 9999 应拒绝");
            var big = new ProgressValues(10, 0, 1_999_999_990, 0);
            Assert(ProgressPlanner.Plan(big, 10, 100, 0, null, true).Error is not null, "累计经验超过 20 亿应拒绝");
            // 存档原本就超过上限、且没改它：不阻止其他修改，只给提醒
            var over = new ProgressValues(1, 0, 2_100_000_000, 12000);
            var p = ProgressPlanner.Plan(over, 1, 0, 12000, t, true);
            Assert(p.Error is null && p.Notes.Count == 2, $"原值超上限不应阻止保存：{p.Error}");
            Assert(ProgressPlanner.Plan(over, 1, 0, 12001, t, true).Error is not null, "把灵光点改成另一个超上限的值应拒绝");
        });

        Test("存档原有的不合规值：没改时只提醒，不阻止其他修改", () =>
        {
            var t = LevelTable.Parse(FakeLevelsJson);
            // 等级不在表里（表 1~4，存档 9 级，例如旧版工具改的）
            var outOfTable = new ProgressValues(9, 50, 5000, 2);
            var p = ProgressPlanner.Plan(outOfTable, 9, 50, 2, t, true);
            Assert(p.Error is null && p.Notes.Count == 1 && p.Values == outOfTable, $"只改别的：{p.Error} {p.Values}");
            Assert(ProgressPlanner.Plan(outOfTable, 10, 50, 2, t, true).Error is not null, "改成另一个表外等级应报错");
            p = ProgressPlanner.Plan(outOfTable, 9, 80, 2, t, true);
            Assert(p.Error is null && p.Values.TotalExp == 5030, $"表外等级只改经验：{p.Error} {p.Values}");
            p = ProgressPlanner.Plan(outOfTable, 3, 0, 2, t, true);
            Assert(p.Error is null && p.Values == new ProgressValues(3, 0, 300, 2), $"改回表内：{p.Error} {p.Values}");

            // 累计经验原本就超过 20 亿：往下改允许，往上改拒绝；有没有等级表都一样
            var bigTotal = new ProgressValues(3, 250, 2_100_000_000, 0);
            p = ProgressPlanner.Plan(bigTotal, 3, 0, 0, t, true);
            Assert(p.Error is null && p.Values.TotalExp == 2_099_999_750, $"降经验：{p.Error} {p.Values}");
            p = ProgressPlanner.Plan(bigTotal, 2, 0, 0, t, true);
            Assert(p.Error is null && p.Values.TotalExp == 2_099_999_550, $"降等级：{p.Error} {p.Values}");
            Assert(ProgressPlanner.Plan(bigTotal, 4, 0, 0, t, false).Error is not null, "累计经验继续变大应拒绝");
            p = ProgressPlanner.Plan(bigTotal, 3, 0, 0, null, true);
            Assert(p.Error is null && p.Values.TotalExp == 2_099_999_750, $"无等级表降经验：{p.Error} {p.Values}");

            // 满级且经验 = NextLevelExp（游戏自己会这样存）、累计经验超上限：只改灵蕴/道具时不应报错
            var maxLevel = new ProgressValues(4, 400, 2_100_000_000, 5);
            p = ProgressPlanner.Plan(maxLevel, 4, 400, 5, t, true);
            Assert(p.Error is null && p.Values.CurrentExp == 399 && p.Values.TotalExp == 2_099_999_999, $"满级：{p.Error} {p.Values}");

            // 负经验 / 负灵光点：没改只提醒；改成别的负数报错；改等级时按 0 处理
            var negExp = new ProgressValues(2, -5, 95, 0);
            p = ProgressPlanner.Plan(negExp, 2, -5, 0, t, true);
            Assert(p.Error is null && p.Notes.Count == 1 && p.Values == negExp, $"负经验未改：{p.Error} {p.Values}");
            Assert(ProgressPlanner.Plan(negExp, 2, -6, 0, t, true).Error is not null, "改成负经验应报错");
            p = ProgressPlanner.Plan(negExp, 3, -5, 0, t, true);
            Assert(p.Error is null && p.Values.CurrentExp == 0 && p.Values.SparkPoints == 1, $"负经验改等级：{p.Error} {p.Values}");
            p = ProgressPlanner.Plan(new ProgressValues(2, 0, 100, -3), 3, 0, -3, t, true);
            Assert(p.Error is null && p.Values.SparkPoints == 1, $"负灵光点补发：{p.Error} {p.Values}");

            // 灵光点原本超上限：往下改允许
            var manySparks = new ProgressValues(1, 0, 0, 12000);
            p = ProgressPlanner.Plan(manySparks, 1, 0, 11000, t, true);
            Assert(p.Error is null && p.Values.SparkPoints == 11000 && p.Notes.Count == 1, $"灵光点往下改：{p.Error}");
        });

        Test("SetExp 同时写当前经验和累计经验；SetItemStoreNum 写仓储", () =>
        {
            var save = SaveFile.Parse(BuildFakeSave());
            var v = new ArchiveView(save);
            v.SetExp(5, 2005);
            v.SetItemStoreNum(3001, 12);
            v.AddItem(2005, 3);
            var back = new ArchiveView(SaveFile.Parse(save.ToByteArray()));
            Assert(back.CurrentExp == 5 && back.TotalExp == 2005, $"{back.CurrentExp}/{back.TotalExp}");
            var items = string.Join(",", back.GetItems().Select(i => $"{i.Id}x{i.Count}+{i.StoreNum}"));
            Assert(items == "2001x1+0,2005x3+0,3001x7+12", $"道具 {items}");
        });
    }
}
