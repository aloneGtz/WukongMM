namespace WukongMM.Core.Mods;

public enum ModKind
{
    /// <summary>Content\Paks 下的 pak 资源 mod（~mods 或 Paks 根目录无 .sig 的 pak）。</summary>
    Pak,
    /// <summary>Content\Paks\LogicMods 下的蓝图 mod（需要 UE4SS + BPModLoader）。</summary>
    LogicMod,
    /// <summary>Binaries\Win64\CSharpLoader\Mods\&lt;Name&gt;。</summary>
    CSharpLoader,
    /// <summary>Binaries\Win64\ue4ss\Mods\&lt;Name&gt; 或 Binaries\Win64\Mods\&lt;Name&gt;。</summary>
    UE4SS,
    /// <summary>用户自定义分组（任意文件/文件夹组合）。</summary>
    Custom,
}

public enum ModState
{
    Enabled,
    Disabled,
    /// <summary>启用位置和禁用仓库里都有这个 mod 的文件（例如禁用期间又装了一份）。</summary>
    Conflict,
}

/// <summary>扫描得到的一个 mod。</summary>
public sealed class ModEntry
{
    /// <summary>稳定 ID：类型前缀 + 相对 b1 的根路径（正斜杠），例如 pak:Content/Paks/~mods/Foo_P。</summary>
    public required string Id { get; init; }

    public required ModKind Kind { get; init; }

    /// <summary>默认显示名（文件主干名或文件夹名），用户可在元数据里覆盖。</summary>
    public required string DefaultName { get; init; }

    /// <summary>组成这个 mod 的文件/文件夹，相对 &lt;b1&gt;（反斜杠）。启停时整体移动。</summary>
    public required IReadOnlyList<string> RelativePaths { get; init; }

    public required ModState State { get; init; }

    public long SizeBytes { get; init; }

    public DateTime LastModified { get; init; }

    /// <summary>mod 包含的所有文件（相对 mod 所在位置的 b1 基准），用于详情展示。</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    /// <summary>UE4SS 自带的内置 mod（属于框架的一部分）。</summary>
    public bool IsFrameworkBuiltin { get; init; }

    public bool IsEnabled => State == ModState.Enabled;

    public override string ToString() => $"{Id} [{State}]";
}

public static class ModKindNames
{
    public static string ToDisplay(this ModKind kind) => kind switch
    {
        ModKind.Pak => L.T("Pak 资源", "Pak asset"),
        ModKind.LogicMod => L.T("蓝图 (LogicMods)", "Blueprint (LogicMods)"),
        ModKind.CSharpLoader => "C# (CSharpLoader)",
        ModKind.UE4SS => L.T("UE4SS 脚本", "UE4SS script"),
        ModKind.Custom => L.T("自定义", "Custom"),
        _ => kind.ToString(),
    };

    public static string ToPrefix(this ModKind kind) => kind switch
    {
        ModKind.Pak => "pak",
        ModKind.LogicMod => "logic",
        ModKind.CSharpLoader => "csharp",
        ModKind.UE4SS => "ue4ss",
        ModKind.Custom => "custom",
        _ => kind.ToString().ToLowerInvariant(),
    };
}
