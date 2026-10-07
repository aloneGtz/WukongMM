using System.Diagnostics;

namespace WukongMM.Core.Game;

/// <summary>检测游戏是否在运行（运行中禁止改存档、切换 mod）。</summary>
public static class GameProcess
{
    /// <summary>游戏主程序与启动器的进程名（不含 .exe）。</summary>
    public static readonly string[] ProcessNames = ["b1-Win64-Shipping", "b1"];

    public static bool IsRunning()
    {
        foreach (var name in ProcessNames)
        {
            var processes = Process.GetProcessesByName(name);
            bool any = processes.Length > 0;
            foreach (var p in processes)
                p.Dispose();
            if (any)
                return true;
        }
        return false;
    }
}

/// <summary>游戏正在运行，拒绝执行写操作。</summary>
public sealed class GameRunningException()
    : InvalidOperationException(L.T("游戏正在运行。请先完全退出《黑神话：悟空》再操作。", "The game is running. Please quit Black Myth: Wukong completely first."));
