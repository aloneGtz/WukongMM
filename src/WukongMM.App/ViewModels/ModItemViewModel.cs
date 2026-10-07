using WukongMM.App.Infrastructure;
using WukongMM.Core;
using WukongMM.Core.Mods;

namespace WukongMM.App.ViewModels;

/// <summary>列表里的一行 mod。mod 的名称和备注是用户 / 作者写的，不随界面语言翻译。</summary>
public sealed class ModItemViewModel : ObservableObject
{
    private readonly ModsViewModel _owner;
    private ModEntry _entry;
    private ModManager _manager;
    private string _displayName = "";
    private string? _warning;
    private string _editName = "";
    private string _editNotes = "";
    private string _editSourceUrl = "";

    public ModItemViewModel(ModsViewModel owner, ModEntry entry, ModManager manager)
    {
        _owner = owner;
        _entry = entry;
        _manager = manager;
        Update(entry, manager);
    }

    public ModEntry Entry => _entry;

    public string Id => _entry.Id;

    /// <summary>
    /// 复选框双向绑定（鼠标、键盘、UI 自动化切换都会走到这里）。
    /// 真正的文件移动延后到绑定更新结束后执行，完成后再按真实状态刷新复选框。
    /// </summary>
    public bool IsEnabled
    {
        get => _entry.State == ModState.Enabled;
        set
        {
            if (value == IsEnabled)
                return;
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => _owner.Toggle(this, value));
        }
    }

    public bool IsConflict => _entry.State == ModState.Conflict;

    public bool CanToggle => _entry.State != ModState.Conflict;

    public bool IsCustom => _entry.Kind == ModKind.Custom;

    public string DisplayName
    {
        get => _displayName;
        private set => SetProperty(ref _displayName, value);
    }

    public string KindText => _entry.Kind.ToDisplay() + (_entry.IsFrameworkBuiltin ? L.T("（框架内置）", " (built-in)") : "");

    public string StateText => _entry.State switch
    {
        ModState.Enabled => L.T("已启用", "Enabled"),
        ModState.Disabled => L.T("已禁用", "Disabled"),
        _ => L.T("重复", "Duplicate"),
    };

    public string SizeText => _entry.SizeBytes switch
    {
        >= 1024L * 1024 * 1024 => $"{_entry.SizeBytes / 1024.0 / 1024 / 1024:F2} GB",
        >= 1024L * 1024 => $"{_entry.SizeBytes / 1024.0 / 1024:F1} MB",
        >= 1024 => $"{_entry.SizeBytes / 1024.0:F0} KB",
        _ => $"{_entry.SizeBytes} B",
    };

    public string ModifiedText => _entry.LastModified == default ? "" : _entry.LastModified.ToString("yyyy-MM-dd HH:mm");

    public string? Warning
    {
        get => _warning;
        private set => SetProperty(ref _warning, value);
    }

    public bool HasWarning => !string.IsNullOrEmpty(Warning);

    public IReadOnlyList<string> Files => _entry.Files;

    public string LocationText => string.Join("\n", _entry.RelativePaths);

    // ---- 资源冲突（pak 类 mod 改了相同的游戏文件）----

    private IReadOnlyList<PakOverlap> Overlaps => _manager.PakConflicts.For(Id).ToList();

    /// <summary>和这个 mod 改了相同游戏文件的 mod 中，当前没被禁用的那些（这个 mod 自己也启用时才真正冲突）。</summary>
    private int OtherActiveCount => Overlaps.Count(o => _manager.Find(o.Other(Id)) is { State: not ModState.Disabled });

    public bool HasResourceConflict => Overlaps.Count > 0;

    /// <summary>自己和冲突对象都在启用状态：同时生效中的冲突。</summary>
    public bool HasActiveResourceConflict => _entry.State != ModState.Disabled && OtherActiveCount > 0;

    /// <summary>列表"冲突"列：⚠ = 正在冲突的 mod 数；不带 ⚠ 的数字 = 可能冲突（对方或自己已禁用）。</summary>
    public string ConflictBadge
    {
        get
        {
            int all = Overlaps.Count;
            if (all == 0)
                return "";
            return HasActiveResourceConflict ? $"⚠ {OtherActiveCount}" : all.ToString();
        }
    }

    /// <summary>详情面板里的冲突说明（每个冲突对象一段，列出最多 8 个相同文件）。</summary>
    public string ConflictDetailText
    {
        get
        {
            var overlaps = Overlaps;
            if (overlaps.Count == 0)
                return "";
            var lines = new List<string>();
            foreach (var o in overlaps)
            {
                var other = _manager.Find(o.Other(Id));
                string state = other is null ? "" : other.State == ModState.Disabled ? L.T("已禁用", "disabled") : L.T("已启用", "enabled");
                lines.Add(L.T($"· 「{_manager.DisplayNameOf(o.Other(Id))}」（{state}）：{o.SharedAssets.Count} 个相同文件",
                              $"· \"{_manager.DisplayNameOf(o.Other(Id))}\" ({state}): {o.SharedAssets.Count} shared file(s)"));
                foreach (var a in o.SharedAssets.Take(8))
                    lines.Add("    " + PakConflictDetector.ShortName(a));
                if (o.SharedAssets.Count > 8)
                    lines.Add(L.T($"    ……另有 {o.SharedAssets.Count - 8} 个", $"    … and {o.SharedAssets.Count - 8} more"));
            }
            return string.Join("\n", lines);
        }
    }

    /// <summary>pak 内容概况（读到几个 pak、多少个游戏文件、读不出的原因）。</summary>
    public string PakContentText
    {
        get
        {
            if (!_manager.PakConflicts.Contents.TryGetValue(Id, out var c))
                return "";
            string text = L.T($"{c.PakCount} 个 pak，修改了 {c.Assets.Count} 个游戏文件", $"{c.PakCount} pak(s), {c.Assets.Count} game file(s) replaced");
            return c.Errors.Count == 0 ? text : text + "\n" + string.Join("\n", c.Errors.Select(e => "⚠ " + e));
        }
    }

    public bool HasPakContent => _manager.PakConflicts.Contents.ContainsKey(Id);

    // ---- 详情面板中可编辑的备注 ----

    public string EditName
    {
        get => _editName;
        set => SetProperty(ref _editName, value);
    }

    public string EditNotes
    {
        get => _editNotes;
        set => SetProperty(ref _editNotes, value);
    }

    public string EditSourceUrl
    {
        get => _editSourceUrl;
        set => SetProperty(ref _editSourceUrl, value);
    }

    public void Update(ModEntry entry, ModManager manager)
    {
        _entry = entry;
        _manager = manager;
        DisplayName = manager.DisplayNameOf(entry);
        var meta = manager.Metadata.Get(entry.Id);
        EditName = meta?.DisplayName ?? "";
        EditNotes = meta?.Notes ?? "";
        EditSourceUrl = meta?.SourceUrl ?? "";
        RefreshComputed();
    }

    /// <summary>重新计算依赖状态 / 语言 / 冲突的文字（不动用户正在编辑的备注）。</summary>
    public void RefreshComputed()
    {
        Warning = _entry.State == ModState.Conflict
            ? L.T("游戏目录和禁用仓库里各有一份这个 mod。请打开文件夹，删掉不要的那份后刷新。",
                  "This mod exists both in the game folder and in the disabled store. Open the folders, delete the copy you don't want, then refresh.")
            : _manager.Frameworks.MissingFrameworkFor(_entry.Kind);
        foreach (var name in new[]
                 {
                     nameof(IsEnabled), nameof(IsConflict), nameof(CanToggle), nameof(IsCustom), nameof(KindText),
                     nameof(StateText), nameof(SizeText), nameof(ModifiedText), nameof(HasWarning), nameof(Files),
                     nameof(LocationText), nameof(HasResourceConflict), nameof(HasActiveResourceConflict), nameof(ConflictBadge),
                     nameof(ConflictDetailText), nameof(PakContentText), nameof(HasPakContent),
                 })
            OnPropertyChanged(name);
    }

    /// <summary>让复选框重新读取真实状态（切换失败时用）。</summary>
    public void RefreshToggle() => OnPropertyChanged(nameof(IsEnabled));
}
