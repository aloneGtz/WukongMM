using System.Media;
using System.Windows;
using System.Windows.Media;
using WukongMM.Core;

namespace WukongMM.App.Views;

public enum MessageKind
{
    Info,
    Warning,
    Error,
    Question,
}

/// <summary>消息 / 确认对话框（替代 MessageBox，按钮随界面语言）。确认框默认按钮是"否"，和原来一样。</summary>
public partial class MessageDialog : Window
{
    private MessageDialog(string title, string message, MessageKind kind, bool confirm)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        (IconText.Text, IconText.Foreground) = kind switch
        {
            MessageKind.Error => ("", new SolidColorBrush(Color.FromRgb(0xE8, 0x4A, 0x4F))),
            MessageKind.Warning => ("", new SolidColorBrush(Color.FromRgb(0xFF, 0xB9, 0x00))),
            MessageKind.Question => ("", new SolidColorBrush(Color.FromRgb(0x4C, 0xA0, 0xE0))),
            _ => ("", new SolidColorBrush(Color.FromRgb(0x4C, 0xA0, 0xE0))),
        };
        YesButton.Content = L.T("是", "Yes");
        NoButton.Content = L.T("否", "No");
        OkButton.Content = L.T("确定", "OK");
        if (confirm)
        {
            OkButton.Visibility = Visibility.Collapsed;
            NoButton.IsDefault = true;
            NoButton.IsCancel = true;
            Loaded += (_, _) => NoButton.Focus();
        }
        else
        {
            YesButton.Visibility = Visibility.Collapsed;
            NoButton.Visibility = Visibility.Collapsed;
            OkButton.IsDefault = true;
            OkButton.IsCancel = true;
            Loaded += (_, _) => OkButton.Focus();
        }
    }

    /// <summary>显示对话框。确认框返回用户是否点了"是"；消息框总是返回 true。</summary>
    public static bool Show(Window? owner, string title, string message, MessageKind kind, bool confirm)
    {
        var dlg = new MessageDialog(title, message, kind, confirm);
        if (owner is not null)
            dlg.Owner = owner;
        else
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        (kind switch
        {
            MessageKind.Error => SystemSounds.Hand,
            MessageKind.Warning => SystemSounds.Exclamation,
            MessageKind.Question => SystemSounds.Question,
            _ => SystemSounds.Asterisk,
        }).Play();
        return dlg.ShowDialog() == true;
    }

    private void Yes_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void No_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
