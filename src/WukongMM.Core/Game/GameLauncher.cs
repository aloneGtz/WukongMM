using System.Diagnostics;

namespace WukongMM.Core.Game;

/// <summary>启动游戏。pak mod 需要 -fileopenlog 参数才会被加载（docs/MOD_LAYOUT.md §1.1）。</summary>
public static class GameLauncher
{
    public const string FileOpenLogArgument = "-fileopenlog";

    public static void Launch(GamePaths paths, bool fileOpenLog)
    {
        if (!File.Exists(paths.GameExe))
            throw new FileNotFoundException(L.T($"找不到游戏主程序：{paths.GameExe}", $"Game executable not found: {paths.GameExe}"));
        if (GameProcess.IsRunning())
            throw new InvalidOperationException(L.T("游戏已经在运行了。", "The game is already running."));
        Process.Start(new ProcessStartInfo(paths.GameExe)
        {
            Arguments = fileOpenLog ? FileOpenLogArgument : "",
            WorkingDirectory = paths.Win64,
            UseShellExecute = true,
        })?.Dispose();
    }
}
