using System.Diagnostics;
using System.IO;
using System.Windows;
using WukongMM.App.Views;
using WukongMM.Core;

namespace WukongMM.App.Infrastructure;

/// <summary>统一的消息框（<see cref="MessageDialog"/>，按钮随界面语言）/ 打开文件夹工具。</summary>
public static class Dialogs
{
    private static string Title => L.T("黑神话：悟空 Mod 管理器", "Black Myth: Wukong Mod Manager");

    public static void Info(string message) => MessageDialog.Show(Owner, Title, message, MessageKind.Info, confirm: false);

    public static void Error(string message) => MessageDialog.Show(Owner, Title, message, MessageKind.Error, confirm: false);

    /// <summary>是 / 否确认（默认按钮"否"）。</summary>
    public static bool Confirm(string message, bool warning = false) =>
        MessageDialog.Show(Owner, Title, message, warning ? MessageKind.Warning : MessageKind.Question, confirm: true);

    /// <summary>在资源管理器中打开文件夹；传入文件时选中该文件。</summary>
    public static void OpenInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
                Process.Start("explorer.exe", $"/select,\"{path}\"")?.Dispose();
            else if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
            else
                Error(L.T($"路径不存在：{path}", $"Path not found: {path}"));
        }
        catch (Exception ex)
        {
            Error(L.T($"无法打开：{ex.Message}", $"Cannot open: {ex.Message}"));
        }
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Error(L.T($"无法打开链接：{ex.Message}", $"Cannot open the link: {ex.Message}"));
        }
    }

    /// <summary>当前激活的窗口（例如"新建自定义 mod"对话框），否则主窗口；都还没显示时为 null（屏幕居中）。</summary>
    private static Window? Owner =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsLoaded)
        ?? (Application.Current?.MainWindow is { IsLoaded: true } w ? w : null);
}
