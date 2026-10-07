using System.Buffers.Binary;
using System.Text;
using WukongMM.Core.Game;
using WukongMM.Core.Mods;

namespace WukongMM.Cli;

/// <summary>Mod 管理测试：在临时目录里搭一个假的游戏目录，不碰真实游戏。</summary>
internal static partial class SelfTest
{
    static partial void RegisterModTestsImpl()
    {
        Test("扫描：识别各类 mod，忽略原版 pak 与 Common/shared", () => WithSandbox(paths =>
        {
            var mm = new ModManager(paths, () => false);
            mm.Refresh();
            string ids = string.Join(" | ", mm.Mods.Select(m => m.Id));
            string[] expected =
            [
                "pak:Content/Paks/~mods/Foo_P",
                "pak:Content/Paks/~mods/BarFolder",
                "pak:Content/Paks/LooseMod_P",
                "logic:Content/Paks/LogicMods/BpMod",
                "csharp:Binaries/Win64/CSharpLoader/Mods/CoolMod",
                "ue4ss:Binaries/Win64/ue4ss/Mods/LuaMod",
                "ue4ss:Binaries/Win64/ue4ss/Mods/BPModLoaderMod",
            ];
            foreach (var id in expected)
                Assert(mm.Find(id) is { State: ModState.Enabled }, $"缺少 {id}；实际：{ids}");
            Assert(mm.Mods.Count == expected.Length, $"数量不对：{ids}");
            Assert(mm.Find("pak:Content/Paks/~mods/Foo_P")!.RelativePaths.Count == 3, "Foo_P 应包含 pak/utoc/ucas 三个文件");
            Assert(mm.Find("ue4ss:Binaries/Win64/ue4ss/Mods/BPModLoaderMod")!.IsFrameworkBuiltin, "BPModLoaderMod 应标记为内置");
            Assert(mm.Frameworks.CSharpLoaderInstalled && mm.Frameworks.UE4SSInstalled, "框架检测");
        }));

        Test("禁用/启用：文件整体移动，原版不受影响", () => WithSandbox(paths =>
        {
            var mm = new ModManager(paths, () => false);
            mm.Refresh();
            var foo = mm.Find("pak:Content/Paks/~mods/Foo_P")!;
            mm.SetEnabled(foo, false);
            Assert(!File.Exists(Path.Combine(paths.Paks, "~mods", "Foo_P.pak")), "禁用后 pak 应移走");
            Assert(File.Exists(Path.Combine(paths.DisabledStore, "Content", "Paks", "~mods", "Foo_P.utoc")), "禁用仓库里应有 utoc");
            Assert(mm.Find(foo.Id)!.State == ModState.Disabled, "状态应为禁用");

            var cool = mm.Find("csharp:Binaries/Win64/CSharpLoader/Mods/CoolMod")!;
            mm.SetEnabled(cool, false);
            Assert(Directory.Exists(Path.Combine(paths.DisabledStore, "Binaries", "Win64", "CSharpLoader", "Mods", "CoolMod")), "C# mod 文件夹应移入仓库");

            mm.SetEnabled(mm.Find(foo.Id)!, true);
            mm.SetEnabled(mm.Find(cool.Id)!, true);
            Assert(File.Exists(Path.Combine(paths.Paks, "~mods", "Foo_P.ucas")), "启用后应移回");
            Assert(!Directory.Exists(Path.Combine(paths.DisabledStore, "Content")), "禁用仓库的空目录应被清理");
            Assert(File.Exists(Path.Combine(paths.Paks, "pakchunk0-Windows.pak")), "原版 pak 不能动");
            Assert(mm.Mods.All(m => m.State == ModState.Enabled), "全部恢复启用");
        }));

        Test("游戏运行时拒绝切换", () => WithSandbox(paths =>
        {
            var mm = new ModManager(paths, () => true);
            mm.Refresh();
            bool threw = false;
            try { mm.SetEnabled(mm.Mods[0], false); } catch (GameRunningException) { threw = true; }
            Assert(threw, "应抛出 GameRunningException");
            Assert(mm.Mods.All(m => m.State == ModState.Enabled), "状态不应改变");
        }));

        Test("冲突：禁用期间又装了一份同名 mod", () => WithSandbox(paths =>
        {
            var mm = new ModManager(paths, () => false);
            mm.Refresh();
            var foo = mm.Find("pak:Content/Paks/~mods/Foo_P")!;
            mm.SetEnabled(foo, false);
            File.WriteAllText(Path.Combine(paths.Paks, "~mods", "Foo_P.pak"), "new copy");
            mm.Refresh();
            Assert(mm.Find(foo.Id)!.State == ModState.Conflict, "应为冲突状态");
            bool threw = false;
            try { mm.SetEnabled(mm.Find(foo.Id)!, true); } catch (InvalidOperationException) { threw = true; }
            Assert(threw, "冲突时应拒绝切换");
        }));

        Test("移动失败时回滚", () => WithSandbox(paths =>
        {
            var mm = new ModManager(paths, () => false);
            mm.Refresh();
            var foo = mm.Find("pak:Content/Paks/~mods/Foo_P")!;
            string locked = Path.Combine(paths.Paks, "~mods", "Foo_P.utoc");
            bool threw = false;
            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None)) // 模拟文件被游戏占用
            {
                try { mm.SetEnabled(foo, false); } catch (IOException) { threw = true; }
            }
            Assert(threw, "应报告移动失败");
            Assert(File.Exists(Path.Combine(paths.Paks, "~mods", "Foo_P.pak")), "已移动的文件应被移回");
            Assert(mm.Find(foo.Id)!.State == ModState.Enabled, "状态应保持启用");
        }));

        Test("自定义 mod：分组启停、不允许原版文件、删除分组", () => WithSandbox(paths =>
        {
            var mm = new ModManager(paths, () => false);
            mm.Refresh();
            bool threw = false;
            try { mm.AddCustomMod("坏的", [Path.Combine(paths.Paks, "pakchunk0-Windows.pak")]); } catch (ArgumentException) { threw = true; }
            Assert(threw, "原版 pak 不能加入自定义 mod");

            // 选中 Foo_P.pak 一个文件 → 整组纳入；再加 C# mod 文件夹
            var def = mm.AddCustomMod("我的组合", [
                Path.Combine(paths.Paks, "~mods", "Foo_P.pak"),
                Path.Combine(paths.B1, "Binaries", "Win64", "CSharpLoader", "Mods", "CoolMod"),
            ]);
            Assert(def.RelativePaths.Count == 4, $"应包含 3 个 pak 文件 + 1 个文件夹，实际 {def.RelativePaths.Count}");
            Assert(mm.Find("pak:Content/Paks/~mods/Foo_P") is null, "被自定义 mod 覆盖的自动条目应隐藏");
            var custom = mm.Find(def.Id)!;
            mm.SetEnabled(custom, false);
            Assert(!File.Exists(Path.Combine(paths.Paks, "~mods", "Foo_P.ucas")), "自定义 mod 禁用后文件应移走");
            Assert(mm.Find(def.Id)!.State == ModState.Disabled, "自定义 mod 状态");

            mm.RemoveCustomMod(def.Id);
            Assert(File.Exists(Path.Combine(paths.Paks, "~mods", "Foo_P.ucas")), "删除分组时应先把文件移回");
            Assert(mm.Find("pak:Content/Paks/~mods/Foo_P") is { State: ModState.Enabled }, "删除分组后自动条目重新出现");
        }));

        Test("元数据：显示名与备注持久化", () => WithSandbox(paths =>
        {
            var mm = new ModManager(paths, () => false);
            mm.Refresh();
            var foo = mm.Find("pak:Content/Paks/~mods/Foo_P")!;
            mm.UpdateMetadata(foo.Id, "超级大圣皮肤", "从 Nexus 下载", "https://example.com/mod");
            var mm2 = new ModManager(paths, () => false);
            mm2.Refresh();
            Assert(mm2.DisplayNameOf(mm2.Find(foo.Id)!) == "超级大圣皮肤", "显示名未保存");
            Assert(mm2.Metadata.Get(foo.Id)!.Notes == "从 Nexus 下载", "备注未保存");
        }));

        Test("元数据：旧窗口保存备注不会覆盖别处新建的自定义分组和备注", () => WithSandbox(paths =>
        {
            var stale = new ModManager(paths, () => false); // 相当于一直开着的管理器窗口
            stale.Refresh();
            var other = new ModManager(paths, () => false); // 另一个进程（安装工具 / 命令行）
            other.Refresh();
            var def = other.AddCustomMod("另一进程建的组", [Path.Combine(paths.B1, "Binaries", "Win64", "CSharpLoader", "Mods", "CoolMod")]);
            other.UpdateMetadata("pak:Content/Paks/~mods/Foo_P", "另一进程的名字", null, null);

            stale.UpdateMetadata("logic:Content/Paks/LogicMods/BpMod", "旧窗口改的名字", null, null);
            var check = new ModManager(paths, () => false);
            check.Refresh();
            Assert(check.Metadata.Data.CustomMods.Any(c => c.Id == def.Id), "自定义分组被旧窗口覆盖丢失");
            Assert(check.DisplayNameOf(check.Find("pak:Content/Paks/~mods/Foo_P")!) == "另一进程的名字", "别处改的显示名被覆盖");
            Assert(check.DisplayNameOf(check.Find("logic:Content/Paks/LogicMods/BpMod")!) == "旧窗口改的名字", "旧窗口自己的修改没保存");
        }));
    }

    static partial void RegisterPakTestsImpl()
    {
        Test("pak 索引：标准尾部 / 黑神话尾部、中文文件名、加密与损坏", () =>
        {
            var dirs = new (string, string[])[]
            {
                ("/", ["readme.txt"]),
                ("b1/Content/00Main/PBTable/", ["FUStTalent.data", "天命.data"]),
            };
            foreach (bool bmw in new[] { false, true })
            {
                var list = PakIndexReader.Read(new MemoryStream(BuildPak("../../../", dirs, bmw)));
                Assert(list.Ok, $"{(bmw ? "黑神话" : "标准")}尾部读取失败：{list.Error}");
                string got = string.Join(" | ", list.Files);
                Assert(got == "readme.txt | b1/Content/00Main/PBTable/FUStTalent.data | b1/Content/00Main/PBTable/天命.data", $"文件清单不对：{got}");
            }
            var mounted = PakIndexReader.Read(new MemoryStream(BuildPak("../../../b1/Content/", [("Mod/", ["A.uasset"])], true)));
            Assert(mounted.Ok && mounted.Files.Single() == "b1/Content/Mod/A.uasset", $"挂载点拼接不对：{string.Join(",", mounted.Files)}");

            var enc = PakIndexReader.Read(new MemoryStream(BuildPak("../../../", dirs, true, encrypted: true)));
            Assert(!enc.Ok && enc.Error!.Contains("加密"), $"加密索引应报错：{enc.Error}");
            var good = BuildPak("../../../", dirs, true);
            var truncated = PakIndexReader.Read(new MemoryStream(good[..^215])); // 下载不完整：文件尾丢了
            Assert(!truncated.Ok, "截断的 pak 应报错");
            var junk = PakIndexReader.Read(new MemoryStream(new byte[4096]));
            Assert(!junk.Ok && junk.Error!.Contains("找不到"), $"非 pak 文件应报错：{junk.Error}");
            // 目录索引里的个数被改坏 → 报格式错误，不抛异常
            var broken = (byte[])good.Clone();
            int at = FindBytes(broken, Encoding.Latin1.GetBytes("b1/Content/00Main/PBTable/\0")) + 27;
            BinaryPrimitives.WriteInt32LittleEndian(broken.AsSpan(at), 0x7FFFFFF0);
            var bad = PakIndexReader.Read(new MemoryStream(broken));
            Assert(!bad.Ok && bad.Error!.Contains("格式"), $"损坏的目录索引应报格式错误：{bad.Error}");
        });

        Test("资源冲突：同一资源的 uasset/uexp 合并比较，区分启用状态", () => WithSandbox(paths =>
        {
            void WritePak(string rel, params string[] files)
            {
                string full = Path.Combine(paths.B1, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllBytes(full, BuildPak("../../../", [("b1/Content/Chars/", files)], bmw: true));
            }
            WritePak(@"Content\Paks\~mods\SkinA_P.pak", "Body.uasset", "Body.uexp", "Head.uasset", "AssetRegistry.bin");
            WritePak(@"Content\Paks\~mods\SkinB_P.pak", "Body.uasset", "Body.uexp", "Body.ubulk", "AssetRegistry.bin");
            WritePak(@"Content\Paks\~mods\Other\other_P.pak", "Tail.uasset", "AssetRegistry.bin");
            var mm = new ModManager(paths, () => false);
            mm.Refresh();
            var r = mm.PakConflicts;
            Assert(r.Contents["pak:Content/Paks/~mods/SkinA_P"].Assets.Count == 2, "SkinA 应有 2 个资源（Body、Head；AssetRegistry.bin 忽略）");
            var o = r.Overlaps.SingleOrDefault() ?? throw new Exception($"应只有 1 对冲突，实际 {r.Overlaps.Count}");
            Assert(o.Involves("pak:Content/Paks/~mods/SkinA_P") && o.Involves("pak:Content/Paks/~mods/SkinB_P"), "冲突双方不对");
            Assert(o.SharedAssets.SequenceEqual(["b1/Content/Chars/Body"]), $"相同资源不对：{string.Join(",", o.SharedAssets)}");
            Assert(r.Contents["pak:Content/Paks/~mods/Foo_P"].Errors.Count == 1, "假 pak 文件应记一条读取错误");
            Assert(mm.ActiveConflicts().Count == 1, "两个都启用时应算作生效中的冲突");

            var skinB = mm.Find("pak:Content/Paks/~mods/SkinB_P")!;
            mm.SetEnabled(skinB, false);
            Assert(mm.PakConflicts.Overlaps.Count == 1, "禁用后仍应列出冲突（禁用仓库里的 pak 也读）");
            Assert(mm.ActiveConflicts().Count == 0, "一方禁用后不再是生效中的冲突");
            var conflicts = mm.ConflictsWithEnabled(mm.Find(skinB.Id)!);
            Assert(conflicts.Count == 1 && conflicts[0].Other(skinB.Id) == "pak:Content/Paks/~mods/SkinA_P", "启用 SkinB 前应提示与 SkinA 冲突");
            Assert(mm.ConflictsWithEnabled(mm.Find("pak:Content/Paks/~mods/SkinA_P")!).Count == 0, "SkinA 的冲突对象已禁用，不应提示");
        }));
    }

    /// <summary>按 UE5 pak v11 格式生成一个只有索引的 pak（文件内容为空）。</summary>
    internal static byte[] BuildPak(string mount, (string Dir, string[] Files)[] dirs, bool bmw, bool encrypted = false)
    {
        static void FString(BinaryWriter w, string s)
        {
            if (s.All(c => c < 128))
            {
                w.Write(s.Length + 1);
                w.Write(Encoding.ASCII.GetBytes(s + "\0"));
            }
            else
            {
                w.Write(-(s.Length + 1));
                w.Write(Encoding.Unicode.GetBytes(s + "\0"));
            }
        }
        static byte[] Build(Action<BinaryWriter> body)
        {
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
                body(w);
            return ms.ToArray();
        }

        int total = dirs.Sum(d => d.Files.Length);
        var dirIndex = Build(w =>
        {
            w.Write(dirs.Length);
            int n = 0;
            foreach (var (dir, files) in dirs)
            {
                FString(w, dir);
                w.Write(files.Length);
                foreach (var f in files)
                {
                    FString(w, f);
                    w.Write(n++ * 12);
                }
            }
        });
        var hashIndex = new byte[8];
        var data = new byte[16];
        byte[] Primary(long hashOff, long dirOff) => Build(w =>
        {
            FString(w, mount);
            w.Write(total);
            w.Write(0UL);
            w.Write(1);
            w.Write(hashOff);
            w.Write((long)hashIndex.Length);
            w.Write(new byte[20]);
            w.Write(1);
            w.Write(dirOff);
            w.Write((long)dirIndex.Length);
            w.Write(new byte[20]);
            w.Write(0); // EncodedPakEntries
            w.Write(0); // 非编码条目数
        });
        int primaryLen = Primary(0, 0).Length;
        long hashOffset = data.Length + primaryLen;
        long dirOffset = hashOffset + hashIndex.Length;
        var primary = Primary(hashOffset, dirOffset);
        return Build(w =>
        {
            w.Write(data);
            w.Write(primary);
            w.Write(hashIndex);
            w.Write(dirIndex);
            w.Write(new byte[16]); // 加密密钥 GUID
            w.Write((byte)(encrypted ? 1 : 0));
            w.Write(0x5A6F12E1u);
            w.Write(11);
            if (bmw)
                w.Write((ushort)0x10);
            w.Write((long)data.Length);
            w.Write((long)primary.Length);
            w.Write(new byte[20]);
            w.Write(new byte[5 * 32]);
        });
    }

    private static int FindBytes(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle);

    /// <summary>在临时目录搭一个假游戏目录，执行测试后删除。</summary>
    private static void WithSandbox(Action<GamePaths> body)
    {
        string root = Path.Combine(Path.GetTempPath(), "wmm-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var p = new GamePaths(root);
            void Touch(string rel, string content = "x")
            {
                string full = Path.Combine(p.B1, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, content);
            }
            Touch(@"Binaries\Win64\b1-Win64-Shipping.exe");
            Touch(@"Content\Paks\pakchunk0-Windows.pak");
            Touch(@"Content\Paks\pakchunk0-Windows.sig");
            Touch(@"Content\Paks\filehash.dat");
            Touch(@"Content\Paks\LooseMod_P.pak");
            Touch(@"Content\Paks\~mods\Foo_P.pak");
            Touch(@"Content\Paks\~mods\Foo_P.utoc");
            Touch(@"Content\Paks\~mods\Foo_P.ucas");
            Touch(@"Content\Paks\~mods\readme.txt");
            Touch(@"Content\Paks\~mods\BarFolder\bar_P.pak");
            Touch(@"Content\Paks\~mods\EmptyFolder\notes.txt");
            Touch(@"Content\Paks\LogicMods\BpMod.pak");
            Touch(@"Binaries\Win64\version.dll");
            Touch(@"Binaries\Win64\CSharpLoader\CSharpManager.bin");
            Touch(@"Binaries\Win64\CSharpLoader\Mods\CoolMod\CoolMod.dll");
            Touch(@"Binaries\Win64\CSharpLoader\Mods\Common\Shared.dll");
            Touch(@"Binaries\Win64\dwmapi.dll");
            Touch(@"Binaries\Win64\ue4ss\UE4SS.dll");
            Touch(@"Binaries\Win64\ue4ss\Mods\mods.txt");
            Touch(@"Binaries\Win64\ue4ss\Mods\LuaMod\Scripts\main.lua");
            Touch(@"Binaries\Win64\ue4ss\Mods\BPModLoaderMod\Scripts\main.lua");
            Touch(@"Binaries\Win64\ue4ss\Mods\shared\lib.lua");
            body(p);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
