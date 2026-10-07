using WukongMM.Core.Saves;

namespace WukongMM.Cli;

/// <summary>生成一份修改后的存档副本（不改动原文件），用于进游戏实验。</summary>
internal static class SetCommand
{
    public static int Run(string[] a)
    {
        if (a.Length < 1)
            throw new ArgumentException("用法：wmm set <存档.sav> --out <新文件> [--money N] [--level N] [--exp N] [--sparks N] [--item ID=数量]... [--name 角色名]");
        string input = Path.GetFullPath(a[0]);
        string? output = null;
        var save = SaveFile.Load(input);
        var view = new ArchiveView(save);
        if (!view.IsRecognized)
            throw new InvalidDataException("无法识别存档结构");

        for (int i = 1; i < a.Length; i++)
        {
            string opt = a[i];
            string val = i + 1 < a.Length ? a[++i] : throw new ArgumentException($"{opt} 缺少参数");
            switch (opt)
            {
                case "--out":
                    output = Path.GetFullPath(val);
                    break;
                case "--money":
                    Console.WriteLine($"灵蕴 {view.Money} -> {val}");
                    view.SetMoney(long.Parse(val));
                    break;
                case "--level":
                    Console.WriteLine($"等级 {view.Level} -> {val}");
                    view.SetLevel(int.Parse(val));
                    break;
                case "--exp":
                    Console.WriteLine($"当前经验 {view.CurrentExp} -> {val}（累计经验同步调整）");
                    view.SetCurrentExp(long.Parse(val));
                    break;
                case "--sparks":
                    Console.WriteLine($"灵光点 {view.SparkPoints} -> {val}");
                    view.SetSparkPoints(int.Parse(val));
                    break;
                case "--name":
                    Console.WriteLine($"角色名 {view.PlayerName} -> {val}（只用于演示 / 测试副本）");
                    view.SetPlayerName(val);
                    break;
                case "--item":
                    var parts = val.Split('=');
                    int id = int.Parse(parts[0]);
                    long count = long.Parse(parts[1]);
                    var old = view.GetItems().FirstOrDefault(x => x.Id == id);
                    Console.WriteLine($"道具 {id}: {(old is null ? "（新增）" : old.Count.ToString())} -> {count}");
                    view.SetItemCount(id, count);
                    break;
                default:
                    throw new ArgumentException($"未知选项 {opt}");
            }
        }

        if (output is null)
            throw new ArgumentException("必须用 --out 指定输出文件");
        if (string.Equals(output, input, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("--out 不能和原文件相同（命令行工具不直接覆盖存档）");

        byte[] bytes = save.ToByteArray();
        var check = new ArchiveView(SaveFile.Parse(bytes));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllBytes(output, bytes);
        Console.WriteLine($"已写出 {output}（{bytes.Length:N0} 字节，原 {save.OriginalBytes.Length:N0} 字节）");
        Console.WriteLine($"复核：等级 {check.Level} 灵蕴 {check.Money} 经验 {check.CurrentExp}/{check.TotalExp} 灵光点 {check.SparkPoints}");
        return 0;
    }
}
