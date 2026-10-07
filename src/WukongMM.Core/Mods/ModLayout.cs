namespace WukongMM.Core.Mods;

/// <summary>各类 mod 的位置（相对 &lt;b1&gt;），见 docs/MOD_LAYOUT.md。</summary>
public static class ModLayout
{
    public static readonly string Paks = Path.Combine("Content", "Paks");
    public static readonly string PakModsDir = Path.Combine(Paks, "~mods");
    public static readonly string LogicModsDir = Path.Combine(Paks, "LogicMods");
    public static readonly string Win64 = Path.Combine("Binaries", "Win64");
    public static readonly string CSharpLoaderDir = Path.Combine(Win64, "CSharpLoader");
    public static readonly string CSharpLoaderModsDir = Path.Combine(CSharpLoaderDir, "Mods");
    public static readonly string UE4SSModsDir = Path.Combine(Win64, "ue4ss", "Mods");
    public static readonly string UE4SSLegacyModsDir = Path.Combine(Win64, "Mods");

    /// <summary>pak mod 的组成文件扩展名。</summary>
    public static readonly string[] PakExtensions = [".pak", ".utoc", ".ucas", ".sig"];

    /// <summary>CSharpLoader 的共享依赖目录，不是 mod。</summary>
    public const string CSharpCommonDir = "Common";

    /// <summary>UE4SS Mods 目录里不是 mod 的子目录。</summary>
    public static readonly string[] UE4SSNonModDirs = ["shared"];

    /// <summary>UE4SS 自带的内置 mod。</summary>
    public static readonly HashSet<string> UE4SSBuiltinMods = new(StringComparer.OrdinalIgnoreCase)
    {
        "ActorDumperMod", "BPML_GenericFunctions", "BPModLoaderMod", "CheatManagerEnablerMod",
        "ConsoleCommandsMod", "ConsoleEnablerMod", "jsbLuaProfilerMod", "Keybinds", "LineTraceMod", "SplitScreenMod",
        // 黑神话版 UE4SS（WukongUE4SS 1.3，Nexus 19）额外自带的两个 C++ mod
        "AllowModsMod", "KismetDebuggerMod",
    };

    /// <summary>
    /// 禁止作为自定义 mod 的路径（移动它们会破坏游戏）：这些目录本身，以及 ModManager / Saved 下的任何东西。
    /// </summary>
    private static readonly string[] ProtectedDirs =
    [
        "", "Binaries", Win64, "Content", Paks, PakModsDir, LogicModsDir, "Plugins", "Saved",
        CSharpLoaderDir, CSharpLoaderModsDir, Path.Combine(Win64, "ue4ss"), UE4SSModsDir, UE4SSLegacyModsDir,
    ];

    private static readonly string[] ProtectedTrees = ["ModManager", "Saved", "Plugins", Path.Combine(Win64, "D3D12")];

    /// <summary>检查一个相对 b1 的路径能否被管理器移动（自定义 mod 用）。返回错误原因或 null。</summary>
    public static string? ValidateMovablePath(string b1Root, string relativePath)
    {
        string rel = Normalize(relativePath);
        if (rel.Length == 0 || Path.IsPathRooted(rel) || rel.Split(Path.DirectorySeparatorChar).Contains(".."))
            return L.T("路径必须位于游戏的 b1 目录内", "The path must be inside the game's b1 folder");
        foreach (var p in ProtectedDirs)
            if (string.Equals(rel, Normalize(p), StringComparison.OrdinalIgnoreCase))
                return L.T("这是游戏或加载器的关键目录，不能整体移动", "This is a key folder of the game or a loader and cannot be moved");
        foreach (var t in ProtectedTrees)
            if (IsSameOrUnder(rel, t))
                return L.T("这个位置的文件不能作为 mod 管理", "Files in this location cannot be managed as a mod");

        string full = Path.Combine(b1Root, rel);
        string name = Path.GetFileName(rel);
        string? parent = Path.GetDirectoryName(rel);
        if (string.Equals(parent, Paks, StringComparison.OrdinalIgnoreCase) && File.Exists(Path.ChangeExtension(full, ".sig")))
            return L.T("这是游戏原版 pak（带 .sig 签名），不能移动", "This is an original game pak (it has a .sig signature) and cannot be moved");
        if (string.Equals(parent, Win64, StringComparison.OrdinalIgnoreCase) && VanillaWin64Files.Contains(name))
            return L.T("这是游戏原版文件，不能移动", "This is an original game file and cannot be moved");
        return null;
    }

    /// <summary>Binaries\Win64 下的原版文件（buildid 21393610）。</summary>
    private static readonly HashSet<string> VanillaWin64Files = new(StringComparer.OrdinalIgnoreCase)
    {
        "b1-Win64-Shipping.exe", "BlackMythWukong.cfg", "amd_fidelityfx_denoiser_dx12.dll", "amd_fidelityfx_dx12.dll",
        "amd_fidelityfx_loader_dx12.dll", "MonoPosixHelper.dll", "OpenImageDenoise.dll", "tbb.dll", "tbb12.dll",
    };

    public static string Normalize(string relativePath) =>
        relativePath.Replace('/', Path.DirectorySeparatorChar).Trim(Path.DirectorySeparatorChar);

    /// <summary>a 等于 b 或位于 b 之下（都是相对路径）。</summary>
    public static bool IsSameOrUnder(string a, string b)
    {
        a = Normalize(a);
        b = Normalize(b);
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
            || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static string ToId(ModKind kind, string relativeRoot) =>
        $"{kind.ToPrefix()}:{Normalize(relativeRoot).Replace(Path.DirectorySeparatorChar, '/')}";
}
