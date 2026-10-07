using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Data;
using WukongMM.App.Infrastructure;
using WukongMM.Core;
using WukongMM.Core.Game;
using WukongMM.Core.Items;
using WukongMM.Core.Saves;

namespace WukongMM.App.ViewModels;

/// <summary>下拉框里的一个存档槽位（显示文字随界面语言变化）。</summary>
public sealed class SaveSlotItem(SaveSlot slot, int? level, long? money, bool readable, bool recognized) : ObservableObject
{
    public SaveSlot Slot { get; } = slot;

    public string Display
    {
        get
        {
            string head = L.T($"槽位 {Slot.SlotNumber}　SteamID {Slot.SteamId}　保存于 {Slot.LastWriteTime:yyyy-MM-dd HH:mm}",
                              $"Slot {Slot.SlotNumber}　SteamID {Slot.SteamId}　saved {Slot.LastWriteTime:yyyy-MM-dd HH:mm}");
            if (!readable)
                return head + L.T("　（无法读取）", "　(cannot be read)");
            return recognized
                ? head + L.T($"　{level} 级　灵蕴 {money:N0}", $"　Lv {level}　Will {money:N0}")
                : head + L.T("　（结构无法识别）", "　(unrecognized format)");
        }
    }

    public void RefreshDisplay() => OnPropertyChanged(nameof(Display));

    public override string ToString() => Display;
}

/// <summary>"新增道具"下拉框里的一项（只列出可修改范围内、允许加到背包道具列表的道具）。</summary>
public sealed record AddableItem(int Id, string Name, string Category, string Display);

/// <summary>"存档修改"页：等级 / 灵蕴 / 经验 / 灵光点 / 道具数量，写入前自动备份。</summary>
public sealed class SaveEditorViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private SaveFile? _save;
    private ArchiveView? _view;
    private SaveSlotItem? _selectedSlot;
    private string? _loadError;

    private int _origLevel;
    private long _origMoney;
    private long _origExp;
    private long _origTotalExp;
    private int _origSparks;
    private string _levelText = "";
    private string _moneyText = "";
    private string _expText = "";
    private string _sparksText = "";
    private string _playerName = "";
    private string _itemFilter = "";
    private bool _onlyModified;
    private string _addItemText = "";
    private string _addItemCount = "1";
    private SaveBackup? _selectedBackup;
    private bool _suppressSlotLoad;
    private LevelTable? _levels;
    private bool _grantSparks = true;
    private IReadOnlyList<AddableItem> _addableItems = [];
    private AddableItem? _selectedAddable;
    private int _hiddenItemCount;
    private List<ItemClass> _categoryClasses = [];
    private int _categoryFilterIndex;

    public SaveEditorViewModel(MainViewModel main)
    {
        _main = main;
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;

        ReloadCommand = new RelayCommand(() => { ReloadSlots(); }, () => _main.HasGame);
        SaveCommand = new RelayCommand(Save, () => WritingEnabled && IsLoaded && HasChanges && !_main.IsGameRunning);
        RevertCommand = new RelayCommand(() => LoadSelected(), () => IsLoaded && HasChanges);
        AddItemCommand = new RelayCommand(AddItem, () => IsLoaded);
        RestoreBackupCommand = new RelayCommand(RestoreBackup, () => SelectedBackup is not null && !_main.IsGameRunning);
        OpenSaveFolderCommand = new RelayCommand(() => Dialogs.OpenInExplorer(SelectedSlot!.Slot.Path), () => SelectedSlot is not null);
        OpenBackupFolderCommand = new RelayCommand(OpenBackupFolder, () => SelectedSlot is not null);
    }

    /// <summary>
    /// 是否允许"保存到存档"。2026-10-07 用户先要求关闭写入（false），之后要求全部恢复（true）。
    /// 改成 false 时页面仍可查看、试改和预览（"还原所选备份"不受影响）。
    /// </summary>
    public static bool WritingEnabled { get; } = true;

    public bool IsWritingDisabled => !WritingEnabled;

    public ObservableCollection<SaveSlotItem> Slots { get; } = [];
    public ObservableCollection<InventoryRowViewModel> Items { get; } = [];
    public ICollectionView ItemsView { get; }
    public ObservableCollection<SaveBackup> Backups { get; } = [];

    public RelayCommand ReloadCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand RevertCommand { get; }
    public RelayCommand AddItemCommand { get; }
    public RelayCommand RestoreBackupCommand { get; }
    public RelayCommand OpenSaveFolderCommand { get; }
    public RelayCommand OpenBackupFolderCommand { get; }

    public SaveSlotItem? SelectedSlot
    {
        get => _selectedSlot;
        set
        {
            if (SetProperty(ref _selectedSlot, value) && !_suppressSlotLoad)
                LoadSelected();
        }
    }

    public bool IsLoaded => _view is not null;

    public string? LoadError
    {
        get => _loadError;
        private set
        {
            if (SetProperty(ref _loadError, value))
                OnPropertyChanged(nameof(HasLoadError));
        }
    }

    public bool HasLoadError => !string.IsNullOrEmpty(LoadError);

    public bool IsGameRunning => _main.IsGameRunning;

    public bool SteamCloudEnabled => SelectedSlot is { } s && SaveLocator.IsSteamCloudEnabled(s.Slot);

    public string PlayerName
    {
        get => _playerName;
        private set => SetProperty(ref _playerName, value);
    }

    public string LevelText
    {
        get => _levelText;
        set { if (SetProperty(ref _levelText, value)) ProgressChanged(); }
    }

    public string MoneyText
    {
        get => _moneyText;
        set { if (SetProperty(ref _moneyText, value)) Changed(); }
    }

    public string ExpText
    {
        get => _expText;
        set { if (SetProperty(ref _expText, value)) ProgressChanged(); }
    }

    public string SparksText
    {
        get => _sparksText;
        set { if (SetProperty(ref _sparksText, value)) ProgressChanged(); }
    }

    /// <summary>升级时按等级表补发灵光点。</summary>
    public bool GrantSparks
    {
        get => _grantSparks;
        set { if (SetProperty(ref _grantSparks, value)) ProgressChanged(); }
    }

    public bool HasLevelTable => _levels is not null;

    private static string Original(string value) => L.T($"原值 {value}", $"was {value}");

    public string OriginalLevelText => IsLoaded ? Original(_origLevel.ToString()) : "";
    public string OriginalMoneyText => IsLoaded ? Original(_origMoney.ToString("N0")) : "";
    public string OriginalExpText => IsLoaded ? Original(_origExp.ToString("N0")) : "";
    public string OriginalSparksText => IsLoaded ? Original(_origSparks.ToString()) : "";

    private ProgressValues OriginalProgress => new(_origLevel, _origExp, _origTotalExp, _origSparks);

    /// <summary>按当前输入换算出的等级/经验/灵光点（输入不是整数时为 null）。</summary>
    private ProgressPlan? CurrentPlan =>
        IsLoaded && int.TryParse(Clean(LevelText), out int level) && long.TryParse(Clean(ExpText), out long exp)
                 && int.TryParse(Clean(SparksText), out int sparks)
            ? ProgressPlanner.Plan(OriginalProgress, level, exp, sparks, _levels, GrantSparks)
            : null;

    /// <summary>累计经验随等级 / 当前经验同步变化。</summary>
    public string TotalExpText
    {
        get
        {
            if (!IsLoaded)
                return "";
            long total = CurrentPlan is { Error: null } p ? p.Values.TotalExp : _origTotalExp;
            string change = total != _origTotalExp ? $" → {total:N0}" : "";
            return L.T($"累计经验：{_origTotalExp:N0}{change}（自动同步）", $"Total EXP: {_origTotalExp:N0}{change} (synced automatically)");
        }
    }

    /// <summary>等级范围与本级经验上限（来自等级表）。</summary>
    public string LevelLimitText
    {
        get
        {
            if (!IsLoaded)
                return "";
            if (_levels is not { } t)
                return L.T("还没有等级表：无法检查等级上限和本级经验上限（见下方说明）。",
                           "No level table yet: the level range and per-level EXP limit cannot be checked (see the note below).");
            string range = L.T($"游戏等级范围 {t.MinLevel}~{t.MaxLevel}", $"Game level range {t.MinLevel}–{t.MaxLevel}");
            bool parsed = int.TryParse(Clean(LevelText), out int level);
            if (!parsed || !t.Contains(level))
                return parsed && level == _origLevel
                    ? range + L.T($"；存档原有的等级 {level} 不在表里：以后获得的经验全部无效，建议改到范围内。",
                                  $"; the save's level {level} is not in the table: all future EXP gains are void, so changing it into range is recommended.")
                    : range + L.T("。", ".");
            // 最高等级没有下一级（游戏升级时找不到 L+1 行就停止），即使表里这一行的 NextLevelExp 不为 0
            return level == t.MaxLevel
                ? range + L.T($"；{level} 级是最高等级，不会再升级，当前经验最多填 {t.MaxExpAt(level):N0}。",
                              $"; level {level} is the max level and never levels up; current EXP can be at most {t.MaxExpAt(level):N0}.")
                : range + L.T($"；{level} 级升到下一级需要 {t.Row(level)!.NextLevelExp:N0} 经验，当前经验最多填 {t.MaxExpAt(level):N0}。",
                              $"; level {level} needs {t.Row(level)!.NextLevelExp:N0} EXP for the next level, so current EXP can be at most {t.MaxExpAt(level):N0}.");
        }
    }

    /// <summary>升级补发灵光点的预览。</summary>
    public string SparksGrantText => CurrentPlan is { Error: null, GrantedSparks: > 0 } p
        ? L.T($"升级补发灵光点 +{p.GrantedSparks}，保存后灵光点为 {p.Values.SparkPoints}。",
              $"Sparks granted for leveling up: +{p.GrantedSparks}; Sparks will be {p.Values.SparkPoints} after saving.")
        : "";

    public string ProgressErrorText => CurrentPlan?.Error ?? "";

    public bool HasProgressError => ProgressErrorText.Length > 0;

    public string ItemFilter
    {
        get => _itemFilter;
        set { if (SetProperty(ref _itemFilter, value)) RefreshItemsView(); }
    }

    public bool OnlyModified
    {
        get => _onlyModified;
        set { if (SetProperty(ref _onlyModified, value)) RefreshItemsView(); }
    }

    // ---------- 类别筛选 ----------

    /// <summary>类别下拉框：第一项"全部类别"，其后是本存档 / 游戏表里出现的细分类别（带本存档里的数量）。</summary>
    public IReadOnlyList<string> CategoryFilters =>
        [L.T($"全部类别（{Items.Count}）", $"All categories ({Items.Count})"),
         .. _categoryClasses.Select(c => $"{ItemClassifier.DisplayName(c)}{L.T("（", " (")}{Items.Count(i => i.Class == c)}{L.T("）", ")")}")];

    public int CategoryFilterIndex
    {
        get => _categoryFilterIndex;
        set
        {
            // 下拉框换 ItemsSource 时会先把选中项置为 -1，忽略它
            if (value >= 0 && SetProperty(ref _categoryFilterIndex, value))
                RefreshItemsView();
        }
    }

    private ItemClass? SelectedCategory =>
        CategoryFilterIndex > 0 && CategoryFilterIndex <= _categoryClasses.Count ? _categoryClasses[CategoryFilterIndex - 1] : null;

    /// <summary>重新计算类别列表（读档、添加道具、换语言后），尽量保持原来选中的类别。</summary>
    private void RebuildCategoryFilters()
    {
        var keep = SelectedCategory;
        var present = Items.Select(i => i.Class).ToHashSet();
        foreach (var g in _main.Catalog.GameItems)
            present.Add(_main.Catalog.ClassOf(g.Id));
        _categoryClasses = ItemClassifier.EditableClasses.Where(present.Contains).ToList();
        int index = keep is { } k && _categoryClasses.IndexOf(k) is >= 0 and var i ? i + 1 : 0;
        OnPropertyChanged(nameof(CategoryFilters));
        _categoryFilterIndex = index;
        OnPropertyChanged(nameof(CategoryFilterIndex));
    }

    // ---------- 新增道具 ----------

    /// <summary>"新增道具"下拉框里可选的道具（可修改范围内、允许放进背包道具列表的）。</summary>
    public IReadOnlyList<AddableItem> AddableItems
    {
        get => _addableItems;
        private set => SetProperty(ref _addableItems, value);
    }

    /// <summary>下拉框里选中的项（输入框文字仍等于它的显示文字时，以它的 ID 为准）。</summary>
    public AddableItem? SelectedAddable
    {
        get => _selectedAddable;
        set { if (SetProperty(ref _selectedAddable, value)) AddPreviewChanged(); }
    }

    /// <summary>"新增道具"输入框的文字：道具 ID、名称，或从下拉框选中的项。</summary>
    public string AddItemText
    {
        get => _addItemText;
        set { if (SetProperty(ref _addItemText, value)) AddPreviewChanged(); }
    }

    private void AddPreviewChanged()
    {
        OnPropertyChanged(nameof(AddItemPreview));
        OnPropertyChanged(nameof(AddItemPreviewIsProblem));
    }

    public string AddItemCount
    {
        get => _addItemCount;
        set => SetProperty(ref _addItemCount, value);
    }

    /// <summary>输入框下方的预览：将要添加的道具，或不能添加的原因。</summary>
    public string AddItemPreview
    {
        get
        {
            if (!IsLoaded || string.IsNullOrWhiteSpace(AddItemText))
                return "";
            if (ResolveAddItemId(out var ambiguous) is not { } id)
                return UnresolvedMessage(ambiguous);
            var check = CheckAddItem(id);
            return check.Level switch
            {
                ItemCheckLevel.Ok => (Items.Any(i => i.Id == id && (!i.IsNew || i.Count > 0)) ? L.T("将修改：", "Will change: ") : L.T("将添加：", "Will add: ")) + check.Message,
                ItemCheckLevel.Warning => $"⚠ {check.Message}",
                _ => $"✗ {check.Message}",
            };
        }
    }

    public bool AddItemPreviewIsProblem =>
        IsLoaded && !string.IsNullOrWhiteSpace(AddItemText)
                 && (ResolveAddItemId(out _) is not { } id || CheckAddItem(id).Level != ItemCheckLevel.Ok);

    private static string Candidates(IEnumerable<AddableItem> items) =>
        string.Join(" / ", items.Select(a => L.T($"{a.Id}（{a.Category}）", $"{a.Id} ({a.Category})")));

    private static string UnresolvedMessage(IReadOnlyList<AddableItem> ambiguous) => ambiguous.Count > 1
        ? L.T($"有 {ambiguous.Count} 个同名道具：{Candidates(ambiguous)}，请从下拉列表里选择或直接输入 ID。",
              $"There are {ambiguous.Count} items with this name: {Candidates(ambiguous)}. Pick one from the list or enter the ID.")
        : L.T("找不到这个道具：请输入道具 ID，或输入名称后从下拉列表里选择。",
              "Item not found: enter an item ID, or type a name and pick it from the list.");

    private string ItemLabel(int id) => _main.Catalog.NameOf(id) is { } n ? n.Trim() : L.T($"道具 {id}", $"Item {id}");

    /// <summary>
    /// 存档里已有的道具只是改数量（只需任务道具提醒）；没有的（包括表格里待添加、还没写入的行）按新增规则检查。
    /// 下拉框自动补全会默默选中同名道具里排在前面的一个，所以有同名道具时一律提醒。
    /// </summary>
    private ItemCheck CheckAddItem(int id)
    {
        ItemCheck check;
        if (Items.FirstOrDefault(i => i.Id == id && !i.IsNew) is { } row)
        {
            string label = row.Name.Length > 0 ? row.Name.Trim() : L.T($"道具 {id}", $"Item {id}");
            string tail = L.T($"（背包里已有 {row.Count}，会改成新数量）", $" (already {row.Count} in the bag; it will be changed to the new count)");
            check = ItemRules.CountChangeWarning(_main.Catalog, id) is { } w
                ? new(ItemCheckLevel.Warning, L.T($"{label} 是任务道具。", $"{label} is a quest item. ") + w + tail)
                : new(ItemCheckLevel.Ok, label + tail);
        }
        else
        {
            check = ItemRules.CheckAdd(_main.Catalog, id);
            if (!check.IsBlocked && Items.FirstOrDefault(i => i.Id == id && i.IsNew) is { Count: > 0 } pending)
                check = check with
                {
                    Message = check.Message + L.T($"（待添加，数量 {pending.Count}，会改成新数量）",
                                                  $" (pending, count {pending.Count}; it will be changed to the new count)"),
                };
        }
        if (check.IsBlocked || AddableItems.FirstOrDefault(a => a.Id == id) is not { } self)
            return check;
        var sameName = AddableItems.Where(a => a.Id != id && a.Name == self.Name).ToList();
        if (sameName.Count == 0)
            return check;
        string others = L.T($"另有同名：{Candidates(sameName)}，如果要的是那个，请从下拉列表里选择或直接输入它的 ID。",
                            $"Other items share this name: {Candidates(sameName)}. If you meant one of those, pick it from the list or enter its ID.");
        return check.Level == ItemCheckLevel.Ok
            ? new(ItemCheckLevel.Warning, $"{check.Message}{L.T("。", ". ")}{others}")
            : check with { Message = $"{check.Message}\n{others}" };
    }

    public SaveBackup? SelectedBackup
    {
        get => _selectedBackup;
        set => SetProperty(ref _selectedBackup, value);
    }

    public string ItemsSummary => IsLoaded
        ? L.T($"可修改的道具 {Items.Count} 种，已修改 {Items.Count(i => i.IsModified)} 项", $"{Items.Count} editable items, {Items.Count(i => i.IsModified)} modified")
          + (_hiddenItemCount > 0
              ? L.T($"（另有 {_hiddenItemCount} 种剧情 / 兑换等道具不在修改范围，未列出）", $" ({_hiddenItemCount} story/exchange items are outside the editable scope and not listed)")
              : "")
        : "";

    /// <summary>游戏数据表（道具表 / 用途表 / 等级表）的来源说明。</summary>
    public string GameDataText
    {
        get
        {
            var c = _main.Catalog;
            string items = !c.HasGameData
                ? L.T("还没有游戏道具表：安装附带的 WukongMMDataExport mod 后进一次游戏（读档即可），就能显示道具名称、携带上限，并检查新增的道具。",
                      "No game item table yet: install the bundled WukongMMDataExport mod and load a save in the game once to get item names and carry limits and to validate added items.")
                : c.HasExtendedFields
                    ? c.GameDataInfo ?? ""
                    : c.GameDataInfo + L.T("。这是旧版道具表（无法区分酒和关卡道具），再进一次游戏就会更新。",
                                           ". This is an old item table (cannot tell drinks and level items apart); load a save in the game once more to update it.");
            string usage = !c.HasGameData ? ""
                : c.Usage is { } u
                    ? L.T($"道具用途表：导出于 {u.ExportedAt}（按游戏的打造 / 炼丹 / 升级配方决定可修改的道具和类别）。",
                          $"Item usage table: exported {u.ExportedAt} (the game's crafting / alchemy / upgrade recipes decide which items are editable and their categories).")
                    : L.T("还没有道具用途表：可修改的道具和类别按内置表判断（与游戏 1.0.21 一致）。",
                          "No item usage table yet: editable items and categories follow the built-in table (matches game version 1.0.21).");
            string english = L.English && c.HasGameData && !c.HasEnglishNames
                ? L.T("", "No English item names yet: switch the game's text language to English and load a save once (with WukongMMDataExport enabled); until then item names are shown in Chinese.")
                : "";
            string levels = _levels is { } t
                ? L.T($"游戏等级表：{t.MinLevel}~{t.MaxLevel} 级", $"Game level table: levels {t.MinLevel}–{t.MaxLevel}")
                  + (t.ExportedAt is { } at ? L.T($"（导出于 {at}）。", $" (exported {at}).") : L.T("。", "."))
                : L.T("还没有游戏等级表：需要新版 WukongMMDataExport（v0.2 起），进一次游戏读档后自动生成。",
                      "No game level table yet: it is generated by WukongMMDataExport (v0.2+) after loading a save in the game once.");
            return string.Join("\n", new[] { items, usage, english, levels }.Where(s => s.Length > 0));
        }
    }

    public bool HasChanges =>
        IsLoaded && (Clean(LevelText) != _origLevel.ToString() || Clean(MoneyText) != _origMoney.ToString()
                     || Clean(ExpText) != _origExp.ToString() || Clean(SparksText) != _origSparks.ToString()
                     || Items.Any(i => i.IsModified));

    public void OnGameRunningChanged() => OnPropertyChanged(nameof(IsGameRunning));

    /// <summary>界面语言切换：重新生成所有算出来的文字（不重读存档，未保存的修改保留）。</summary>
    public void OnLanguageChanged()
    {
        foreach (var slot in Slots)
            slot.RefreshDisplay();
        foreach (var row in Items)
            row.OnLanguageChanged();
        RebuildAddableItemsKeepingInput();
        OnPropertyChanged(string.Empty);
        RebuildCategoryFilters();
        RefreshItemsView();
        var keepBackup = SelectedBackup?.Directory;
        LoadBackups(); // 备份原因的显示文字随语言变化
        SelectedBackup = Backups.FirstOrDefault(b => b.Directory == keepBackup);
    }

    public void OnItemCountChanged()
    {
        OnPropertyChanged(nameof(ItemsSummary));
        AddPreviewChanged();
        Changed();
    }

    public void OnItemNameChanged(InventoryRowViewModel row)
    {
        _main.Catalog.SetUserName(row.Id, row.Name);
        try
        {
            if (!_main.IsTestMode)
                _main.Catalog.Save();
        }
        catch (Exception ex)
        {
            _main.StatusText = L.T($"保存道具名称失败：{ex.Message}", $"Failed to save the item name: {ex.Message}");
        }
        // 名称按 ID 保存：同 ID 的重复条目一起改；清空名称时退回游戏里的名称
        string name = _main.Catalog.NameOf(row.Id) ?? "";
        foreach (var r in Items.Where(i => i.Id == row.Id))
            r.SyncName(name);
        RebuildAddableItemsKeepingInput();
        AddPreviewChanged();
    }

    /// <summary>
    /// 重建下拉框（改名、换语言后）。换掉 ItemsSource 时 WPF 找不到旧的选中项（记录不再相等），
    /// 会把输入框清空，所以先记下、换完再恢复（选中的道具按 ID 重新选中，显示新文字）。
    /// </summary>
    private void RebuildAddableItemsKeepingInput()
    {
        var keep = SelectedAddable;
        string keepText = AddItemText;
        AddableItems = BuildAddableItems();
        if (keep is not null && keepText.Trim() == keep.Display
                             && AddableItems.FirstOrDefault(a => a.Id == keep.Id) is { } again)
        {
            SelectedAddable = again;
            AddItemText = again.Display;
        }
        else if (AddItemText != keepText)
        {
            AddItemText = keepText;
        }
    }

    /// <summary>刷新道具表筛选；表格还有未提交的行编辑时先提交，否则 Refresh 会抛异常。</summary>
    private void RefreshItemsView()
    {
        if (ItemsView is IEditableCollectionView { IsEditingItem: true } editing)
            editing.CommitEdit();
        ItemsView.Refresh();
    }

    private void Changed() => OnPropertyChanged(nameof(HasChanges));

    /// <summary>等级 / 经验 / 灵光点 / 补发选项变化后刷新相关提示。</summary>
    private void ProgressChanged()
    {
        foreach (var name in new[]
                 {
                     nameof(TotalExpText), nameof(LevelLimitText), nameof(SparksGrantText),
                     nameof(ProgressErrorText), nameof(HasProgressError),
                 })
            OnPropertyChanged(name);
        Changed();
    }

    /// <summary>去掉空白和千位分隔符。</summary>
    private static string Clean(string text) => text.Trim().Replace(",", "");

    // ---------- 加载 ----------

    /// <summary>重新扫描存档槽位（保留当前选择）。</summary>
    public void ReloadSlots()
    {
        string? keep = SelectedSlot?.Slot.Path ?? _main.Settings.LastSavePath;
        _suppressSlotLoad = true;
        Slots.Clear();
        if (_main.Paths is { } paths)
        {
            foreach (var slot in SaveLocator.FindSlots(paths))
                Slots.Add(Describe(slot));
        }
        SelectedSlot = Slots.FirstOrDefault(s => string.Equals(s.Slot.Path, keep, StringComparison.OrdinalIgnoreCase))
                       ?? Slots.FirstOrDefault();
        _suppressSlotLoad = false;
        LoadSelected();
    }

    private static SaveSlotItem Describe(SaveSlot slot)
    {
        try
        {
            var v = new ArchiveView(SaveFile.Load(slot.Path));
            return new SaveSlotItem(slot, v.Level, v.Money, readable: true, recognized: v.IsRecognized);
        }
        catch (Exception)
        {
            return new SaveSlotItem(slot, null, null, readable: false, recognized: false);
        }
    }

    private void LoadSelected()
    {
        _save = null;
        _view = null;
        _levels = null;
        _hiddenItemCount = 0;
        LoadError = null;
        Items.Clear();
        if (SelectedSlot is { } item)
        {
            try
            {
                var save = SaveFile.Load(item.Slot.Path);
                var view = new ArchiveView(save);
                if (!view.IsRecognized)
                    throw new InvalidDataException(L.T("存档结构无法识别（可能是游戏版本更新导致格式变化）。",
                                                       "The save format is not recognized (a game update may have changed it)."));
                if (!save.OriginalChecksumValid)
                    LoadError = L.T("注意：这个存档文件的校验值不正确，游戏会提示“存档完整性校验失败”。" +
                                    "可以在右侧“备份”里还原；或者随便改一个值再保存，本工具会重新计算校验值。",
                                    "Note: this save file has a wrong checksum and the game will report an integrity check failure. " +
                                    "Restore it from Backups, or change any value and save; this tool recalculates the checksum.");
                _save = save;
                _view = view;
                _origLevel = view.Level ?? 0;
                _origMoney = view.Money ?? 0;
                _origExp = view.CurrentExp ?? 0;
                _origTotalExp = view.TotalExp ?? 0;
                _origSparks = view.SparkPoints ?? 0;
                PlayerName = view.PlayerName ?? "";
                if (_main.Paths is { } gp)
                {
                    _main.Catalog.LoadGameData(gp.GameDataItemsFile);
                    _levels = LevelTable.TryLoad(gp.GameDataLevelsFile);
                }
                AddableItems = BuildAddableItems();
                // 只列出可修改范围内的道具（打造 / 炼丹 / 升级材料、丹药、种子）；其余道具留在存档里原样不动。
                // 同一 ID 出现多次时游戏只用第一条（PlayerBag.FindBagItem），后面的显示为只读
                var seen = new HashSet<int>();
                foreach (var it in view.GetItems())
                {
                    bool duplicate = !seen.Add(it.Id);
                    if (!_main.Catalog.IsEditable(it.Id))
                    {
                        if (!duplicate)
                            _hiddenItemCount++;
                        continue;
                    }
                    Items.Add(new InventoryRowViewModel(this, _main.Catalog, it.Id, it.Count, it.StoreNum, isDuplicate: duplicate));
                }
                _main.Settings.LastSavePath = item.Slot.Path;
                _main.TrySaveSettings();
                _main.StatusText = L.T($"已读取存档：{item.Slot.Path}", $"Save loaded: {item.Slot.Path}");
            }
            catch (Exception ex)
            {
                LoadError = L.T($"读取存档失败：{ex.Message}", $"Failed to read the save: {ex.Message}");
            }
        }

        _levelText = IsLoaded ? _origLevel.ToString() : "";
        _moneyText = IsLoaded ? _origMoney.ToString() : "";
        _expText = IsLoaded ? _origExp.ToString() : "";
        _sparksText = IsLoaded ? _origSparks.ToString() : "";
        _addItemText = "";
        _selectedAddable = null;
        foreach (var name in new[]
                 {
                     nameof(IsLoaded), nameof(LevelText), nameof(MoneyText), nameof(ExpText), nameof(SparksText),
                     nameof(OriginalLevelText), nameof(OriginalMoneyText), nameof(OriginalExpText), nameof(OriginalSparksText),
                     nameof(ItemsSummary), nameof(SteamCloudEnabled), nameof(GameDataText), nameof(HasLevelTable),
                     nameof(AddItemText), nameof(SelectedAddable), nameof(AddItemPreview), nameof(AddItemPreviewIsProblem),
                 })
            OnPropertyChanged(name);
        ProgressChanged();
        RebuildCategoryFilters();
        RefreshItemsView();
        LoadBackups();
    }

    private void LoadBackups()
    {
        Backups.Clear();
        if (_main.Paths is { } paths && SelectedSlot is { } s)
        {
            foreach (var b in new SaveBackupService(paths).List(s.Slot.SteamId))
                Backups.Add(b);
        }
    }

    private bool FilterItem(object o)
    {
        if (o is not InventoryRowViewModel row)
            return false;
        if (OnlyModified && !row.IsModified)
            return false;
        if (SelectedCategory is { } cls && row.Class != cls)
            return false;
        if (string.IsNullOrWhiteSpace(ItemFilter))
            return true;
        string q = ItemFilter.Trim();
        return row.Id.ToString().Contains(q) || row.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
               || row.Category.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    // ---------- 新增道具 ----------

    /// <summary>下拉框候选：可修改范围内、允许放进背包道具列表的道具，按类别、ID 排序。</summary>
    private IReadOnlyList<AddableItem> BuildAddableItems() =>
        _main.Catalog.GameItems
            .Where(g => _main.Catalog.IsEditable(g.Id) && !ItemRules.CheckAdd(_main.Catalog, g.Id).IsBlocked)
            .OrderBy(g => Array.IndexOf(ItemClassifier.EditableClasses, _main.Catalog.ClassOf(g.Id))).ThenBy(g => g.Id)
            .Select(g =>
            {
                string name = (_main.Catalog.NameOf(g.Id) ?? g.Name).Trim();
                string category = _main.Catalog.CategoryOf(g.Id);
                return new AddableItem(g.Id, name, category, L.T($"{name}（{g.Id}，{category}）", $"{name} ({g.Id}, {category})"));
            })
            .ToList();

    /// <summary>显示文字末尾的"（ID，类别）"或"(ID, 类别)"；锚定到结尾，避免用户备注名里的"（数字，"被误认。</summary>
    private static readonly Regex DisplayIdPattern = new(@"(?:（(\d+)，[^（）]*）|\((\d+), [^()]*\))$", RegexOptions.Compiled);

    /// <summary>
    /// 把输入框文字解析成道具 ID：下拉框选中项、纯数字、显示文字末尾的 ID、或唯一匹配的道具名称。
    /// 有多个同名道具时返回 null，并在 <paramref name="ambiguous"/> 里给出候选。
    /// </summary>
    private int? ResolveAddItemId(out IReadOnlyList<AddableItem> ambiguous)
    {
        ambiguous = [];
        string text = AddItemText.Trim();
        if (text.Length == 0)
            return null;
        if (SelectedAddable is { } selected && text == selected.Display)
            return selected.Id;
        if (int.TryParse(text, out int id))
            return id;
        if (DisplayIdPattern.Match(text) is { Success: true } m
            && int.TryParse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, out id))
            return id;
        var byName = AddableItems.Where(a => string.Equals(a.Name, text, StringComparison.CurrentCultureIgnoreCase)).ToList();
        if (byName.Count == 1)
            return byName[0].Id;
        ambiguous = byName;
        return null;
    }

    private void AddItem()
    {
        if (ResolveAddItemId(out var ambiguous) is not { } id)
        {
            Dialogs.Error(UnresolvedMessage(ambiguous));
            return;
        }
        if (!long.TryParse(Clean(AddItemCount), out long count) || count < 0 || count > int.MaxValue)
        {
            Dialogs.Error(L.T("数量必须是 0 到 2147483647 之间的整数。", "The count must be an integer from 0 to 2147483647."));
            return;
        }
        // 同一 ID 要么是存档里已有的行（可能有重复条目，第一条可编辑），要么是一行待添加（IsNew，还没写入）
        var row = Items.FirstOrDefault(i => i.Id == id);
        if (row is not { IsNew: false } && count == 0)
        {
            // 数量 0 的条目也算"背包里有"：例如种子有条目就不再掉落
            Dialogs.Error(row is { Count: > 0 }
                ? L.T("新增道具的数量至少为 1。要取消这个待添加的道具，把表格里它的数量改成 0 即可。",
                      "A new item needs a count of at least 1. To cancel this pending item, set its count in the table to 0.")
                : L.T("新增道具的数量至少为 1。", "A new item needs a count of at least 1."));
            return;
        }
        var check = CheckAddItem(id);
        if (check.IsBlocked)
        {
            Dialogs.Error(check.Message);
            return;
        }
        string label = _main.Catalog.NameOf(id) is { } n ? L.T($"{n.Trim()}（{id}）", $"{n.Trim()} ({id})") : $"ID {id}";
        var warnings = new List<string>();
        if (check.Level == ItemCheckLevel.Warning)
            warnings.Add(check.Message);
        if (ItemRules.CheckCount(_main.Catalog, id, count) is { Level: ItemCheckLevel.Warning } countCheck)
            warnings.Add(countCheck.Message);
        if (warnings.Count > 0 &&
            !Dialogs.Confirm(L.T($"添加 {label}，数量 {count}：\n\n{string.Join("\n\n", warnings)}\n\n仍要添加吗？",
                                 $"Add {label}, count {count}:\n\n{string.Join("\n\n", warnings)}\n\nAdd anyway?"), warning: true))
            return;

        string later = L.T("（点“保存到存档”后生效）", " (takes effect after Save to file)");
        if (row is null)
        {
            Items.Add(new InventoryRowViewModel(this, _main.Catalog, id, count, 0, isNew: true));
            _main.StatusText = L.T($"已添加 {label} ×{count}", $"Added {label} ×{count}") + later;
        }
        else
        {
            long before = row.Count;
            row.Count = count;
            _main.StatusText = (!row.IsNew
                ? L.T($"{label} 背包里已有，已把数量改为 {count}", $"{label} is already in the bag; its count was changed to {count}")
                : before > 0
                    ? L.T($"已把待添加的 {label} 数量改为 {count}", $"Pending {label} count changed to {count}")
                    : L.T($"已添加 {label} ×{count}", $"Added {label} ×{count}")) + later; // 之前添加后又把数量改成 0 取消了
        }
        SelectedAddable = null;
        AddItemText = "";
        OnItemCountChanged();
        RebuildCategoryFilters();
        RefreshItemsView();
    }

    // ---------- 保存 ----------

    private void Save()
    {
        if (!WritingEnabled || _save is null || _view is null || SelectedSlot is null || _main.Paths is null)
            return;
        if (_main.IsGameRunning)
        {
            Dialogs.Error(L.T("游戏正在运行，不能修改存档。请先完全退出游戏。", "The game is running; the save cannot be changed. Please quit the game completely first."));
            return;
        }
        // 没改的输入直接用原值、不查范围：存档原有的不合规值（等级不在表里、负经验、灵蕴超过 int32 等）不挡住其他修改，
        // 交给 ProgressPlanner 和下面的"注意"提醒。灵光点 / 累计经验的建议上限由 ProgressPlanner 检查；这里只做基本范围
        if (!TryReadInput(LevelText, _origLevel, L.T("等级", "Level"), _levels?.MinLevel ?? 1, _levels?.MaxLevel ?? ProgressPlanner.FallbackMaxLevel, out long levelIn)
            || !TryReadInput(MoneyText, _origMoney, L.T("灵蕴", "Will"), 0, int.MaxValue, out long money)
            || !TryReadInput(ExpText, _origExp, L.T("当前经验", "Current EXP"), 0, int.MaxValue, out long expIn)
            || !TryReadInput(SparksText, _origSparks, L.T("灵光点", "Sparks"), 0, int.MaxValue, out long sparksIn))
            return;
        // 灵蕴上限只拦"改大并超过上限"：存档原本就超过时，不改或往下改都允许（SAVE_FORMAT §8.3）
        if (money > _origMoney && money > ArchiveView.MaxSafeMoney)
        {
            Dialogs.Error(L.T($"灵蕴建议不超过 {ArchiveView.MaxSafeMoney:N0}：游戏以后获得灵蕴时不检查上限，商店和界面按 int32 读取，接近 21 亿时就会溢出成负数。",
                              $"Will should not exceed {ArchiveView.MaxSafeMoney:N0}: the game does not check the limit when adding Will later, and shops and the UI read it as int32, so it overflows to a negative number near 2.1 billion."));
            return;
        }
        var plan = ProgressPlanner.Plan(OriginalProgress, (int)levelIn, expIn, (int)sparksIn, _levels, GrantSparks);
        if (plan.Error is { } planError)
        {
            Dialogs.Error(planError);
            return;
        }
        var pv = plan.Values;
        bool levelChanged = pv.Level != _origLevel;
        bool expChanged = pv.CurrentExp != _origExp || pv.TotalExp != _origTotalExp;
        bool sparksChanged = pv.SparkPoints != _origSparks;

        var changed = Items.Where(i => i.IsModified).ToList();
        var sb = new StringBuilder();
        if (levelChanged) sb.AppendLine(L.T($"等级：{_origLevel} → {pv.Level}", $"Level: {_origLevel} → {pv.Level}"));
        if (money != _origMoney) sb.AppendLine(L.T($"灵蕴：{_origMoney:N0} → {money:N0}", $"Will: {_origMoney:N0} → {money:N0}"));
        if (expChanged)
            sb.AppendLine(pv.CurrentExp != _origExp
                ? L.T($"当前经验：{_origExp:N0} → {pv.CurrentExp:N0}（累计经验 {_origTotalExp:N0} → {pv.TotalExp:N0}）",
                      $"Current EXP: {_origExp:N0} → {pv.CurrentExp:N0} (total EXP {_origTotalExp:N0} → {pv.TotalExp:N0})")
                : L.T($"累计经验：{_origTotalExp:N0} → {pv.TotalExp:N0}（随等级同步）",
                      $"Total EXP: {_origTotalExp:N0} → {pv.TotalExp:N0} (synced with the level)"));
        if (sparksChanged) sb.AppendLine(L.T($"灵光点：{_origSparks} → {pv.SparkPoints}", $"Sparks: {_origSparks} → {pv.SparkPoints}"));
        foreach (var row in changed.Take(15))
        {
            string name = row.Name.Length > 0 ? L.T($"（{row.Name.Trim()}）", $" ({row.Name.Trim()})") : "";
            sb.AppendLine(L.T($"道具 {row.Id}{name}：{row.ChangeText}", $"Item {row.Id}{name}: {row.ChangeText}"));
        }
        if (changed.Count > 15)
            sb.AppendLine(L.T($"……另有 {changed.Count - 15} 项道具", $"… and {changed.Count - 15} more items"));
        if (sb.Length == 0)
        {
            Dialogs.Info(L.T("没有需要保存的修改。", "There are no changes to save."));
            return;
        }

        // 不会让读档失败、但需要用户知道的后果（SAVE_FORMAT §8.2/§8.3）
        var notes = new List<string>(plan.Notes);
        if (money > ArchiveView.MaxSafeMoney)
            notes.Add(L.T($"灵蕴 {money:N0} 超过建议上限 {ArchiveView.MaxSafeMoney:N0}（存档原本就超过），游戏以后获得灵蕴时可能溢出",
                          $"Will {money:N0} is above the recommended limit {ArchiveView.MaxSafeMoney:N0} (already so in the save); it may overflow when the game adds Will"));
        foreach (var row in changed)
        {
            string label = row.Name.Length > 0 ? row.Name.Trim() : L.T($"道具 {row.Id}", $"Item {row.Id}");
            string sep = L.T("：", ": ");
            if (ItemRules.CheckCount(_main.Catalog, row.Id, row.Count) is { Level: ItemCheckLevel.Warning } c)
                notes.Add(label + sep + c.Message);
            // 新增的道具重复添加时的提醒（种子 / 旧表消耗品）；已有的道具只需任务道具提醒
            if (row.IsNew && ItemRules.CheckAdd(_main.Catalog, row.Id) is { Level: ItemCheckLevel.Warning } add)
                notes.Add(label + sep + add.Message.Replace("\n", ""));
            else if (!row.IsNew && ItemRules.CountChangeWarning(_main.Catalog, row.Id) is { } w)
                notes.Add(label + sep + w);
            if (row.StoreNum > 0 && row.Count < row.OriginalCount && row.CarryMax is not null)
                notes.Add(label + sep + L.T($"仓储里还有 {row.StoreNum}，在神龛休息时会补进携带数量；要减少请把“仓储”也改小",
                                            $"{row.StoreNum} more in storage will refill the carried count when resting at a shrine; lower Storage too if you want fewer"));
        }
        string warn = notes.Count > 0
            ? L.T("注意：\n", "Notes:\n") + string.Join("\n", notes.Take(12).Select(n => "· " + n))
              + (notes.Count > 12 ? L.T($"\n· ……另有 {notes.Count - 12} 条", $"\n· … and {notes.Count - 12} more") : "") + "\n\n"
            : "";

        string cloud = SteamCloudEnabled
            ? L.T("\n· 这个存档开启了 Steam 云同步。下次启动游戏时 Steam 一般会上传本地的新存档；如果弹出冲突提示，请选择“本地”文件。",
                  "\n· Steam Cloud is on for this save. Steam usually uploads the new local save the next time you start the game; if a conflict prompt appears, choose the local file.")
            : "";
        if (!Dialogs.Confirm(
                L.T($"将写入以下修改：\n\n{sb}\n{warn}", $"The following changes will be written:\n\n{sb}\n{warn}") +
                L.T("· 写入前会自动备份当前存档，出问题可在右侧“备份”里一键还原。\n", "· The current save is backed up automatically first; restore it from Backups if anything goes wrong.\n") +
                L.T("· 会按游戏的算法重新计算存档校验值。", "· The save checksum is recalculated with the game's algorithm.") + cloud +
                L.T("\n\n确定写入？", "\n\nWrite now?"), warning: true))
            return;

        try
        {
            if (levelChanged) _view.SetLevel(pv.Level);
            if (money != _origMoney) _view.SetMoney(money);
            if (expChanged) _view.SetExp(pv.CurrentExp, pv.TotalExp);
            if (sparksChanged) _view.SetSparkPoints(pv.SparkPoints);
            foreach (var row in changed)
            {
                _view.SetItemCount(row.Id, row.Count);
                if (row.StoreNum != row.OriginalStoreNum)
                    _view.SetItemStoreNum(row.Id, row.StoreNum);
            }

            // 重复条目不可编辑，changed 里每个 ID 只有一行；核对时也按游戏的规则只看第一条
            var expectItems = changed.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => (g.First().Count, g.First().StoreNum));
            var result = SaveWriter.Write(_save, SelectedSlot.Slot, new SaveBackupService(_main.Paths), v =>
            {
                if (v.Level != pv.Level) return L.T("等级未生效", "level not applied");
                if (v.Money != money) return L.T("灵蕴未生效", "Will not applied");
                if (v.CurrentExp != pv.CurrentExp || v.TotalExp != pv.TotalExp) return L.T("经验未生效", "EXP not applied");
                if (v.SparkPoints != pv.SparkPoints) return L.T("灵光点未生效", "Sparks not applied");
                var now = v.GetItems().GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
                foreach (var (id, (count, store)) in expectItems)
                    if (!now.TryGetValue(id, out var it) || it.Count != count || it.StoreNum != store)
                        return L.T($"道具 {id} 数量未生效", $"item {id} count not applied");
                return null;
            });
            Dialogs.Info(L.T($"已写入存档。\n\n修改前的备份：\n{result.Backup.Directory}", $"The save was written.\n\nBackup of the previous save:\n{result.Backup.Directory}"));
            _main.StatusText = L.T($"存档已保存（备份：{Path.GetFileName(result.Backup.Directory)}）", $"Save written (backup: {Path.GetFileName(result.Backup.Directory)})");
        }
        catch (Exception ex)
        {
            Dialogs.Error(L.T($"保存失败，存档未改动：\n{ex.Message}", $"Saving failed; the save file was not changed:\n{ex.Message}"));
        }
        ReloadSlots();
    }

    /// <summary>读取输入框：和原值一样时直接返回原值（不查范围）；改了才要求是 min~max 之间的整数。</summary>
    private static bool TryReadInput(string text, long original, string what, long min, long max, out long value)
    {
        string s = Clean(text);
        if (s == original.ToString())
        {
            value = original;
            return true;
        }
        if (long.TryParse(s, out value) && value >= min && value <= max)
            return true;
        Dialogs.Error(L.T($"{what}必须是 {min:N0} 到 {max:N0} 之间的整数。", $"{what} must be an integer from {min:N0} to {max:N0}."));
        return false;
    }

    // ---------- 备份 ----------

    private void RestoreBackup()
    {
        if (SelectedBackup is not { } backup || _main.Paths is null)
            return;
        if (!Dialogs.Confirm(L.T($"把存档还原到这个备份？\n\n{backup.DisplayText}\n\n当前的存档会先自动备份一份。",
                                 $"Restore the save to this backup?\n\n{backup.DisplayText}\n\nThe current save is backed up automatically first."), warning: true))
            return;
        try
        {
            new SaveBackupService(_main.Paths).Restore(backup);
            Dialogs.Info(L.T("已还原。", "Restored."));
        }
        catch (Exception ex)
        {
            Dialogs.Error(L.T($"还原失败：{ex.Message}", $"Restore failed: {ex.Message}"));
        }
        ReloadSlots();
    }

    private void OpenBackupFolder()
    {
        if (_main.Paths is null || SelectedSlot is null)
            return;
        string dir = new SaveBackupService(_main.Paths).RootFor(SelectedSlot.Slot.SteamId);
        Directory.CreateDirectory(dir);
        Dialogs.OpenInExplorer(dir);
    }
}
