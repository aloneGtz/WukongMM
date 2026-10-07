using WukongMM.Core.Game;

namespace WukongMM.Core.Saves;

public sealed record SaveWriteResult(SaveBackup Backup, int BytesWritten);

/// <summary>把修改后的存档安全写回：检查游戏未运行 → 校验 → 备份 → 临时文件 → 原子替换。</summary>
public static class SaveWriter
{
    /// <param name="verify">对重新解析后的存档做额外校验（例如确认修改值已生效），返回错误信息或 null。</param>
    public static SaveWriteResult Write(SaveFile save, SaveSlot slot, SaveBackupService backups, Func<ArchiveView, string?>? verify = null)
    {
        if (GameProcess.IsRunning())
            throw new GameRunningException();

        // 加载之后游戏又写过存档（或用户还原过备份）→ 拒绝覆盖，避免丢进度
        byte[] current = File.ReadAllBytes(slot.Path);
        if (!current.AsSpan().SequenceEqual(save.OriginalBytes))
            throw new InvalidOperationException(L.T("存档文件在加载后被修改过（可能游戏又保存了）。请重新加载存档后再修改。", "The save file changed after it was loaded (maybe the game saved again). Reload the save and edit again."));

        byte[] bytes = save.ToByteArray();

        // 写入前自检：结果必须能被重新解析、校验值正确（游戏会校验 Md5Str），且修改确实生效
        var reparsed = SaveFile.Parse(bytes);
        if (!reparsed.OriginalChecksumValid)
            throw new InvalidDataException(L.T("自检失败：生成的存档校验值不正确，已取消写入。", "Self-check failed: the new save's checksum is wrong. Nothing was written."));
        var view = new ArchiveView(reparsed);
        if (!view.IsRecognized)
            throw new InvalidDataException(L.T("自检失败：修改后的存档无法识别，已取消写入。", "Self-check failed: the modified save cannot be recognized. Nothing was written."));
        if (verify?.Invoke(view) is { } error)
            throw new InvalidDataException(L.T($"自检失败：{error}，已取消写入。", $"Self-check failed: {error}. Nothing was written."));

        var backup = backups.Create(slot, SaveBackupService.ReasonBeforeEdit);

        // 临时文件放在存档目录之外（同一卷，Move 仍是原子重命名）：游戏的自动备份会把存档目录里
        // 文件名含 ".sav" / "ArchiveSaveFile.N" 的文件一并拷走（docs/SAVE_FORMAT.md §8）。
        string tmpDir = Path.Combine(backups.TempDirectory);
        Directory.CreateDirectory(tmpDir);
        string tmp = Path.Combine(tmpDir, $"write-{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, slot.Path, overwrite: true);
        return new SaveWriteResult(backup, bytes.Length);
    }
}
