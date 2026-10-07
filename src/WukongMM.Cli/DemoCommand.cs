using WukongMM.Core.Game;
using WukongMM.Core.Mods;
using WukongMM.Core.Saves;

namespace WukongMM.Cli;

/// <summary>
/// wmm demo &lt;目录&gt; --save &lt;存档.sav&gt; [--game &lt;真实游戏根&gt;] [--lang zh|en]
/// 生成一个演示用的假游戏目录（截图 / 宣传用）：示例 mod（含两个故意冲突的 pak）、改了角色名的存档副本、游戏导出的数据表。
/// 不含任何真实 mod 文件；pak 只有索引（内容为空）。
/// </summary>
internal static class DemoCommand
{
    private sealed record DemoMod(string Rel, string NameZh, string NameEn, string NotesZh, string NotesEn, bool Disabled, Action<string> Create);

    public static int Run(string[] a)
    {
        if (a.Length < 1)
            throw new ArgumentException("用法：wmm demo <目录> --save <存档.sav> [--game <真实游戏根>] [--lang zh|en]");
        string root = Path.GetFullPath(a[0]);
        string? save = null, real = null;
        bool en = false;
        for (int i = 1; i + 1 < a.Length; i += 2)
        {
            switch (a[i])
            {
                case "--save": save = a[i + 1]; break;
                case "--game": real = a[i + 1]; break;
                case "--lang": en = a[i + 1] == "en"; break;
                default: throw new ArgumentException($"未知选项 {a[i]}");
            }
        }
        if (save is null)
            throw new ArgumentException("必须用 --save 指定一个存档（会复制一份并把角色名改成“天命人”）");
        real ??= GameLocator.Find();
        if (Directory.Exists(Path.Combine(root, "b1", "Content", "Paks")) && Directory.EnumerateFiles(Path.Combine(root, "b1", "Content", "Paks"), "pakchunk*").Any())
            throw new InvalidOperationException($"{root} 看起来是真实游戏目录");
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);

        var p = new GamePaths(root);
        void Touch(string rel, byte[]? bytes = null)
        {
            string full = Path.Combine(p.B1, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes ?? []);
        }
        void Pak(string rel, string dir, params string[] files) => Touch(rel, SelfTest.BuildPak("../../../", [(dir, files)], bmw: true));

        // 游戏本体与框架（空文件，只为让管理器识别）
        Touch(@"Binaries\Win64\b1-Win64-Shipping.exe");
        Touch(@"Binaries\Win64\version.dll");
        Touch(@"Binaries\Win64\CSharpLoader\CSharpManager.bin");
        Touch(@"Binaries\Win64\dwmapi.dll");
        Touch(@"Binaries\Win64\ue4ss\UE4SS.dll");
        Touch(@"Binaries\Win64\ue4ss\Mods\BPModLoaderMod\Scripts\main.lua");

        var mods = new List<DemoMod>
        {
            new(@"Content\Paks\~mods\TigerArmor_P.pak", "虎皮套装换肤（示例）", "Tiger Armor Skin (example)",
                "替换天命人身体和护腿的外观。", "Replaces the Destined One's body and leg armor look.", false,
                rel => Pak(rel, "b1/Content/00MainHZ/Characters/Wukong/Meshes/Equip/HuPi/", "SK_WuKong_Body_HuPi.uasset", "SK_WuKong_Body_HuPi.uexp", "SK_WuKong_Foot_HuPi.uasset", "SK_WuKong_Foot_HuPi.uexp")),
            new(@"Content\Paks\~mods\GoldenArmor_P.pak", "金甲换肤（示例）", "Golden Armor Skin (example)",
                "另一套身体外观，和虎皮套装改的是同一个模型。", "Another body look; replaces the same model as Tiger Armor Skin.", false,
                rel => Pak(rel, "b1/Content/00MainHZ/Characters/Wukong/Meshes/Equip/HuPi/", "SK_WuKong_Body_HuPi.uasset", "SK_WuKong_Body_HuPi.uexp", "T_WuKong_Gold_D.uasset")),
            new(@"Content\Paks\~mods\FastDodge_P.pak", "快速闪避（示例）", "Fast Dodge (example)",
                "翻滚动作更快。需 -fileopenlog。", "Faster roll animations. Needs -fileopenlog.", false,
                rel => Pak(rel, "b1/Content/00Main/Animation/Player/Dodge/", "AM_Dodge_F.uasset", "AM_Dodge_F.uexp", "AM_Dodge_B.uasset", "AM_Dodge_B.uexp")),
            new(@"Content\Paks\~mods\RareDrops_P.pak", "稀有掉落提升（示例）", "Rare Drop Boost (example)",
                "整张替换掉落规则表。", "Replaces the whole drop rule table.", true,
                rel => Pak(rel, "b1/Content/00Main/PBTable/NoneRuntime/", "CommDropRuleDesc.data")),
            new(@"Content\Paks\LogicMods\WorldMap.pak", "大地图（示例）", "World Map (example)",
                "M 键打开地图。需要 UE4SS。", "Press M to open the map. Needs UE4SS.", true,
                rel => Pak(rel, "b1/Content/Mods/WorldMap/", "ModActor.uasset", "ModActor.uexp")),
            new(@"Binaries\Win64\CSharpLoader\Mods\DamageNumbers\DamageNumbers.dll", "伤害数值显示（示例）", "Damage Numbers (example)",
                "C# mod，需要 CSharpLoader。", "A C# mod; needs CSharpLoader.", false, rel => Touch(rel)),
            new(@"Binaries\Win64\ue4ss\Mods\CloudAnywhere\Scripts\main.lua", "随处筋斗云（示例）", "Cloud Anywhere (example)",
                "UE4SS 脚本 mod。", "A UE4SS script mod.", true, rel => Touch(rel)),
        };
        foreach (var m in mods)
            m.Create(m.Rel);
        string exportDll = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "mods", "WukongMMDataExport", "bin", "Release", "net472", "WukongMMDataExport.dll");
        if (File.Exists(exportDll))
            Touch(ExportModInstaller.RelativeDll, File.ReadAllBytes(exportDll));

        // 元数据（显示名 / 备注）后，按需要禁用
        var mm = new ModManager(p, () => false);
        mm.Refresh();
        foreach (var m in mods)
        {
            var entry = mm.Mods.First(e => e.RelativePaths.Any(r => ModLayout.IsSameOrUnder(m.Rel, r)));
            mm.UpdateMetadata(entry.Id, en ? m.NameEn : m.NameZh, en ? m.NotesEn : m.NotesZh, null);
        }
        mm.Refresh();
        foreach (var m in mods.Where(m => m.Disabled))
            mm.SetEnabled(mm.Mods.First(e => e.RelativePaths.Any(r => ModLayout.IsSameOrUnder(m.Rel, r))), false);

        // 存档：复制并改角色名；数据表从真实游戏复制
        string saveDir = Path.Combine(p.SaveGames, "76561190000000001");
        Directory.CreateDirectory(saveDir);
        var sf = SaveFile.Load(save);
        new ArchiveView(sf).SetPlayerName(en ? "Destined One" : "天命人");
        File.WriteAllBytes(Path.Combine(saveDir, Path.GetFileName(save)), sf.ToByteArray());
        if (real is not null && Directory.Exists(new GamePaths(real).ManagerData))
        {
            string src = Path.GetDirectoryName(new GamePaths(real).GameDataItemsFile)!;
            string dst = Path.GetDirectoryName(p.GameDataItemsFile)!;
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src, "*.json"))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
        }
        Console.WriteLine($"演示目录：{root}（{mods.Count} 个示例 mod，存档 {Path.GetFileName(save)}）");
        return 0;
    }
}
