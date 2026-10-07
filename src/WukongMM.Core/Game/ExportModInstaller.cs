using System.Diagnostics;
using WukongMM.Core.Mods;

namespace WukongMM.Core.Game;

/// <summary>
/// 管理器自带的游戏内导出 mod（WukongMMDataExport，CSharpLoader mod）的安装 / 更新。
/// 它在游戏读档后导出道具表、等级表、道具用途表到 &lt;b1&gt;\ModManager\GameData，存档页靠这些数据显示道具名称和分类。
/// </summary>
public static class ExportModInstaller
{
    public const string ModName = "WukongMMDataExport";

    public static string RelativeDll => Path.Combine(ModLayout.CSharpLoaderModsDir, ModName, ModName + ".dll");

    /// <summary>已安装的 dll（启用位置优先，其次禁用仓库），没有时为 null。</summary>
    public static string? InstalledDll(GamePaths paths)
    {
        foreach (var baseDir in new[] { paths.B1, paths.DisabledStore })
        {
            string p = Path.Combine(baseDir, RelativeDll);
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    public static bool IsDisabled(GamePaths paths) =>
        InstalledDll(paths) is { } p && p.StartsWith(paths.DisabledStore, StringComparison.OrdinalIgnoreCase);

    /// <summary>dll 的文件版本（如 0.3.0），读不出来时为 null。</summary>
    public static Version? VersionOf(string dllPath)
    {
        try
        {
            var v = FileVersionInfo.GetVersionInfo(dllPath);
            return new Version(v.FileMajorPart, v.FileMinorPart, v.FileBuildPart);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// 安装或更新：写到当前所在位置（在禁用仓库里就更新禁用仓库那份，保持禁用状态），没有时装到启用位置。
    /// 需要 CSharpLoader；游戏运行时拒绝。返回写入的路径。
    /// </summary>
    public static string Install(GamePaths paths, byte[] dll, Func<bool>? isGameRunning = null)
    {
        if ((isGameRunning ?? GameProcess.IsRunning)())
            throw new GameRunningException();
        if (!FrameworkStatus.Detect(paths).CSharpLoaderInstalled)
            throw new InvalidOperationException(L.T("没有检测到 CSharpLoader（Binaries\\Win64\\version.dll + CSharpLoader 文件夹）。请先安装 CSharpLoader，导出 mod 才能运行。",
                                                    "CSharpLoader was not found (Binaries\\Win64\\version.dll + CSharpLoader folder). Install CSharpLoader first; the export mod needs it."));
        string target = InstalledDll(paths) ?? Path.Combine(paths.B1, RelativeDll);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string tmp = target + ".tmp";
        File.WriteAllBytes(tmp, dll);
        File.Move(tmp, target, overwrite: true);
        return target;
    }
}
