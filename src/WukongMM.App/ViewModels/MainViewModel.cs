using System.IO;
using System.Windows.Threading;
using WukongMM.App.Infrastructure;
using WukongMM.Core;
using WukongMM.Core.Game;
using WukongMM.Core.Items;
using WukongMM.Core.Settings;

namespace WukongMM.App.ViewModels;

/// <summary>主窗口：持有全局状态（设置、游戏目录、道具名称表、界面语言），并定时检测游戏是否运行。</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _timer;
    private GamePaths? _paths;
    private bool _isGameRunning;
    private string _statusText;
    private int _selectedTab;

    public MainViewModel()
    {
        Settings = AppSettings.Load();
        // 界面语言要在创建各页面之前定下来（各页面构造时就会生成文字）。
        // --lang zh|en 只在本次生效（截图 / UI 测试用），不写设置
        string language = CommandLineValue("--lang") ?? Settings.Language;
        LocalizationSource.Set(string.Equals(language, "en", StringComparison.OrdinalIgnoreCase));
        _statusText = L.T("就绪", "Ready");
        Catalog = ItemCatalog.Load();

        // 测试模式：WukongMM.exe --game <游戏根目录>（例如沙盒假目录）只在本次使用这个目录，
        // 不写设置、不保存道具备注，不影响用户平时的配置
        string? testRoot = CommandLineValue("--game") is { } g ? Path.GetFullPath(g) : null;
        IsTestMode = testRoot is not null;
        string? root = testRoot ?? (GamePaths.IsValidGameRoot(Settings.GameRoot) ? Settings.GameRoot : GameLocator.Find());
        _paths = root is not null && GamePaths.IsValidGameRoot(root) ? new GamePaths(root) : null;
        if (IsTestMode)
            _statusText = L.T($"测试模式：游戏目录 {testRoot}（不保存设置和道具备注）",
                              $"Test mode: game folder {testRoot} (settings and item notes are not saved)");
        if (_paths is not null && !string.Equals(Settings.GameRoot, _paths.Root, StringComparison.OrdinalIgnoreCase))
        {
            Settings.GameRoot = _paths.Root;
            TrySaveSettings();
        }

        _isGameRunning = GameProcess.IsRunning();
        Mods = new ModsViewModel(this);
        Saves = new SaveEditorViewModel(this);
        SettingsPage = new SettingsViewModel(this);
        LaunchGameCommand = new RelayCommand(LaunchGame, () => HasGame && !IsGameRunning);
        ToggleLanguageCommand = new RelayCommand(ToggleLanguage);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => IsGameRunning = GameProcess.IsRunning();
        _timer.Start();

        Mods.Reload();
        Saves.ReloadSlots();
    }

    public AppSettings Settings { get; }

    public ItemCatalog Catalog { get; }

    /// <summary>用 --game 启动的测试模式：设置和道具备注只改内存、不写盘。</summary>
    public bool IsTestMode { get; }

    /// <summary>命令行参数 <paramref name="name"/> 后面的值（没有时为 null）。</summary>
    private static string? CommandLineValue(string name)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public ModsViewModel Mods { get; }

    public SaveEditorViewModel Saves { get; }

    public SettingsViewModel SettingsPage { get; }

    public RelayCommand LaunchGameCommand { get; }

    public RelayCommand ToggleLanguageCommand { get; }

    public GamePaths? Paths => _paths;

    public bool HasGame => _paths is not null;

    public string GameRootText => _paths?.Root ?? L.T("未找到游戏目录，请到“设置”页指定", "Game folder not found. Please set it on the Settings page");

    public int SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    public bool IsGameRunning
    {
        get => _isGameRunning;
        private set
        {
            if (SetProperty(ref _isGameRunning, value))
            {
                OnPropertyChanged(nameof(GameStatusText));
                Mods.OnGameRunningChanged();
                Saves.OnGameRunningChanged();
            }
        }
    }

    public string GameStatusText => IsGameRunning
        ? L.T("● 游戏运行中（禁止修改）", "● Game running (changes locked)")
        : L.T("○ 游戏未运行", "○ Game not running");

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string LaunchButtonText => Settings.LaunchWithFileOpenLog
        ? L.T("启动游戏 (-fileopenlog)", "Launch game (-fileopenlog)")
        : L.T("启动游戏", "Launch game");

    /// <summary>切换游戏目录（设置页调用）。</summary>
    public void SetGameRoot(string? root)
    {
        _paths = root is not null && GamePaths.IsValidGameRoot(root) ? new GamePaths(root) : null;
        Settings.GameRoot = _paths?.Root;
        TrySaveSettings();
        OnPropertyChanged(nameof(Paths));
        OnPropertyChanged(nameof(HasGame));
        OnPropertyChanged(nameof(GameRootText));
        Mods.Reload();
        Saves.ReloadSlots();
        SettingsPage.OnGameChanged();
    }

    public void OnLaunchOptionChanged()
    {
        TrySaveSettings();
        OnPropertyChanged(nameof(LaunchButtonText));
    }

    public void TrySaveSettings()
    {
        if (IsTestMode)
            return;
        try
        {
            Settings.Save();
        }
        catch (Exception ex)
        {
            StatusText = L.T($"保存设置失败：{ex.Message}", $"Failed to save settings: {ex.Message}");
        }
    }

    /// <summary>中文 ⇄ 英文。mod 的名称和备注保持原样（由用户 / 作者填写）。</summary>
    private void ToggleLanguage()
    {
        LocalizationSource.Set(!L.English);
        Settings.Language = L.Code;
        TrySaveSettings();
        OnPropertyChanged(string.Empty); // 刷新本页所有算出来的文字
        Mods.OnLanguageChanged();
        Saves.OnLanguageChanged();
        SettingsPage.OnLanguageChanged();
        StatusText = L.T("界面已切换为中文", "Interface switched to English");
    }

    private void LaunchGame()
    {
        if (_paths is null)
            return;
        try
        {
            GameLauncher.Launch(_paths, Settings.LaunchWithFileOpenLog);
            StatusText = Settings.LaunchWithFileOpenLog
                ? L.T("已启动游戏（带 -fileopenlog 参数）", "Game launched (with -fileopenlog)")
                : L.T("已启动游戏", "Game launched");
        }
        catch (Exception ex)
        {
            Dialogs.Error(ex.Message);
        }
    }
}
