using WukongMM.Core.Protobuf;

namespace WukongMM.Core.Saves;

/// <summary>存档主体里已确认字段的位置（docs/SAVE_FORMAT.md §5）。</summary>
public static class ArchiveLayout
{
    /// <summary>主体 .1.1 = 角色信息。</summary>
    public static ReadOnlySpan<int> RoleInfoPath => [1, 1];

    public const int Basic = 1;            // .1.1.1
    public const int BasicName = 2;
    public const int BasicLevel = 4;

    public const int Bag = 2;              // .1.1.2
    public const int BagCurrency = 1;      // .1.1.2.1 {1: 类型, 2: 数量}
    public const int CurrencyType = 1;
    public const int CurrencyAmount = 2;
    public const int CurrencyTypeWill = 1; // 灵蕴
    public const int BagItem = 2;          // .1.1.2.2 RoleItem {1: ItemId, 2: Num, 3: StoreNum}
    public const int ItemId = 1;
    public const int ItemCount = 2;
    public const int ItemStore = 3;        // 仓储数量：在神龛休息时补进 Num（任何 CarryMax>0 的道具）

    public const int GrowthGroup = 3;      // .1.1.3
    public const int Growth = 2;           // .1.1.3.2
    public const int GrowthCurrentExp = 1;
    public const int GrowthSparkPoints = 2;
    public const int GrowthTotalExp = 7;
}

/// <summary>背包里的一个普通道具：携带数量 + 仓储数量（休息时补进携带数量）。</summary>
public sealed record InventoryItem(int Id, long Count, long StoreNum);

/// <summary>
/// 在 <see cref="SaveFile"/> 之上提供等级、灵蕴、经验、道具的类型化读写。
/// 路径不存在时读取返回 null（界面据此禁用对应编辑项）。
/// </summary>
public sealed class ArchiveView
{
    /// <summary>protobuf int32 字段可安全写入的上限。</summary>
    public const long MaxInt32Value = int.MaxValue;

    /// <summary>
    /// 灵蕴建议上限：游戏获得灵蕴不做上限检查，商店/UI 按 int32 读取，接近 21 亿时再捡就会溢出成负数（SAVE_FORMAT §8.3）。
    /// 只限制"改成的新值"；存档里原本就超过的值不拦。
    /// </summary>
    public const long MaxSafeMoney = 999_999_999;

    private readonly ProtoMessage? _role;

    public ArchiveView(SaveFile save)
    {
        Save = save;
        _role = save.Body.GetMessagePath(ArchiveLayout.RoleInfoPath);
    }

    public SaveFile Save { get; }

    /// <summary>存档结构是否符合预期（找得到角色信息段）。</summary>
    public bool IsRecognized => _role?.GetMessage(ArchiveLayout.Basic) is not null;

    private ProtoMessage? BasicMsg => _role?.GetMessage(ArchiveLayout.Basic);
    private ProtoMessage? BagMsg => _role?.GetMessage(ArchiveLayout.Bag);
    private ProtoMessage? GrowthMsg => _role?.GetMessagePath(ArchiveLayout.GrowthGroup, ArchiveLayout.Growth);

    // ---------- 基本信息 ----------

    public string? PlayerName =>
        BasicMsg?.Get(ArchiveLayout.BasicName) is { WireType: WireType.LengthDelimited } f ? f.AsString() : null;

    /// <summary>改角色名（只用于制作演示 / 测试用的存档副本，界面不提供）。</summary>
    public void SetPlayerName(string name) =>
        (Require(BasicMsg, L.T("角色基本信息", "character info")).Get(ArchiveLayout.BasicName)
         ?? throw new InvalidDataException(L.T("存档里没有角色名", "No character name in the save")))
        .SetBytes(System.Text.Encoding.UTF8.GetBytes(name));

    public int? Level => BasicMsg is { } b ? (int)b.GetVarint(ArchiveLayout.BasicLevel) : null;

    public void SetLevel(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 9999);
        Require(BasicMsg, L.T("角色基本信息", "character info")).SetVarint(ArchiveLayout.BasicLevel, (ulong)level);
    }

    // ---------- 灵蕴 ----------

    private ProtoMessage? WillEntry
    {
        get
        {
            if (BagMsg is not { } bag)
                return null;
            foreach (var f in bag.GetAll(ArchiveLayout.BagCurrency))
                if (f.TryGetMessage(out var m) && m.GetVarint(ArchiveLayout.CurrencyType) == ArchiveLayout.CurrencyTypeWill)
                    return m;
            return null;
        }
    }

    /// <summary>灵蕴数量。背包段存在但没有灵蕴条目时视为 0。</summary>
    public long? Money => BagMsg is null ? null : (long)(WillEntry?.GetVarint(ArchiveLayout.CurrencyAmount) ?? 0);

    public void SetMoney(long amount)
    {
        CheckRange(amount);
        var bag = Require(BagMsg, L.T("背包", "bag"));
        var entry = WillEntry;
        if (entry is null)
        {
            entry = new ProtoMessage();
            entry.SetVarint(ArchiveLayout.CurrencyType, ArchiveLayout.CurrencyTypeWill);
            bag.InsertOrdered(ProtoField.CreateMessage(ArchiveLayout.BagCurrency, entry));
        }
        entry.SetVarint(ArchiveLayout.CurrencyAmount, (ulong)amount);
    }

    // ---------- 经验 / 灵光点 ----------

    /// <summary>当前等级内的经验。</summary>
    public long? CurrentExp => GrowthMsg is { } g ? (long)g.GetVarint(ArchiveLayout.GrowthCurrentExp) : null;

    /// <summary>累计总经验。</summary>
    public long? TotalExp => GrowthMsg is { } g ? (long)g.GetVarint(ArchiveLayout.GrowthTotalExp) : null;

    /// <summary>未分配的灵光点。</summary>
    public int? SparkPoints => GrowthMsg is { } g ? (int)g.GetVarint(ArchiveLayout.GrowthSparkPoints) : null;

    /// <summary>设置当前等级内经验，并把总经验同步加上同样的差值（保持"总经验 − 当前经验"不变）。</summary>
    public void SetCurrentExp(long exp)
    {
        CheckRange(exp);
        var g = Require(GrowthMsg, L.T("成长数据", "growth data"));
        long oldCur = (long)g.GetVarint(ArchiveLayout.GrowthCurrentExp);
        long oldTotal = (long)g.GetVarint(ArchiveLayout.GrowthTotalExp);
        long newTotal = oldTotal + (exp - oldCur);
        CheckRange(newTotal);
        g.SetVarint(ArchiveLayout.GrowthCurrentExp, (ulong)exp);
        g.SetVarint(ArchiveLayout.GrowthTotalExp, (ulong)newTotal);
    }

    /// <summary>同时设置当前经验和累计经验（改等级时由 <see cref="ProgressPlanner"/> 算好两者）。</summary>
    public void SetExp(long current, long total)
    {
        CheckRange(current);
        CheckRange(total);
        var g = Require(GrowthMsg, L.T("成长数据", "growth data"));
        g.SetVarint(ArchiveLayout.GrowthCurrentExp, (ulong)current);
        g.SetVarint(ArchiveLayout.GrowthTotalExp, (ulong)total);
    }

    public void SetSparkPoints(int points)
    {
        CheckRange(points);
        Require(GrowthMsg, L.T("成长数据", "growth data")).SetVarint(ArchiveLayout.GrowthSparkPoints, (ulong)points);
    }

    // ---------- 道具 ----------

    public IReadOnlyList<InventoryItem> GetItems()
    {
        var list = new List<InventoryItem>();
        if (BagMsg is not { } bag)
            return list;
        foreach (var f in bag.GetAll(ArchiveLayout.BagItem))
        {
            if (!f.TryGetMessage(out var m))
                continue;
            list.Add(new InventoryItem(
                (int)m.GetVarint(ArchiveLayout.ItemId),
                (long)m.GetVarint(ArchiveLayout.ItemCount),
                (long)m.GetVarint(ArchiveLayout.ItemStore)));
        }
        return list;
    }

    private ProtoMessage? FindItemEntry(int itemId)
    {
        if (BagMsg is not { } bag)
            return null;
        foreach (var f in bag.GetAll(ArchiveLayout.BagItem))
            if (f.TryGetMessage(out var m) && (int)m.GetVarint(ArchiveLayout.ItemId) == itemId)
                return m;
        return null;
    }

    public bool HasItem(int itemId) => FindItemEntry(itemId) is not null;

    /// <summary>修改已有道具的数量；道具不存在时新增一条。</summary>
    public void SetItemCount(int itemId, long count)
    {
        CheckRange(count);
        var entry = FindItemEntry(itemId);
        if (entry is null)
        {
            AddItem(itemId, count);
            return;
        }
        entry.SetVarint(ArchiveLayout.ItemCount, (ulong)count);
    }

    /// <summary>修改已有道具的仓储数量（道具不存在时抛异常；新增道具的仓储数量必须为 0）。</summary>
    public void SetItemStoreNum(int itemId, long storeNum)
    {
        CheckRange(storeNum);
        var entry = FindItemEntry(itemId) ?? throw new InvalidOperationException(L.T($"背包里没有道具 {itemId}", $"Item {itemId} is not in the bag"));
        entry.SetVarint(ArchiveLayout.ItemStore, (ulong)storeNum);
    }

    /// <summary>新增道具条目（仓储数量为 0），按道具 ID 升序插入到背包道具列表中。</summary>
    public void AddItem(int itemId, long count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(itemId, 1);
        CheckRange(count);
        if (FindItemEntry(itemId) is not null)
            throw new InvalidOperationException(L.T($"道具 {itemId} 已存在", $"Item {itemId} already exists"));
        var bag = Require(BagMsg, L.T("背包", "bag"));

        var entry = new ProtoMessage();
        entry.SetVarint(ArchiveLayout.ItemId, (ulong)itemId);
        entry.SetVarint(ArchiveLayout.ItemCount, (ulong)count);
        var field = ProtoField.CreateMessage(ArchiveLayout.BagItem, entry);

        int insertAt = -1;
        int lastItemIndex = -1;
        for (int i = 0; i < bag.Fields.Count; i++)
        {
            var f = bag.Fields[i];
            if (f.Number != ArchiveLayout.BagItem)
                continue;
            lastItemIndex = i;
            if (insertAt < 0 && f.TryGetMessage(out var m) && (int)m.GetVarint(ArchiveLayout.ItemId) > itemId)
                insertAt = i;
        }
        if (insertAt >= 0)
            bag.Insert(insertAt, field);
        else if (lastItemIndex >= 0)
            bag.Insert(lastItemIndex + 1, field);
        else
            bag.InsertOrdered(field);
    }

    // ---------- 工具 ----------

    private static ProtoMessage Require(ProtoMessage? msg, string what) =>
        msg ?? throw new InvalidDataException(L.T($"存档中找不到{what}，可能是游戏版本不同", $"Cannot find the {what} in the save; the game version may differ"));

    private static void CheckRange(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxInt32Value);
    }
}
