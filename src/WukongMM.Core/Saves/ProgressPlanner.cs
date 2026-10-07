using WukongMM.Core.Game;

namespace WukongMM.Core.Saves;

/// <summary>角色成长数值：等级 / 当前等级内经验 / 累计经验 / 未分配灵光点。</summary>
public readonly record struct ProgressValues(int Level, long CurrentExp, long TotalExp, int SparkPoints);

/// <summary>换算结果：要写入的值、给用户看的说明；<see cref="Error"/> 不为 null 时不能保存。</summary>
public sealed record ProgressPlan(ProgressValues Values, int GrantedSparks, IReadOnlyList<string> Notes, string? Error);

/// <summary>
/// 按游戏规则把用户输入的等级 / 经验 / 灵光点换算成要写入存档的值（docs/SAVE_FORMAT.md §8.2/§8.3）：
/// <list type="bullet">
/// <item>等级必须是等级表里的 Id，否则登录后经验清零且以后获得的经验全部无效；</item>
/// <item>当前经验必须 &lt; 本级 NextLevelExp，否则登录时被压回 NextLevelExp−1（不会自动升级）；</item>
/// <item>累计经验只用于显示/统计：按"升到该等级所需 + 当前经验"的变化量同步，保留存档原有的偏差；</item>
/// <item>改等级不会自动给灵光点；可选按等级表补发升级应得的点数。</item>
/// </list>
/// 只拦"改成的"不合规值：存档里原本就不合规、而用户没动的值不阻止其他修改，只放进 <see cref="ProgressPlan.Notes"/> 提醒；
/// 上限类检查（灵光点、累计经验）只拦"变大并超过上限"，往下改总是允许。
/// 没有等级表时只做基本范围检查，累计经验跟随当前经验的差值变化（旧行为）。
/// </summary>
public static class ProgressPlanner
{
    /// <summary>没有等级表时允许的最高等级（只是防呆，不代表游戏上限）。</summary>
    public const int FallbackMaxLevel = 9999;

    /// <summary>灵光点上限：游戏升级时按 int32 无检查累加，留足余量；与灵光点道具的 CarryMax 一致。</summary>
    public const int MaxSparks = 9999;

    /// <summary>累计经验上限：游戏获得经验时按 int32 无检查累加，留出余量防止溢出。</summary>
    public const long MaxSafeTotalExp = 2_000_000_000;

    public static ProgressPlan Plan(ProgressValues original, int level, long exp, int sparks, LevelTable? table, bool grantSparks)
    {
        var notes = new List<string>();
        bool levelChanged = level != original.Level;
        bool expChanged = exp != original.CurrentExp;
        bool sparksChanged = sparks != original.SparkPoints;

        if (exp < 0 || exp > int.MaxValue)
        {
            if (expChanged)
                return Fail(original, L.T("当前经验必须是 0 到 2,147,483,647 之间的整数。", "Current EXP must be an integer from 0 to 2,147,483,647."));
            if (exp < 0 && levelChanged)
            {
                // 改等级要重写经验，负数写不进存档
                notes.Add(L.T($"存档原有的当前经验 {exp:N0} 是负数，改等级时一起改为 0", $"The save's current EXP {exp:N0} is negative; it will be set to 0 together with the level change"));
                exp = 0;
            }
            else
            {
                notes.Add(L.T($"存档原有的当前经验 {exp:N0} 不在正常范围内（游戏不会修正负数），建议改为 0", $"The save's current EXP {exp:N0} is out of range (the game does not fix negative values); setting it to 0 is recommended"));
            }
        }
        if (sparks < 0)
        {
            if (sparksChanged)
                return Fail(original, L.T("灵光点不能是负数。", "Sparks cannot be negative."));
            notes.Add(L.T($"存档原有的灵光点 {sparks} 是负数，建议改为 0", $"The save's Sparks value {sparks} is negative; setting it to 0 is recommended"));
        }

        long total;
        int granted = 0;
        if (table is not null && table.Contains(level))
        {
            long maxExp = table.MaxExpAt(level);
            if (exp > maxExp)
            {
                if (expChanged)
                    return Fail(original, level == table.MaxLevel
                        ? L.T($"{level} 级是最高等级，当前经验最多 {maxExp:N0}（经验再多也不会升级）。超出的部分游戏登录时会压回去。", $"Level {level} is the max level; current EXP can be at most {maxExp:N0} (more EXP never levels up). The game clamps any excess when loading.")
                        : L.T($"{level} 级的当前经验最多 {maxExp:N0}（升到下一级需要 {maxExp + 1:N0}）。超出的部分游戏登录时会压回去，并不会自动升级。", $"At level {level} current EXP can be at most {maxExp:N0} (the next level needs {maxExp + 1:N0}). The game clamps any excess when loading; it does not level up automatically."));

                notes.Add(L.T($"当前经验 {exp:N0} 超过了 {level} 级的上限，已调整为 {maxExp:N0}", $"Current EXP {exp:N0} exceeds the limit for level {level}; adjusted to {maxExp:N0}"));
                exp = maxExp;
            }

            long newBase = table.CumulativeExp(level) + exp;
            total = table.Contains(original.Level)
                ? original.TotalExp + newBase - (table.CumulativeExp(original.Level) + original.CurrentExp)
                : newBase;
            if (total < 0)
                total = newBase;

            if (levelChanged && grantSparks && level > original.Level && table.Contains(original.Level))
            {
                granted = table.TalentBetween(original.Level, level);
                if (granted > 0)
                    notes.Add(L.T($"按等级表补发升级应得的灵光点 +{granted}", $"Sparks granted for the levels gained (from the level table): +{granted}"));
            }
            if (levelChanged && level < original.Level)
                notes.Add(L.T("降低等级不会收回已经加到天赋上的灵光点", "Lowering the level does not refund Sparks already spent on talents"));
        }
        else
        {
            // 没有等级表，或等级不在等级表里
            if (table is not null || level < 1 || level > FallbackMaxLevel)
            {
                string range = table is null ? L.T($"1 到 {FallbackMaxLevel}", $"1 to {FallbackMaxLevel}") : L.T($"{table.MinLevel} 到 {table.MaxLevel}", $"{table.MinLevel} to {table.MaxLevel}");
                if (levelChanged)
                    return Fail(original, table is null
                        ? L.T($"等级必须在 {range} 之间。", $"The level must be between {range}.")
                        : L.T($"等级必须在 {range} 之间（游戏等级表）。超出范围的等级会让以后获得的经验全部无效。", $"The level must be between {range} (game level table). A level outside the table makes all future EXP gains void."));
                notes.Add(table is null
                    ? L.T($"存档原有的等级 {level} 不在 {range} 之间", $"The save's level {level} is not between {range}")
                    : L.T($"存档原有的等级 {level} 不在游戏等级表 {range} 里：以后获得的经验全部无效，建议把等级改到范围内", $"The save's level {level} is not in the game level table ({range}): all future EXP gains are void; changing the level into range is recommended"));
            }
            total = original.TotalExp + (exp - original.CurrentExp);
            if (total < 0 && total != original.TotalExp)
                return Fail(original, L.T("当前经验改动后累计经验变成了负数。", "Total EXP would become negative after this change."));
            if (table is null && levelChanged)
                notes.Add(L.T("还没有游戏等级表，无法确认新等级是否有效（超过游戏最高等级会让以后获得的经验全部无效），也无法同步累计经验、补发灵光点。装好 WukongMMDataExport（v0.2）后进一次游戏读档，就会自动导出等级表", "There is no game level table yet, so the new level cannot be verified (a level above the game max makes all future EXP void), and total EXP / Sparks cannot be synced. Install WukongMMDataExport and load a save once to export the level table"));

        }

        if (total > MaxSafeTotalExp)
        {
            if (total > original.TotalExp)
                return Fail(original, L.T($"累计经验会超过 {MaxSafeTotalExp:N0}，游戏以后获得经验时可能溢出。", $"Total EXP would exceed {MaxSafeTotalExp:N0}; it may overflow when the game adds EXP later."));
            notes.Add(L.T($"累计经验 {total:N0} 超过建议上限 {MaxSafeTotalExp:N0}（存档原本就超过），游戏以后获得经验时可能溢出", $"Total EXP {total:N0} is above the recommended limit {MaxSafeTotalExp:N0} (already so in the save); it may overflow when the game adds EXP"));
        }
        if (sparks < 0 && granted > 0)
            sparks = 0; // 原有的负灵光点要随补发一起重写，负数写不进存档，按 0 计算
        long finalSparks = (long)sparks + granted;
        if (finalSparks > int.MaxValue)
            return Fail(original, L.T("灵光点超出范围。", "Sparks out of range."));
        if (finalSparks > MaxSparks)
        {
            if (finalSparks > original.SparkPoints)
                return Fail(original, granted > 0
                    ? L.T($"灵光点加上升级补发的 {granted} 点后超过 {MaxSparks}。", $"Sparks would exceed {MaxSparks} after adding the {granted} granted for leveling up.")
                    : L.T($"灵光点最多 {MaxSparks}（游戏以后升级加点时不检查溢出）。", $"Sparks can be at most {MaxSparks} (the game does not check for overflow when adding points later)."));
            notes.Add(L.T($"灵光点 {finalSparks} 超过建议上限 {MaxSparks}（存档原本就超过）", $"Sparks {finalSparks} is above the recommended limit {MaxSparks} (already so in the save)"));
        }
        return new ProgressPlan(new ProgressValues(level, exp, total, (int)finalSparks), granted, notes, null);
    }

    private static ProgressPlan Fail(ProgressValues original, string error) => new(original, 0, [], error);
}
