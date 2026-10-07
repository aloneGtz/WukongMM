using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using WukongMM.App.Infrastructure;
using WukongMM.App.Views;
using WukongMM.Core;
using WukongMM.Core.Mods;

namespace WukongMM.App.ViewModels;

/// <summary>"Mod 管理"页：列表、筛选、逐个启停、备注、自定义分组、资源冲突提醒。</summary>
public sealed class ModsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private ModManager? _manager;
    private ModItemViewModel? _selected;
    private string _searchText = "";
    private int _kindFilterIndex;
    private int _stateFilterIndex;
    private bool _showFrameworkBuiltin;
    private bool _busy;

    /// <summary>状态筛选的第 4 项：有资源冲突（不是 ModState）。</summary>
    private const int StateFilterResourceConflict = 4;

    public ModsViewModel(MainViewModel main)
    {
        _main = main;
        View = CollectionViewSource.GetDefaultView(Items);
        View.Filter = Filter;

        RefreshCommand = new RelayCommand(Reload, () => _main.HasGame);
        OpenModsFolderCommand = new RelayCommand(OpenModsFolder, () => _main.HasGame);
        OpenDisabledStoreCommand = new RelayCommand(OpenDisabledStore, () => _main.HasGame);
        NewCustomModCommand = new RelayCommand(NewCustomMod, () => _manager is not null && !_main.IsGameRunning);
        RemoveCustomModCommand = new RelayCommand(RemoveCustomMod, () => Selected is { IsCustom: true } && !_main.IsGameRunning);
        SaveMetadataCommand = new RelayCommand(SaveMetadata, () => Selected is not null);
        OpenSelectedLocationCommand = new RelayCommand(OpenSelectedLocation, () => Selected is not null);
        OpenSourceUrlCommand = new RelayCommand(OpenSourceUrl, () => Uri.TryCreate(Selected?.EditSourceUrl, UriKind.Absolute, out _));
    }

    public ObservableCollection<ModItemViewModel> Items { get; } = [];

    public ICollectionView View { get; }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenModsFolderCommand { get; }
    public RelayCommand OpenDisabledStoreCommand { get; }
    public RelayCommand NewCustomModCommand { get; }
    public RelayCommand RemoveCustomModCommand { get; }
    public RelayCommand SaveMetadataCommand { get; }
    public RelayCommand OpenSelectedLocationCommand { get; }
    public RelayCommand OpenSourceUrlCommand { get; }

    public IReadOnlyList<string> KindFilters =>
        [L.T("全部类型", "All types"), ModKind.Pak.ToDisplay(), ModKind.LogicMod.ToDisplay(), ModKind.CSharpLoader.ToDisplay(), ModKind.UE4SS.ToDisplay(), ModKind.Custom.ToDisplay()];

    public IReadOnlyList<string> StateFilters =>
        [L.T("全部状态", "All states"), L.T("已启用", "Enabled"), L.T("已禁用", "Disabled"), L.T("重复", "Duplicate"), L.T("有资源冲突", "With conflicts")];

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                View.Refresh();
        }
    }

    public int KindFilterIndex
    {
        get => _kindFilterIndex;
        set
        {
            // 换语言时下拉框重设 ItemsSource 会先把选中项置为 -1，忽略它，保持原来的筛选
            if (value >= 0 && SetProperty(ref _kindFilterIndex, value))
                View.Refresh();
        }
    }

    public int StateFilterIndex
    {
        get => _stateFilterIndex;
        set
        {
            if (value >= 0 && SetProperty(ref _stateFilterIndex, value))
                View.Refresh();
        }
    }

    /// <summary>显示 UE4SS 自带的内置脚本（框架的一部分，默认隐藏，免得一堆"已启用"条目干扰）。</summary>
    public bool ShowFrameworkBuiltin
    {
        get => _showFrameworkBuiltin;
        set
        {
            if (SetProperty(ref _showFrameworkBuiltin, value))
            {
                View.Refresh();
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public ModItemViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => Selected is not null;

    public string SummaryText
    {
        get
        {
            if (_manager is null)
                return L.T("未找到游戏目录", "Game folder not found");
            var mods = Items.Where(i => !i.Entry.IsFrameworkBuiltin).ToList();
            int on = mods.Count(i => i.Entry.State == ModState.Enabled);
            int off = mods.Count(i => i.Entry.State == ModState.Disabled);
            int dup = mods.Count - on - off;
            int builtin = Items.Count - mods.Count;
            return L.T($"共 {mods.Count} 个 mod：已启用 {on}，已禁用 {off}", $"{mods.Count} mods: {on} enabled, {off} disabled")
                   + (dup > 0 ? L.T($"，重复 {dup}", $", {dup} duplicate") : "")
                   + (builtin > 0
                       ? L.T($"（另有 {builtin} 个框架内置组件{(ShowFrameworkBuiltin ? "" : "，已隐藏")}）",
                             $" (plus {builtin} built-in framework components{(ShowFrameworkBuiltin ? "" : ", hidden")})")
                       : "");
        }
    }

    public bool IsEmpty => _manager is not null && Items.Count == 0;

    public string FrameworkText
    {
        get
        {
            if (_manager is null)
                return "";
            var f = _manager.Frameworks;
            return $"CSharpLoader：{(f.CSharpLoaderInstalled ? L.T("已安装", "installed") : L.T("未安装", "not installed"))}　　UE4SS：{f.UE4SSText}";
        }
    }

    /// <summary>有启用的 pak 类 mod 但启动参数没开 -fileopenlog 时提示。</summary>
    public bool ShowFileOpenLogHint =>
        Items.Any(i => i.IsEnabled && i.Entry.Kind is ModKind.Pak or ModKind.LogicMod or ModKind.Custom)
        && !_main.Settings.LaunchWithFileOpenLog;

    /// <summary>当前都启用、改了相同游戏文件的 mod 对（顶部提醒）。</summary>
    public string ActiveConflictText
    {
        get
        {
            if (_manager?.ActiveConflicts() is not { Count: > 0 } list)
                return "";
            var pairs = list.Select(o => L.T(
                $"「{_manager.DisplayNameOf(o.ModA)}」与「{_manager.DisplayNameOf(o.ModB)}」（{o.SharedAssets.Count} 个相同文件）",
                $"\"{_manager.DisplayNameOf(o.ModA)}\" and \"{_manager.DisplayNameOf(o.ModB)}\" ({o.SharedAssets.Count} shared files)"));
            return L.T("已启用的 mod 之间有资源冲突：", "Enabled mods conflict with each other: ") + string.Join(L.T("；", "; "), pairs)
                   + L.T("。同时启用时只有其中一个的修改生效，可能导致功能异常，建议只保留一个。",
                         ". Only one of them takes effect when both are enabled, which may break features; keep only one.");
        }
    }

    public bool HasActiveConflicts => ActiveConflictText.Length > 0;

    public bool IsGameRunning => _main.IsGameRunning;

    public void OnGameRunningChanged() => OnPropertyChanged(nameof(IsGameRunning));

    /// <summary>界面语言切换：重新生成下拉框选项和所有行的文字。</summary>
    public void OnLanguageChanged()
    {
        OnPropertyChanged(string.Empty);
        foreach (var item in Items)
            item.RefreshComputed();
        // 重设 ItemsSource 后再把选中项推回去
        OnPropertyChanged(nameof(KindFilterIndex));
        OnPropertyChanged(nameof(StateFilterIndex));
    }

    /// <summary>重新扫描（切换游戏目录、点"刷新"时调用）。</summary>
    public void Reload()
    {
        _manager = _main.Paths is { } p ? new ModManager(p) : null;
        try
        {
            _manager?.Refresh();
        }
        catch (Exception ex)
        {
            Dialogs.Error(L.T($"扫描 mod 失败：{ex.Message}", $"Failed to scan mods: {ex.Message}"));
        }
        Sync();
        _main.StatusText = _manager is null ? L.T("未找到游戏目录", "Game folder not found") : L.T($"已扫描：{SummaryText}", $"Scanned: {SummaryText}");
    }

    /// <summary>用管理器的最新扫描结果更新列表，尽量保留现有行对象（保持选中项和滚动位置）。</summary>
    private void Sync()
    {
        string? selectedId = Selected?.Id;
        var entries = _manager?.Mods ?? [];
        var byId = Items.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool structureChanged = false;
        foreach (var entry in entries)
        {
            keep.Add(entry.Id);
            if (byId.TryGetValue(entry.Id, out var vm))
            {
                vm.Update(entry, _manager!);
            }
            else
            {
                Items.Add(new ModItemViewModel(this, entry, _manager!));
                structureChanged = true;
            }
        }
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(Items[i].Id))
            {
                Items.RemoveAt(i);
                structureChanged = true;
            }
        }

        Selected = Items.FirstOrDefault(i => string.Equals(i.Id, selectedId, StringComparison.OrdinalIgnoreCase));
        // 只在筛选结果可能变化时才刷新视图：Refresh 会重建所有行（丢失滚动位置/焦点）
        if (!structureChanged && (StateFilterIndex != 0 || !string.IsNullOrWhiteSpace(SearchText)))
            View.Refresh();
        foreach (var name in new[]
                 {
                     nameof(SummaryText), nameof(IsEmpty), nameof(FrameworkText), nameof(ShowFileOpenLogHint),
                     nameof(ActiveConflictText), nameof(HasActiveConflicts),
                 })
            OnPropertyChanged(name);
    }

    /// <summary>某一行的开关被切换到 <paramref name="target"/>。</summary>
    public void Toggle(ModItemViewModel item, bool target)
    {
        if (_manager is null || _busy || target == item.IsEnabled)
        {
            item.RefreshToggle();
            return;
        }
        if (_main.IsGameRunning)
        {
            Dialogs.Error(L.T("游戏正在运行，不能启用/禁用 mod。请先退出游戏。", "The game is running; mods cannot be enabled or disabled. Please quit the game first."));
            item.RefreshToggle();
            return;
        }
        if (target && _manager.ConflictsWithEnabled(item.Entry) is { Count: > 0 } conflicts && !ConfirmConflicts(item, conflicts))
        {
            item.RefreshToggle();
            return;
        }
        _busy = true;
        try
        {
            _manager.SetEnabled(item.Entry, target);
            _main.StatusText = target ? L.T($"已启用：{item.DisplayName}", $"Enabled: {item.DisplayName}") : L.T($"已禁用：{item.DisplayName}", $"Disabled: {item.DisplayName}");
        }
        catch (Exception ex)
        {
            Dialogs.Error(target
                ? L.T($"启用「{item.DisplayName}」失败：\n{ex.Message}", $"Failed to enable \"{item.DisplayName}\":\n{ex.Message}")
                : L.T($"禁用「{item.DisplayName}」失败：\n{ex.Message}", $"Failed to disable \"{item.DisplayName}\":\n{ex.Message}"));
        }
        finally
        {
            _busy = false;
            Sync();
            item.RefreshToggle();
        }
    }

    /// <summary>启用会和已启用的 mod 冲突时，列出冲突对象请用户确认。</summary>
    private bool ConfirmConflicts(ModItemViewModel item, IReadOnlyList<PakOverlap> conflicts)
    {
        var lines = conflicts.Select(o =>
        {
            string files = string.Join(L.T("、", ", "), o.SharedAssets.Take(3).Select(a => Path.GetFileName(PakConflictDetector.ShortName(a))));
            string more = o.SharedAssets.Count > 3 ? L.T(" 等", " …") : "";
            return L.T($"· {_manager!.DisplayNameOf(o.Other(item.Id))}：{o.SharedAssets.Count} 个相同文件（{files}{more}）",
                       $"· {_manager!.DisplayNameOf(o.Other(item.Id))}: {o.SharedAssets.Count} shared file(s) ({files}{more})");
        });
        return Dialogs.Confirm(L.T(
            $"「{item.DisplayName}」和下面已启用的 mod 修改了相同的游戏文件：\n\n{string.Join("\n", lines)}\n\n" +
            "同时启用时只有其中一个的修改生效（取决于加载顺序），可能导致功能异常。建议先禁用上面的 mod。\n\n仍然启用吗？",
            $"\"{item.DisplayName}\" changes the same game files as these enabled mods:\n\n{string.Join("\n", lines)}\n\n" +
            "When both are enabled only one of them takes effect (depending on load order), which may break features. Consider disabling the mods above first.\n\nEnable anyway?"),
            warning: true);
    }

    private bool Filter(object o)
    {
        if (o is not ModItemViewModel m)
            return false;
        if (m.Entry.IsFrameworkBuiltin && !ShowFrameworkBuiltin)
            return false;
        if (KindFilterIndex > 0 && (int)m.Entry.Kind != KindFilterIndex - 1)
            return false;
        if (StateFilterIndex == StateFilterResourceConflict)
        {
            if (!m.HasResourceConflict)
                return false;
        }
        else if (StateFilterIndex > 0 && (int)m.Entry.State != StateFilterIndex - 1)
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string q = SearchText.Trim();
            return m.DisplayName.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                || m.Id.Contains(q, StringComparison.OrdinalIgnoreCase)
                || m.EditNotes.Contains(q, StringComparison.CurrentCultureIgnoreCase);
        }
        return true;
    }

    private void OpenModsFolder()
    {
        if (_main.Paths is not { } p)
            return;
        string dir = Path.Combine(p.Paks, "~mods");
        if (!Directory.Exists(dir) && Dialogs.Confirm(L.T($"~mods 文件夹还不存在，要创建吗？\n{dir}", $"The ~mods folder does not exist yet. Create it?\n{dir}")))
            Directory.CreateDirectory(dir);
        if (Directory.Exists(dir))
            Dialogs.OpenInExplorer(dir);
    }

    private void OpenDisabledStore()
    {
        if (_main.Paths is not { } p)
            return;
        Directory.CreateDirectory(p.DisabledStore);
        Dialogs.OpenInExplorer(p.DisabledStore);
    }

    private void OpenSelectedLocation()
    {
        if (Selected is null || _main.Paths is not { } p)
            return;
        string baseDir = Selected.Entry.State == ModState.Disabled ? p.DisabledStore : p.B1;
        string first = Path.Combine(baseDir, Selected.Entry.RelativePaths[0]);
        Dialogs.OpenInExplorer(first);
    }

    private void OpenSourceUrl()
    {
        if (Selected?.EditSourceUrl is { Length: > 0 } url)
            Dialogs.OpenUrl(url);
    }

    private void SaveMetadata()
    {
        if (Selected is null || _manager is null)
            return;
        try
        {
            _manager.UpdateMetadata(Selected.Id, Selected.EditName, Selected.EditNotes, Selected.EditSourceUrl);
            Sync();
            _main.StatusText = L.T("备注已保存", "Notes saved");
        }
        catch (Exception ex)
        {
            Dialogs.Error(L.T($"保存备注失败：{ex.Message}", $"Failed to save notes: {ex.Message}"));
        }
    }

    private void NewCustomMod()
    {
        if (_manager is null || _main.Paths is null)
            return;
        var dlg = new CustomModWindow(_main.Paths.B1) { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            var def = _manager.AddCustomMod(dlg.ModName, dlg.SelectedPaths);
            Sync();
            Selected = Items.FirstOrDefault(i => i.Id == def.Id);
            _main.StatusText = L.T($"已创建自定义 mod：{def.Name}", $"Custom mod created: {def.Name}");
        }
        catch (Exception ex)
        {
            Dialogs.Error(L.T($"创建失败：{ex.Message}", $"Failed to create: {ex.Message}"));
        }
    }

    private void RemoveCustomMod()
    {
        if (_manager is null || Selected is not { IsCustom: true } sel)
            return;
        if (!Dialogs.Confirm(L.T(
                $"删除自定义分组「{sel.DisplayName}」？\n只删除分组，不会删除任何文件；若当前是禁用状态，会先把文件移回游戏目录。",
                $"Delete the custom group \"{sel.DisplayName}\"?\nOnly the group is removed; no files are deleted. If it is disabled, its files are moved back into the game folder first.")))
            return;
        try
        {
            _manager.RemoveCustomMod(sel.Id);
            Sync();
        }
        catch (Exception ex)
        {
            Dialogs.Error(L.T($"删除失败：{ex.Message}", $"Failed to delete: {ex.Message}"));
            Sync();
        }
    }
}
