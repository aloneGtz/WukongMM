using System.Text.Json;

namespace WukongMM.Core.Items;

/// <summary>
/// 道具在游戏配方里的用途（导出 mod v0.3 的 usage.json）：打造、防具精进、炼丹、葫芦/酒/精魄升级、真言、商店兑换、种子产出。
/// 只记"哪些道具 ID 出现在哪种用途里"，分类规则在 <see cref="ItemClassifier"/>。
/// </summary>
public sealed class ItemUsage
{
    /// <summary>装备部位（BtlB1.EquipPosition）：0 头 1 身 2 臂 3 腿 4 葫芦 5 兵器 6 法宝 7 珍玩。</summary>
    public const int PositionWeapon = 5;

    public static bool IsArmorPosition(int position) => position is >= 0 and <= 3;

    /// <summary>打造兵器消耗的材料。</summary>
    public HashSet<int> ForgeWeaponCost { get; } = [];

    /// <summary>打造防具（头/身/臂/腿）消耗的材料，以及防具精进的材料。</summary>
    public HashSet<int> ForgeArmorCost { get; } = [];

    /// <summary>打造葫芦 / 法宝 / 珍玩消耗的材料。</summary>
    public HashSet<int> ForgeOtherCost { get; } = [];

    /// <summary>打造的关键道具（keyItemList，多为 Boss 掉落的特殊材料）。</summary>
    public HashSet<int> ForgeKeys { get; } = [];

    /// <summary>炼丹材料（ItemRecipeDesc.materialList）。</summary>
    public HashSet<int> AlchemyMaterials { get; } = [];

    /// <summary>炼丹的关键道具（ItemRecipeDesc.keyItemId）。</summary>
    public HashSet<int> AlchemyKeys { get; } = [];

    /// <summary>炼丹的产物。</summary>
    public HashSet<int> AlchemyTargets { get; } = [];

    /// <summary>葫芦 / 酒 / 精魄升级、真言消耗的道具。</summary>
    public HashSet<int> UpgradeCost { get; } = [];

    /// <summary>
    /// 在"商店"里换装备用的道具。游戏的防具打造就是用商店表实现的（绵纱/蚕丝/寒铁叶… → 防具），
    /// 所以这些算防具材料；换葫芦的（佛手·左/右 → 妙仙葫芦）另记在 <see cref="ShopGourdCost"/>，不算材料。
    /// </summary>
    public HashSet<int> ShopCraftCost { get; } = [];

    /// <summary>只用来兑换葫芦的道具（用户要求：换完葫芦就没用了，不列入修改范围）。</summary>
    public HashSet<int> ShopGourdCost { get; } = [];

    /// <summary>葫芦的装备 ID（HuluDesc.Id）。</summary>
    public HashSet<int> GourdIds { get; } = [];

    /// <summary>种子种出来的道具。</summary>
    public HashSet<int> SeedOutputs { get; } = [];

    /// <summary>消耗品表里标记为药材的。</summary>
    public HashSet<int> Medicinal { get; } = [];

    public string? ExportedAt { get; private set; }

    public static ItemUsage? TryLoad(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>解析 usage.json（格式错误时抛异常）。缺少的节当作空。</summary>
    public static ItemUsage Parse(string json)
    {
        var u = new ItemUsage();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("exportedAt", out var at) && at.ValueKind == JsonValueKind.String)
            u.ExportedAt = at.GetString();

        foreach (var r in Rows(root, "forge"))
        {
            int pos = Int(r, "position", -1);
            var cost = pos == PositionWeapon ? u.ForgeWeaponCost : IsArmorPosition(pos) ? u.ForgeArmorCost : u.ForgeOtherCost;
            AddIds(cost, r, "cost");
            AddIds(u.ForgeKeys, r, "key");
        }
        foreach (var r in Rows(root, "armorEnhance"))
            AddIds(u.ForgeArmorCost, r, "items");
        foreach (var r in Rows(root, "alchemy"))
        {
            AddIds(u.AlchemyMaterials, r, "materials");
            if (r.TryGetProperty("keyItem", out var key) && key.ValueKind == JsonValueKind.Array && key.GetArrayLength() > 0
                && key[0].GetInt32() is > 0 and var keyId)
                u.AlchemyKeys.Add(keyId);
            if (r.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.Array && target.GetArrayLength() > 0)
                u.AlchemyTargets.Add(target[0].GetInt32());
        }
        foreach (var section in new[] { "hulu", "wine", "soulSkill", "mantra" })
            foreach (var r in Rows(root, section))
                AddIds(u.UpgradeCost, r, "cost");
        foreach (var r in Rows(root, "hulu"))
            if (Int(r, "id", 0) is > 0 and var gourd)
                u.GourdIds.Add(gourd);
        foreach (var r in Rows(root, "shop"))
            AddIds(u.GourdIds.Contains(Int(r, "itemId", 0)) ? u.ShopGourdCost : u.ShopCraftCost, r, "cost");
        foreach (var r in Rows(root, "seeds"))
            if (r.TryGetProperty("outputs", out var outs) && outs.ValueKind == JsonValueKind.Array)
                foreach (var o in outs.EnumerateArray())
                    u.SeedOutputs.Add(o.GetInt32());
        foreach (var r in Rows(root, "medicinal"))
            if (r.ValueKind == JsonValueKind.Number)
                u.Medicinal.Add(r.GetInt32());
        return u;
    }

    private static IEnumerable<JsonElement> Rows(JsonElement root, string name) =>
        root.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array ? arr.EnumerateArray() : [];

    /// <summary>[[id, num], ...] 里的 id。</summary>
    private static void AddIds(HashSet<int> set, JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;
        foreach (var pair in arr.EnumerateArray())
            if (pair.ValueKind == JsonValueKind.Array && pair.GetArrayLength() > 0 && pair[0].GetInt32() > 0)
                set.Add(pair[0].GetInt32());
    }

    private static int Int(JsonElement e, string name, int fallback) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : fallback;
}
