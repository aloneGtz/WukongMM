namespace WukongMM.Core.Items;

public enum ItemCheckLevel
{
    /// <summary>可以添加 / 修改。</summary>
    Ok,

    /// <summary>可以，但要提醒用户（需要确认）。</summary>
    Warning,

    /// <summary>不允许。</summary>
    Blocked,
}

public sealed record ItemCheck(ItemCheckLevel Level, string Message)
{
    public bool IsBlocked => Level == ItemCheckLevel.Blocked;
}

/// <summary>
/// 往背包普通道具列表（RoleBag.ItemList，.1.1.2.2）里新增 / 改数量时的游戏规则（docs/SAVE_FORMAT.md §8.3），
/// 以及可修改范围（<see cref="ItemClassifier"/>：只允许打造 / 炼丹 / 升级材料、丹药、种子）。
/// </summary>
public static class ItemRules
{
    /// <summary>酒的 ConsumeDesc.Type（酒放在葫芦/酒列表里，不在普通道具列表）。</summary>
    public const int ConsumeTypeWine = 1;

    /// <summary>
    /// 这种 ItemType 的数量是否从普通道具列表读取。游戏 PlayerBag.GetBagItemNum 对 1/6/7/8/12/14/18 读别的列表；
    /// 20/23/24 在获得时直接转换成别的效果（GainItemByType），也不放进列表。
    /// </summary>
    public static bool LivesInItemList(int itemType) => itemType switch
    {
        1 or 2 or 6 or 7 or 8 or 12 or 14 or 18 or 20 or 23 or 24 => false,
        _ => true,
    };

    /// <summary>不能放进普通道具列表的类型，给用户看的原因。</summary>
    private static string NotInItemListReason(int itemType) => itemType switch
    {
        1 => L.T("灵蕴请直接修改左侧的“灵蕴”。", "Edit Will directly in the Will field on the left."),
        2 => L.T("经验请直接修改左侧的“当前经验”。", "Edit experience directly in the Current EXP field on the left."),
        12 => L.T("灵光点请直接修改左侧的“灵光点”。", "Edit Sparks directly in the Sparks field on the left."),
        6 => L.T("装备存放在单独的装备列表里（每件有唯一编号），本工具暂不支持添加装备。",
                 "Equipment is stored in a separate list (each piece has a unique serial); adding equipment is not supported."),
        7 => L.T("这类丹药存放在单独的属性道具列表里，本工具暂不支持添加。",
                 "These pills are stored in a separate attribute-item list; adding them is not supported."),
        8 => L.T("法术存放在单独的法术列表里，本工具暂不支持添加。", "Spells are stored in a separate list; adding them is not supported."),
        14 => L.T("传承存放在单独的列表里，本工具暂不支持添加。", "Relics are stored in a separate list; adding them is not supported."),
        18 => L.T("精魄存放在单独的精魄列表里，本工具暂不支持添加。", "Spirits are stored in a separate list; adding them is not supported."),
        _ => L.T("这种道具在游戏里获得时会直接转换成其他效果，放进背包没有作用。",
                 "The game converts this item into another effect when obtained; putting it in the bag does nothing."),
    };

    /// <summary>可修改范围的说明（不在范围内的道具被拒绝时附上）。</summary>
    public static string EditableScopeText => L.T(
        "存档页只能修改打造、炼丹、升级用的材料，以及丹药和种子；剧情 / 任务道具、只用于兑换的道具等不在修改范围内。",
        "Only crafting, alchemy and upgrade materials, pills and seeds can be edited; story/quest items and exchange-only items are excluded.");

    private static string NameFor(ItemCatalog catalog, int id, GameItemInfo? g) =>
        (catalog.NameOf(id) ?? g?.Name ?? L.T($"道具 {id}", $"Item {id}")).Trim();

    /// <summary>检查能否往背包里新增这个道具 ID。</summary>
    public static ItemCheck CheckAdd(ItemCatalog catalog, int id)
    {
        if (id <= 0)
            return new(ItemCheckLevel.Blocked, L.T("道具 ID 必须是正整数。", "The item ID must be a positive integer."));
        if (!catalog.HasGameData)
        {
            if (!catalog.IsEditable(id))
                return new(ItemCheckLevel.Blocked, L.T($"ID {id} 不在可修改范围内。", $"ID {id} is not editable.") + EditableScopeText);
            return new(ItemCheckLevel.Warning, L.T(
                "还没有游戏道具表，无法确认这个 ID 是否存在。\n" +
                "如果游戏里没有这个道具，读档时游戏会自动删除它并提示“存档已修复”。\n" +
                "建议先安装 WukongMMDataExport 进一次游戏，导出道具表后再添加。",
                "There is no game item table yet, so this ID cannot be verified.\n" +
                "If the game has no such item, it will delete it when loading and report that the save was repaired.\n" +
                "Install WukongMMDataExport and load a save in the game once to export the item table first."));
        }
        if (catalog.GameInfo(id) is not { } g)
            return new(ItemCheckLevel.Blocked, L.T($"游戏道具表里没有 ID {id}。加进去读档时会被游戏自动删除。",
                                                   $"ID {id} is not in the game item table. The game would delete it when loading."));

        string name = NameFor(catalog, id, g);
        if (!LivesInItemList(g.ItemType))
            return new(ItemCheckLevel.Blocked, L.T($"{name}（{ItemCatalog.ItemTypeDisplay(g.ItemType)}）不能加到背包道具里：",
                                                   $"{name} ({ItemCatalog.ItemTypeDisplay(g.ItemType)}) cannot be added to the bag: ")
                                               + NotInItemListReason(g.ItemType));
        if (g.IsLevelItem)
            return new(ItemCheckLevel.Blocked, L.T($"{name} 是关卡道具，每次进入游戏都会被游戏清除，加了也没用。",
                                                   $"{name} is a level item; the game clears it on every load, so adding it is pointless."));
        if (g.ItemType == 4 && g.ConsumeType == ConsumeTypeWine)
            return new(ItemCheckLevel.Blocked, L.T($"{name} 是酒。酒存放在葫芦/酒的列表里，不在背包道具里，本工具暂不支持添加。",
                                                   $"{name} is a drink. Drinks live in the gourd/drink list, not in the bag; adding them is not supported."));
        if (!catalog.IsEditable(id))
            return new(ItemCheckLevel.Blocked, L.T($"{name}（{catalog.CategoryOf(id)}）不在可修改范围内。",
                                                   $"{name} ({catalog.CategoryOf(id)}) is not editable. ") + EditableScopeText);
        if (g.ItemType == 4 && !catalog.HasExtendedFields)
            return new(ItemCheckLevel.Warning, L.T(
                $"{name} 是消耗品。现有道具表是旧版，无法判断它是不是酒（酒不能加到背包道具里，加了会被游戏忽略）。\n" +
                "进一次游戏让新版导出 mod 更新道具表后，就能自动判断。",
                $"{name} is a consumable. The item table is an old version and cannot tell whether it is a drink " +
                "(drinks cannot go in the bag and would be ignored).\nLoad a save once with the new export mod to update the table."));
        if (g.ItemType == 19)
            return new(ItemCheckLevel.Warning, L.T(
                $"{name} 是种子。背包里一旦有这一项（哪怕数量为 0），以后这种种子就不会再从敌人或宝箱掉落。",
                $"{name} is a seed. Once the bag has an entry for it (even with count 0), this seed will no longer drop from enemies or chests."));
        return new(ItemCheckLevel.Ok, $"{name}（{catalog.CategoryOf(id)}" +
                                      (g.CarryMax > 0 ? L.T($"，携带上限 {g.CarryMax}", $", carry limit {g.CarryMax}") : "") + "）");
    }

    /// <summary>任务道具：任务阶段条件按背包数量判断（FTaskStageDetectCondition_CheckItemNum），开新周目时全部清除。</summary>
    public static string TaskItemWarning => L.T(
        "剧情任务会按背包里的数量判断进度，凭空添加或改数量可能让任务提前满足或流程错乱；开新周目时任务道具会被游戏清除。",
        "Quests check the item count in the bag; adding or changing it may complete a quest early or break its flow. Quest items are cleared in New Game+.");

    /// <summary>改数量需要额外提醒的道具（目前只有任务道具），不需要时返回 null。</summary>
    public static string? CountChangeWarning(ItemCatalog catalog, int id) =>
        catalog.GameInfo(id) is { ItemType: 5 } ? TaskItemWarning : null;

    /// <summary>
    /// 检查道具数量是否超过游戏的携带上限（超过不会报错，但之后捡不到、买不到这个道具）。
    /// 可仓储的丹药不受携带上限限制（获得时多出的部分进仓储）。
    /// </summary>
    public static ItemCheck CheckCount(ItemCatalog catalog, int id, long count)
    {
        if (count < 0 || count > int.MaxValue)
            return new(ItemCheckLevel.Blocked, L.T("数量必须是 0 到 2147483647 之间的整数。", "The count must be an integer from 0 to 2147483647."));
        if (catalog.CarryMaxOf(id) is { } max && count > max && !catalog.IsStorable(id))
            return new(ItemCheckLevel.Warning, L.T(
                $"超过携带上限 {max}：游戏不会报错，但之后在游戏里捡不到、买不到这个道具，直到数量降到上限以下。",
                $"Exceeds the carry limit {max}: the game will not complain, but you cannot pick up or buy this item until the count drops below the limit."));
        return new(ItemCheckLevel.Ok, "");
    }
}
