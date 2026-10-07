using WukongMM.App.Infrastructure;
using WukongMM.Core;
using WukongMM.Core.Items;

namespace WukongMM.App.ViewModels;

/// <summary>存档页道具表的一行（只有可修改范围内的道具才会有行）。</summary>
public sealed class InventoryRowViewModel : ObservableObject
{
    private readonly SaveEditorViewModel _owner;
    private readonly ItemCatalog _catalog;
    private long _count;
    private long _storeNum;
    private string _name;

    public InventoryRowViewModel(SaveEditorViewModel owner, ItemCatalog catalog, int id, long count, long storeNum,
                                 bool isNew = false, bool isDuplicate = false)
    {
        _owner = owner;
        _catalog = catalog;
        Id = id;
        OriginalCount = isNew ? -1 : count;
        OriginalStoreNum = isNew ? 0 : storeNum;
        _count = count;
        _storeNum = storeNum;
        _name = catalog.NameOf(id) ?? "";
        IsNew = isNew;
        IsDuplicate = isDuplicate;
        Class = catalog.ClassOf(id);
        CarryMax = catalog.CarryMaxOf(id);
        IsStorable = catalog.IsStorable(id);
        ExistsInGame = catalog.ExistsInGame(id);
    }

    /// <summary>细分类别（筛选用）。</summary>
    public ItemClass Class { get; }

    /// <summary>游戏表里的携带上限（未知为 null）。</summary>
    public int? CarryMax { get; }

    public string CarryMaxText => CarryMax?.ToString() ?? "";

    /// <summary>可仓储的丹药：获得时超出上限的部分进仓储，不受携带上限限制。</summary>
    public bool IsStorable { get; }

    /// <summary>游戏表里有没有这个道具（游戏表未导出时为 null）。</summary>
    public bool? ExistsInGame { get; }

    /// <summary>数量超过携带上限（可仓储丹药除外）：之后捡不到、买不到。</summary>
    public bool ExceedsCarryMax => CarryMax is { } max && Count > max && !IsStorable;

    public int Id { get; }

    /// <summary>读档时的数量；新增的道具为 -1。</summary>
    public long OriginalCount { get; }

    /// <summary>读档时的仓储数量（RoleItem.StoreNum）。</summary>
    public long OriginalStoreNum { get; }

    public bool IsNew { get; }

    /// <summary>存档里同一 ID 的第二条及以后：游戏只用第一条（PlayerBag.FindBagItem），这里只读显示。</summary>
    public bool IsDuplicate { get; }

    public string OriginalText => IsNew ? L.T("（新增）", "(new)") : OriginalCount.ToString();

    /// <summary>保存确认框里的变化描述。</summary>
    public string ChangeText
    {
        get
        {
            string count = $"{OriginalText} → {Count}";
            return StoreNum != OriginalStoreNum ? count + L.T($"，仓储 {OriginalStoreNum} → {StoreNum}", $", storage {OriginalStoreNum} → {StoreNum}") : count;
        }
    }

    public string Category => _catalog.CategoryOf(Id) + (IsDuplicate ? L.T("（重复条目，游戏不使用）", " (duplicate entry, unused by the game)") : "");

    /// <summary>名称：用户备注优先，否则是游戏里的名称（随界面语言）。</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? ""))
                _owner.OnItemNameChanged(this);
        }
    }

    /// <summary>名称按 ID 保存：同 ID 的某一行改名后，用它同步所有行的显示（不回调 owner）。</summary>
    internal void SyncName(string name)
    {
        if (_name == name)
            return;
        _name = name;
        OnPropertyChanged(nameof(Name));
    }

    /// <summary>界面语言切换：名称换成对应语言的游戏名（用户备注不变），类别等文字重新生成。</summary>
    internal void OnLanguageChanged()
    {
        SyncName(_catalog.NameOf(Id) ?? "");
        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(OriginalText));
    }

    public long Count
    {
        get => _count;
        set
        {
            long v = IsDuplicate ? _count : Math.Clamp(value, 0, int.MaxValue);
            if (SetProperty(ref _count, v))
            {
                OnPropertyChanged(nameof(IsModified));
                OnPropertyChanged(nameof(ExceedsCarryMax));
                _owner.OnItemCountChanged();
            }
            else if (v != value)
            {
                OnPropertyChanged(); // 把被拒绝的输入刷回界面
            }
        }
    }

    /// <summary>仓储数量：在神龛休息时补进携带数量（任何有携带上限的道具）。新增道具固定为 0。</summary>
    public long StoreNum
    {
        get => _storeNum;
        set
        {
            long v = IsNew || IsDuplicate ? _storeNum : Math.Clamp(value, 0, int.MaxValue);
            if (SetProperty(ref _storeNum, v))
            {
                OnPropertyChanged(nameof(IsModified));
                _owner.OnItemCountChanged();
            }
            else if (v != value)
            {
                OnPropertyChanged(); // 把被拒绝的输入刷回界面
            }
        }
    }

    /// <summary>需要写入存档：新增且数量 &gt; 0（数量改回 0 视为撤销新增），或已有条目的数量 / 仓储变了。</summary>
    public bool IsModified => IsNew ? Count > 0 : !IsDuplicate && (Count != OriginalCount || StoreNum != OriginalStoreNum);
}
