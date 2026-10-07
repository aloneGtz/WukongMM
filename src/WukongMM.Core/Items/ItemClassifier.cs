namespace WukongMM.Core.Items;

/// <summary>存档页可修改道具的细分类别。<see cref="Hidden"/> = 不在可修改范围（剧情/任务道具、兑换用道具、装备、酒等），不显示。</summary>
public enum ItemClass
{
    Hidden = 0,
    /// <summary>丹药（消耗品里的 Elixir：炼丹炉炼出来的、可仓储的那些）。</summary>
    Pill,
    /// <summary>药材：炼丹用的材料。</summary>
    Herb,
    Seed,
    /// <summary>打造兵器用的材料。</summary>
    WeaponMaterial,
    /// <summary>打造 / 精进防具用的材料。</summary>
    ArmorMaterial,
    /// <summary>兵器和防具都用到，或打造葫芦 / 法宝 / 珍玩用的材料。</summary>
    ForgeMaterial,
    /// <summary>打造或炼丹的关键道具（多为 Boss 掉落的特殊材料）。</summary>
    RareMaterial,
    /// <summary>葫芦 / 酒 / 精魄升级、真言用的材料。</summary>
    UpgradeMaterial,
}

/// <summary>
/// 判断一个道具能不能在存档页修改、属于哪个细分类别（用户 2026-10-07 要求：只列出能用于打造 / 炼丹 / 升级的材料、丹药、种子；
/// 剧情和隐藏支线道具、只用于兑换的道具（佛手·左/右）等不出现）。
/// 有导出 mod v0.3 的 usage.json 时按游戏配方表判断；没有时按道具 ID 段的内置表（buildid 21393610）。
/// </summary>
public static class ItemClassifier
{
    /// <summary>可修改类别的显示顺序。</summary>
    public static readonly ItemClass[] EditableClasses =
    [
        ItemClass.Pill, ItemClass.Herb, ItemClass.Seed, ItemClass.WeaponMaterial, ItemClass.ArmorMaterial,
        ItemClass.ForgeMaterial, ItemClass.RareMaterial, ItemClass.UpgradeMaterial,
    ];

    public static string DisplayName(ItemClass c) => c switch
    {
        ItemClass.Pill => L.T("丹药", "Pills"),
        ItemClass.Herb => L.T("药材", "Herbs"),
        ItemClass.Seed => L.T("种子", "Seeds"),
        ItemClass.WeaponMaterial => L.T("兵器材料", "Weapon materials"),
        ItemClass.ArmorMaterial => L.T("防具材料", "Armor materials"),
        ItemClass.ForgeMaterial => L.T("打造材料", "Crafting materials"),
        ItemClass.RareMaterial => L.T("珍稀材料", "Rare materials"),
        ItemClass.UpgradeMaterial => L.T("升级材料", "Upgrade materials"),
        _ => L.T("不可修改", "Not editable"),
    };

    public static ItemClass Classify(int id, GameItemInfo? g, bool hasGameData, ItemUsage? usage)
    {
        if (g is null)
            return hasGameData ? ItemClass.Hidden : ClassifyById(id);

        // 先排除：不在普通道具列表里的、关卡道具、任务道具、酒、泡酒物、配方、地图……
        if (!ItemRules.LivesInItemList(g.ItemType) || g.IsLevelItem)
            return ItemClass.Hidden;
        if (g.ItemType == 19)
            return ItemClass.Seed;
        if (g.ItemType == 4 && g.ConsumeType == ConsumeTypeElixir)
            return ItemClass.Pill;
        if (g.ItemType == 4 && g.ConsumeType == 0)
            return ClassifyById(id); // 旧版道具表没有 consumeType：按 ID 段认丹药；特殊消耗品（如意画轴等）不在任何段里
        if (g.ItemType is not (3 or 4))
            return ItemClass.Hidden; // 任务道具 5、酒升级 9、葫芦升级 10、配方 11、地图 17、人种袋 21、解锁传承天赋 22 等

        if (usage is null)
            return g.ItemType == 3 ? ClassifyById(id) : ItemClass.Hidden;

        if (usage.AlchemyMaterials.Contains(id) || usage.Medicinal.Contains(id))
            return ItemClass.Herb;
        if (usage.ForgeKeys.Contains(id) || usage.AlchemyKeys.Contains(id))
            return ItemClass.RareMaterial;
        bool shopCraft = usage.ShopCraftCost.Contains(id);
        // 防具打造（商店表）没有"关键道具"一栏：Boss 掉落的特殊材料（毒敌钩、妖生角）靠携带上限很小来区分
        if (shopCraft && g.CarryMax is > 0 and < RareCarryLimit)
            return ItemClass.RareMaterial;
        bool weapon = usage.ForgeWeaponCost.Contains(id);
        bool armor = usage.ForgeArmorCost.Contains(id) || shopCraft;
        if (weapon && !armor)
            return ItemClass.WeaponMaterial;
        if (armor && !weapon)
            return ItemClass.ArmorMaterial;
        if (weapon || usage.ForgeOtherCost.Contains(id))
            return ItemClass.ForgeMaterial;
        if (usage.UpgradeCost.Contains(id))
            return ItemClass.UpgradeMaterial;
        return ItemClass.Hidden; // 只用于兑换葫芦（佛手）、只能卖钱（金片）、或者哪里都用不到（五蕴）
    }

    /// <summary>ConsumeDesc.Type 4 = Elixir（丹药）。</summary>
    public const int ConsumeTypeElixir = 4;

    /// <summary>携带上限小于这个数的打造材料视为珍稀材料（Boss 掉落，最多几个）。普通材料上限 999。</summary>
    public const int RareCarryLimit = 100;

    /// <summary>
    /// 没有 usage.json 时的内置分类：与游戏 1.0.21（buildid 21393610）配方表的分类结果一致（2026-10-07 导出核对），
    /// 游戏更新后以新导出的 usage.json 为准。
    /// </summary>
    public static ItemClass ClassifyById(int id) => id switch
    {
        >= 6001 and <= 6099 => ItemClass.Seed,
        >= 2201 and <= 2299 => ItemClass.Pill,
        >= 3201 and <= 3219 => ItemClass.Herb,             // 炼丹药材
        >= 3958 and <= 3962 => ItemClass.WeaponMaterial,   // 精石 / 妖核 / 镔铁砂 / 金趁心 / 先天锟钢
        >= 3950 and <= 3954 => ItemClass.ArmorMaterial,    // 绵纱 / 蚕丝 / 寒铁叶 / 细金线 / 虹霓云绦
        >= 3901 and <= 3932 => ItemClass.RareMaterial,     // Boss 掉落的兵器 / 防具关键材料
        3144 or 3003 => ItemClass.RareMaterial,            // 楮白枪头、玲珑内丹（炼仙丹）
        3301 or 3309 or 3310 => ItemClass.UpgradeMaterial, // 小活师 / 铁中血 / 虚空结（精魄升级）
        _ => ItemClass.Hidden, // 佛手 3304/3305、金稳兽 3302、金片 3306~3308、五蕴 3963~3967、任务道具 4xxx 等
    };
}
