using WukongMM.Core.Game;

namespace WukongMM.Core.Mods;

/// <summary>
/// 启用/禁用一个 mod：在 &lt;b1&gt; 与禁用仓库之间整体移动它的文件（保留相对路径）。
/// 任一步失败则把已移动的文件移回去。
/// </summary>
public sealed class ModToggleService(GamePaths paths, Func<bool>? isGameRunning = null)
{
    private readonly Func<bool> _isGameRunning = isGameRunning ?? GameProcess.IsRunning;

    public void SetEnabled(ModEntry mod, bool enabled)
    {
        if (mod.IsEnabled == enabled)
            return;
        if (enabled)
            Move(mod, paths.DisabledStore, paths.B1);
        else
            Move(mod, paths.B1, paths.DisabledStore);
    }

    private void Move(ModEntry mod, string fromBase, string toBase)
    {
        if (_isGameRunning())
            throw new GameRunningException();
        if (mod.State == ModState.Conflict)
            throw new InvalidOperationException(L.T("这个 mod 在游戏目录和禁用仓库里各有一份，请先打开文件夹手动保留其中一份。", "This mod exists both in the game folder and in the disabled store. Open the folders and keep only one copy first."));

        var moves = new List<(string Source, string Target, bool IsDirectory)>();
        foreach (var rel in mod.RelativePaths)
        {
            string src = Path.Combine(fromBase, rel);
            string dst = Path.Combine(toBase, rel);
            bool isDir = Directory.Exists(src);
            if (!isDir && !File.Exists(src))
                continue;
            if (File.Exists(dst) || Directory.Exists(dst))
                throw new IOException(L.T($"目标位置已存在同名文件，已取消：{dst}", $"A file with the same name already exists at the target; cancelled: {dst}"));
            moves.Add((src, dst, isDir));
        }
        if (moves.Count == 0)
            throw new FileNotFoundException(L.T("找不到这个 mod 的文件（可能已被手动删除或移动）。请刷新列表。", "The files of this mod were not found (maybe deleted or moved by hand). Please refresh the list."));

        var done = new List<(string Source, string Target, bool IsDirectory)>();
        try
        {
            foreach (var m in moves)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(m.Target)!);
                if (m.IsDirectory)
                    Directory.Move(m.Source, m.Target);
                else
                    File.Move(m.Source, m.Target);
                done.Add(m);
            }
        }
        catch (Exception ex)
        {
            var rollbackErrors = new List<string>();
            for (int i = done.Count - 1; i >= 0; i--)
            {
                var m = done[i];
                try
                {
                    if (m.IsDirectory)
                        Directory.Move(m.Target, m.Source);
                    else
                        File.Move(m.Target, m.Source);
                }
                catch (Exception rex)
                {
                    rollbackErrors.Add($"{m.Target}: {rex.Message}");
                }
            }
            string msg = ex is IOException or UnauthorizedAccessException
                ? L.T($"移动文件失败（文件可能被占用或没有权限）：{ex.Message}", $"Failed to move files (in use or no permission): {ex.Message}")
                : ex.Message;
            if (rollbackErrors.Count > 0)
                msg += L.T("\n回滚时也出错，以下文件可能需要手动移回：\n", "\nRollback also failed; these files may need to be moved back by hand:\n") + string.Join("\n", rollbackErrors);
            throw new IOException(msg, ex);
        }

        // 只清理禁用仓库里留下的空目录；游戏目录里的目录一律不删
        if (string.Equals(fromBase, paths.DisabledStore, StringComparison.OrdinalIgnoreCase))
            foreach (var m in moves)
                RemoveEmptyParents(Path.GetDirectoryName(m.Source)!, paths.DisabledStore);
    }

    private static void RemoveEmptyParents(string dir, string stopAt)
    {
        string stop = Path.GetFullPath(stopAt).TrimEnd(Path.DirectorySeparatorChar);
        string cur = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
        while (cur.Length > stop.Length && cur.StartsWith(stop, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (Directory.EnumerateFileSystemEntries(cur).Any())
                    return;
                Directory.Delete(cur);
            }
            catch (IOException)
            {
                return;
            }
            cur = Path.GetDirectoryName(cur)!;
        }
    }
}
