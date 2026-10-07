using System.IO;
using System.Windows;
using Microsoft.Win32;
using WukongMM.App.Infrastructure;
using WukongMM.Core;

namespace WukongMM.App.Views;

/// <summary>新建自定义 mod：输入名称 + 选择游戏 b1 目录里的文件/文件夹。</summary>
public partial class CustomModWindow : Window
{
    private readonly string _b1;

    public CustomModWindow(string b1Directory)
    {
        _b1 = b1Directory;
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
    }

    public string ModName => NameBox.Text.Trim();

    public IReadOnlyList<string> SelectedPaths => PathList.Items.Cast<string>().ToList();

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = L.T("选择 mod 文件", "Choose mod files"),
            Multiselect = true,
            InitialDirectory = Directory.Exists(Path.Combine(_b1, "Content", "Paks")) ? Path.Combine(_b1, "Content", "Paks") : _b1,
        };
        if (dlg.ShowDialog(this) == true)
            AddPaths(dlg.FileNames);
    }

    private void AddFolders_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = L.T("选择 mod 文件夹", "Choose mod folders"), Multiselect = true, InitialDirectory = _b1 };
        if (dlg.ShowDialog(this) == true)
            AddPaths(dlg.FolderNames);
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            string full = Path.GetFullPath(p);
            if (!full.StartsWith(_b1 + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                Dialogs.Error(L.T($"只能选择游戏 b1 目录里的文件：\n{full}", $"Only files inside the game b1 folder can be chosen:\n{full}"));
                continue;
            }
            if (!PathList.Items.Contains(full))
                PathList.Items.Add(full);
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (PathList.SelectedItem is { } item)
            PathList.Items.Remove(item);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (ModName.Length == 0)
        {
            Dialogs.Error(L.T("请填写名称。", "Please enter a name."));
            return;
        }
        if (PathList.Items.Count == 0)
        {
            Dialogs.Error(L.T("请至少添加一个文件或文件夹。", "Please add at least one file or folder."));
            return;
        }
        DialogResult = true;
    }
}
