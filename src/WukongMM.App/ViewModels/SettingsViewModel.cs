using System.IO;
using System.Reflection;
using Microsoft.Win32;
using WukongMM.App.Infrastructure;
using WukongMM.Core;
using WukongMM.Core.Game;
using WukongMM.Core.Settings;

namespace WukongMM.App.ViewModels;

/// <summary>"设置"页：游戏目录、启动参数、自带导出 mod、数据目录。</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private string _gameRootInput;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        _gameRootInput = main.Paths?.Root ?? "";
        BrowseCommand = new RelayCommand(Browse);
        AutoDetectCommand = new RelayCommand(AutoDetect);
        ApplyCommand = new RelayCommand(Apply);
        OpenAppDataCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(AppSettings.DataDirectory);
            Dialogs.OpenInExplorer(AppSettings.DataDirectory);
        });
        OpenManagerDataCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(_main.Paths!.ManagerData);
            Dialogs.OpenInExplorer(_main.Paths.ManagerData);
        }, () => _main.HasGame);
        InstallExportModCommand = new RelayCommand(InstallExportMod, () => _main.HasGame && BundledExportMod is not null && !_main.IsGameRunning);
        OpenReleasesCommand = new RelayCommand(() => Dialogs.OpenUrl(ReleasesUrl));
        OpenProjectCommand = new RelayCommand(() => Dialogs.OpenUrl(ProjectUrl));
    }

    /// <summary>项目主页；新版本只在 GitHub Releases 发布（用户 2026-10-07 决定）。管理器自己不联网，只用浏览器打开。</summary>
    public const string ProjectUrl = "https://github.com/aloneGtz/WukongMM";

    public const string ReleasesUrl = ProjectUrl + "/releases/latest";

    public RelayCommand OpenReleasesCommand { get; }

    public RelayCommand OpenProjectCommand { get; }

    public RelayCommand InstallExportModCommand { get; }

    // ---------- 自带的导出 mod ----------

    private static byte[]? _bundled;
    private static bool _bundledLoaded;

    /// <summary>打包在 exe 里的 WukongMMDataExport.dll（构建时没有就为 null）。</summary>
    private static byte[]? BundledExportMod
    {
        get
        {
            if (_bundledLoaded)
                return _bundled;
            _bundledLoaded = true;
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("WukongMMDataExport.dll");
            if (s is null)
                return null;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            _bundled = ms.ToArray();
            return _bundled;
        }
    }

    private static Version? BundledVersion
    {
        get
        {
            if (BundledExportMod is not { } bytes)
                return null;
            string tmp = Path.Combine(Path.GetTempPath(), $"wmm-export-{Guid.NewGuid():N}.dll");
            try
            {
                File.WriteAllBytes(tmp, bytes);
                return ExportModInstaller.VersionOf(tmp);
            }
            catch (IOException)
            {
                return null;
            }
            finally
            {
                try { File.Delete(tmp); } catch (IOException) { }
            }
        }
    }

    public string ExportModStatusText
    {
        get
        {
            if (_main.Paths is not { } p)
                return L.T("未找到游戏目录。", "Game folder not found.");
            string bundled = BundledVersion is { } bv
                ? L.T($"管理器自带 v{bv.ToString(3)}。", $"Bundled with the manager: v{bv.ToString(3)}.")
                : L.T("这个版本的管理器没有打包导出 mod。", "This build does not bundle the export mod.");
            string installed = ExportModInstaller.InstalledDll(p) is { } dll
                ? L.T($"已安装 v{ExportModInstaller.VersionOf(dll)?.ToString(3) ?? "?"}", $"Installed: v{ExportModInstaller.VersionOf(dll)?.ToString(3) ?? "?"}")
                  + (ExportModInstaller.IsDisabled(p) ? L.T("（在 Mod 页里被禁用了，启用后才会导出）", " (disabled on the Mods page; enable it so it can export)") : "") + L.T("。", ".")
                : L.T("未安装。", "Not installed.");
            string loader = Core.Mods.FrameworkStatus.Detect(p).CSharpLoaderInstalled ? ""
                : L.T("需要先安装 CSharpLoader（GitHub czastack/B1CSharpLoader）。", "CSharpLoader is required first (GitHub czastack/B1CSharpLoader).");
            return string.Join(L.T("", " "), new[] { installed, bundled, loader }.Where(s => s.Length > 0));
        }
    }

    private void InstallExportMod()
    {
        if (_main.Paths is not { } p || BundledExportMod is not { } dll)
            return;
        try
        {
            string path = ExportModInstaller.Install(p, dll);
            _main.Mods.Reload();
            OnPropertyChanged(nameof(ExportModStatusText));
            Dialogs.Info(L.T($"已安装到：\n{path}\n\n进一次游戏读档，就会导出道具表、等级表和道具用途表；想要英文道具名，把游戏文字语言切成英文再读一次档。",
                             $"Installed to:\n{path}\n\nLoad a save in the game once to export the item, level and item-usage tables. For English item names, switch the game text language to English and load a save once more."));
        }
        catch (Exception ex)
        {
            Dialogs.Error(L.T($"安装失败：{ex.Message}", $"Installation failed: {ex.Message}"));
        }
    }

    public RelayCommand BrowseCommand { get; }
    public RelayCommand AutoDetectCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand OpenAppDataCommand { get; }
    public RelayCommand OpenManagerDataCommand { get; }

    public string GameRootInput
    {
        get => _gameRootInput;
        set => SetProperty(ref _gameRootInput, value);
    }

    public bool LaunchWithFileOpenLog
    {
        get => _main.Settings.LaunchWithFileOpenLog;
        set
        {
            if (_main.Settings.LaunchWithFileOpenLog == value)
                return;
            _main.Settings.LaunchWithFileOpenLog = value;
            _main.OnLaunchOptionChanged();
            OnPropertyChanged();
        }
    }

    public string AppDataDirectory => AppSettings.DataDirectory;

    public void OnLanguageChanged() => OnPropertyChanged(string.Empty);

    /// <summary>游戏目录变化后刷新状态。</summary>
    public void OnGameChanged() => OnPropertyChanged(nameof(ExportModStatusText));

    public string VersionText =>
        L.T("版本", "Version") + $" {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}　·　.NET {Environment.Version}";

    private void Browse()
    {
        var dlg = new OpenFolderDialog { Title = L.T("选择《黑神话：悟空》安装目录（包含 b1 文件夹的那一层）", "Choose the Black Myth: Wukong install folder (the one that contains the b1 folder)") };
        if (Directory.Exists(GameRootInput))
            dlg.InitialDirectory = GameRootInput;
        if (dlg.ShowDialog() == true)
            GameRootInput = dlg.FolderName;
    }

    private void AutoDetect()
    {
        var root = GameLocator.Find();
        if (root is null)
        {
            Dialogs.Error(L.T("没有自动找到游戏，请手动选择安装目录。", "The game was not found automatically. Please choose the install folder."));
            return;
        }
        GameRootInput = root;
        Apply();
    }

    private void Apply()
    {
        string root = GameRootInput.Trim().Trim('"');
        // 用户可能选到了 b1 或 Win64 里面，往上找
        var dir = new DirectoryInfo(root);
        while (dir is not null && !GamePaths.IsValidGameRoot(dir.FullName))
            dir = dir.Parent;
        if (dir is null)
        {
            Dialogs.Error(L.T("这个目录不是《黑神话：悟空》的安装目录（找不到 b1\\Binaries\\Win64\\b1-Win64-Shipping.exe）。", "This is not a Black Myth: Wukong install folder (b1\\Binaries\\Win64\\b1-Win64-Shipping.exe not found)."));
            return;
        }
        GameRootInput = dir.FullName;
        _main.SetGameRoot(dir.FullName);
        _main.StatusText = L.T($"游戏目录：{dir.FullName}", $"Game folder: {dir.FullName}");
    }
}
