using System.Text.Json;

namespace WukongMM.Core.Game;

/// <summary>
/// 等级表 PlayerLevelDesc 的一行：<see cref="Level"/> = 等级（表 Id），
/// <see cref="NextLevelExp"/> = 从本级升到下一级所需经验，<see cref="NextLevelGainTalent"/> = 从本级升到下一级获得的灵光点。
/// </summary>
public sealed record LevelRow(int Level, long NextLevelExp, int NextLevelGainTalent);

/// <summary>
/// 游戏等级表（由游戏内导出 mod WukongMMDataExport 写到 &lt;b1&gt;\ModManager\GameData\levels.json）。
/// 规则见 docs/SAVE_FORMAT.md §8.2/§8.3：等级必须是表里的 Id；当前经验必须 &lt; 本级 NextLevelExp。
/// </summary>
public sealed class LevelTable
{
    private readonly SortedDictionary<int, LevelRow> _rows = [];

    private LevelTable()
    {
    }

    public int Count => _rows.Count;

    /// <summary>最低等级（表里最小的 Id）。</summary>
    public int MinLevel => _rows.Keys.First();

    /// <summary>最高等级（表里最大的 Id；游戏 GetPlayerLevelMax 取的就是最后一行）。</summary>
    public int MaxLevel => _rows.Keys.Last();

    public string? ExportedAt { get; private set; }

    public IEnumerable<LevelRow> Rows => _rows.Values;

    public bool Contains(int level) => _rows.ContainsKey(level);

    public LevelRow? Row(int level) => _rows.GetValueOrDefault(level);

    /// <summary>该等级下"当前经验"允许的最大值：NextLevelExp − 1（满级等 NextLevelExp &lt; 1 时为 0，与游戏登录时的修正一致）。</summary>
    public long MaxExpAt(int level) => _rows.TryGetValue(level, out var r) ? Math.Max(0, r.NextLevelExp - 1) : 0;

    /// <summary>升到 <paramref name="level"/> 级时累计获得的经验 S(L) = Σ NextLevelExp(l)，l 从最低等级到 L−1。</summary>
    public long CumulativeExp(int level) => _rows.Values.Where(r => r.Level < level).Sum(r => r.NextLevelExp);

    /// <summary>从 <paramref name="from"/> 级升到 <paramref name="to"/> 级游戏会发放的灵光点（to ≤ from 时为 0）。</summary>
    public int TalentBetween(int from, int to) =>
        to <= from ? 0 : _rows.Values.Where(r => r.Level >= from && r.Level < to).Sum(r => r.NextLevelGainTalent);

    /// <summary>读取 levels.json；文件不存在或格式不对时返回 null。</summary>
    public static LevelTable? TryLoad(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>解析导出 mod 的 JSON：{"levels": [{"id", "nextLevelExp", "nextLevelGainTalent"}, …]}。表为空时抛异常。</summary>
    public static LevelTable Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var table = new LevelTable
        {
            ExportedAt = root.TryGetProperty("exportedAt", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null,
        };
        foreach (var e in root.GetProperty("levels").EnumerateArray())
        {
            var row = new LevelRow(
                e.GetProperty("id").GetInt32(),
                e.GetProperty("nextLevelExp").GetInt64(),
                e.TryGetProperty("nextLevelGainTalent", out var g) ? g.GetInt32() : 0);
            if (row.Level >= 1)
                table._rows[row.Level] = row;
        }
        if (table._rows.Count == 0)
            throw new InvalidDataException(L.T("等级表为空", "The level table is empty"));
        return table;
    }
}
