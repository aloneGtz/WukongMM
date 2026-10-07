using WukongMM.Core.Protobuf;
using WukongMM.Core.Saves;

namespace WukongMM.Cli;

/// <summary>
/// 无损往返测试，对每个 .sav：
///  1. 不修改直接序列化 == 原文件
///  2. 强制重编码"会被编辑的路径"上的所有节点（tag/长度前缀重新生成）== 原文件 → 证明我们的编码与游戏一致
///  3. 改灵蕴 +1 → 重新解析读到新值；再改回原值 → 序列化 == 原文件 → 证明修改只影响目标字段
///  4. 全树强制重编码（仅统计，字符串被误判为消息时可能不一致，不算失败）
/// </summary>
internal static class RoundTrip
{
    public static int Run(string[] targets)
    {
        if (targets.Length == 0)
            throw new ArgumentException("用法：wmm roundtrip <文件或目录>...");
        var files = targets.SelectMany(t => Directory.Exists(t)
                ? Directory.EnumerateFiles(t, "ArchiveSaveFile.*.sav", SearchOption.AllDirectories)
                : [t])
            .ToList();
        int failed = 0;
        foreach (var file in files)
        {
            var problems = Check(file, out string summary);
            Console.WriteLine($"{(problems.Count == 0 ? "通过" : "失败")}  {file}  {summary}");
            foreach (var p in problems)
                Console.WriteLine($"      - {p}");
            if (problems.Count > 0)
                failed++;
        }
        Console.WriteLine($"共 {files.Count} 个，失败 {failed} 个");
        return failed == 0 ? 0 : 1;
    }

    private static List<string> Check(string file, out string summary)
    {
        var problems = new List<string>();
        byte[] original = File.ReadAllBytes(file);

        // 1. 原样
        var s1 = SaveFile.Parse(original, file);
        if (!s1.ToByteArray().AsSpan().SequenceEqual(original))
            problems.Add("原样序列化与原文件不一致");
        if (!s1.OriginalChecksumValid)
            problems.Add($"原文件校验值不匹配（存 {s1.StoredChecksum}，算 {s1.ComputeChecksum()}）——游戏会拒绝读取");

        // 2. 编辑路径上的节点强制重编码
        var s2 = SaveFile.Parse(original, file);
        var body = s2.Body;
        body.ForceReencode(recursive: false);
        int[][] paths = [[1], [1, 1], [1, 1, 1], [1, 1, 2], [1, 1, 3], [1, 1, 3, 2]];
        foreach (var path in paths)
            body.GetMessagePath(path)?.ForceReencode(recursive: false);
        foreach (var f in body.GetMessagePath(1, 1, 2)?.Fields ?? [])
            if (f.TryGetMessage(out var entry))
                entry.ForceReencode(recursive: false);
        if (!s2.ToByteArray().AsSpan().SequenceEqual(original))
            problems.Add("编辑路径重编码后与原文件不一致（编码方式和游戏不同）");

        // 3. 修改再改回
        var s3 = SaveFile.Parse(original, file);
        var v3 = new ArchiveView(s3);
        if (v3.Money is long money)
        {
            v3.SetMoney(money + 1);
            var modified = SaveFile.Parse(s3.ToByteArray());
            if (new ArchiveView(modified).Money != money + 1)
                problems.Add("修改灵蕴后重新解析读到的值不对");
            if (!modified.OriginalChecksumValid)
                problems.Add("修改后的存档校验值不正确");
            if (modified.StoredChecksum == s1.StoredChecksum)
                problems.Add("修改后校验值没有变化");
            v3.SetMoney(money);
            if (!s3.ToByteArray().AsSpan().SequenceEqual(original))
                problems.Add("灵蕴改回原值后与原文件不一致");
        }
        else
        {
            problems.Add("找不到灵蕴字段");
        }

        // 4. 全树强制重编码（只统计）
        var s4 = SaveFile.Parse(original, file);
        s4.Body.ForceReencode(recursive: true);
        bool fullSame = s4.ToByteArray().AsSpan().SequenceEqual(original);

        var v = new ArchiveView(s1);
        summary = $"等级 {v.Level} 灵蕴 {v.Money} 经验 {v.CurrentExp}/{v.TotalExp} 道具 {v.GetItems().Count} 种 | 全树重编码{(fullSame ? "一致" : "不一致")}";
        return problems;
    }
}
