using WukongMM.Core.Protobuf;
using WukongMM.Core.Saves;

namespace WukongMM.Cli;

/// <summary>不依赖测试框架的内置单元测试。新增测试：在 Tests 里加一项。</summary>
internal static partial class SelfTest
{
    private static readonly List<(string Name, Action Body)> Tests = [];

    public static int Run()
    {
        Tests.Clear();
        RegisterProtobufTests();
        RegisterSaveTests();
        RegisterModTests();
        RegisterRuleTests();

        int failed = 0;
        foreach (var (name, body) in Tests)
        {
            try
            {
                body();
                Console.WriteLine($"  通过  {name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"  失败  {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        Console.WriteLine($"共 {Tests.Count} 项，失败 {failed} 项");
        return failed == 0 ? 0 : 1;
    }

    private static void Test(string name, Action body) => Tests.Add((name, body));

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    private static void AssertBytes(byte[] actual, byte[] expected, string what) =>
        Assert(actual.AsSpan().SequenceEqual(expected),
            $"{what}：期望 {Convert.ToHexString(expected)}，实际 {Convert.ToHexString(actual)}");

    private static void RegisterProtobufTests()
    {
        Test("varint 边界值编解码", () =>
        {
            foreach (ulong v in new ulong[] { 0, 1, 127, 128, 300, 16383, 16384, uint.MaxValue, ulong.MaxValue })
            {
                var m = new ProtoMessage();
                m.Add(ProtoField.CreateVarint(1, v));
                var back = ProtoMessage.Parse(m.ToByteArray());
                Assert(back.GetVarint(1) == v, $"值 {v} 往返后变成 {back.GetVarint(1)}");
            }
        });

        Test("规范编码与 Google protobuf 一致", () =>
        {
            var m = new ProtoMessage();
            m.Add(ProtoField.CreateVarint(1, 150));
            m.Add(ProtoField.CreateBytes(2, "testing"u8.ToArray()));
            m.Add(ProtoField.CreateFixed32(3, 0x3F800000)); // 1.0f
            AssertBytes(m.ToByteArray(), Convert.FromHexString("089601120774657374696E671D0000803F"), "编码");
        });

        Test("未修改的非规范编码原样保留", () =>
        {
            // field 1 = 1，但 varint 用了非规范的 2 字节编码 81 00
            byte[] raw = Convert.FromHexString("08810010" + "02");
            var m = ProtoMessage.Parse(raw);
            AssertBytes(m.ToByteArray(), raw, "原样输出");
        });

        Test("修改嵌套字段后父级长度前缀自动更新", () =>
        {
            var inner = new ProtoMessage();
            inner.Add(ProtoField.CreateVarint(1, 5));
            var outer = new ProtoMessage();
            outer.Add(ProtoField.CreateMessage(1, inner));
            outer.Add(ProtoField.CreateVarint(2, 7));
            var parsed = ProtoMessage.Parse(outer.ToByteArray());
            parsed.GetMessage(1)!.SetVarint(1, 300); // 1 字节 → 2 字节
            var back = ProtoMessage.Parse(parsed.ToByteArray());
            Assert(back.GetMessage(1)!.GetVarint(1) == 300, "嵌套值未更新");
            Assert(back.GetVarint(2) == 7, "兄弟字段被破坏");
        });

        Test("SetVarint 为 0 时按 proto3 删除字段，缺失时按序插入", () =>
        {
            var m = ProtoMessage.Parse(Convert.FromHexString("0801" + "1803")); // 1=1, 3=3
            m.SetVarint(2, 9);
            Assert(string.Join(",", m.Fields.Select(f => f.Number)) == "1,2,3", "插入位置不对");
            m.SetVarint(2, 0);
            Assert(!m.HasField(2), "值为 0 时字段应被删除");
            AssertBytes(m.ToByteArray(), Convert.FromHexString("08011803"), "删除后编码");
        });

        Test("非法数据解析失败而不是抛出异常", () =>
        {
            Assert(!ProtoMessage.TryParse(Convert.FromHexString("0A05AABB"), out _), "长度越界应失败");
            Assert(!ProtoMessage.TryParse(Convert.FromHexString("00"), out _), "字段号 0 应失败");
            Assert(!ProtoMessage.TryParse(Convert.FromHexString("0B"), out _), "group 应失败");
            Assert(!ProtoMessage.TryParse(Convert.FromHexString("08FFFFFFFFFFFFFFFFFFFF01"), out _), "超长 varint 应失败");
        });
    }

    private static void RegisterSaveTests()
    {
        Test("XOR 加解密可逆且与密钥对齐", () =>
        {
            byte[] data = Enumerable.Range(0, 37).Select(i => (byte)i).ToArray();
            byte[] copy = (byte[])data.Clone();
            SaveCrypto.Xor(copy);
            Assert(copy[0] == (0 ^ 0x7B) && copy[9] == (9 ^ 0x5C) && copy[36] == (36 ^ 0x3E), "密钥位置不对");
            SaveCrypto.Xor(copy);
            AssertBytes(copy, data, "两次异或后");
        });

        Test("构造存档：读写等级/灵蕴/经验/道具", () =>
        {
            var save = SaveFile.Parse(BuildFakeSave());
            var v = new ArchiveView(save);
            Assert(v.IsRecognized, "结构未识别");
            Assert(v.Level == 10 && v.Money == 500 && v.CurrentExp == 30 && v.TotalExp == 1030, "初始值读取错误");

            v.SetLevel(11);
            v.SetMoney(99999);
            v.SetCurrentExp(80);
            v.SetItemCount(3001, 50);
            v.AddItem(2005, 3);   // 插到 2001 和 3001 之间
            v.AddItem(9999, 1);   // 插到最后

            var back = new ArchiveView(SaveFile.Parse(save.ToByteArray()));
            Assert(back.Level == 11, "等级");
            Assert(back.Money == 99999, "灵蕴");
            Assert(back.CurrentExp == 80 && back.TotalExp == 1080, $"经验 {back.CurrentExp}/{back.TotalExp}");
            var ids = string.Join(",", back.GetItems().Select(i => $"{i.Id}x{i.Count}"));
            Assert(ids == "2001x1,2005x3,3001x50,9999x1", $"道具列表 {ids}");
        });

        Test("校验值：构造存档正确、修改后自动重算、篡改可被检出", () =>
        {
            var save = SaveFile.Parse(BuildFakeSave());
            Assert(save.OriginalChecksumValid, "构造的存档校验值应正确");
            var v = new ArchiveView(save);
            v.SetMoney(123456);
            var back = SaveFile.Parse(save.ToByteArray());
            Assert(back.OriginalChecksumValid, "修改后重新生成的校验值应正确");
            Assert(back.StoredChecksum == back.ComputeChecksum(), "存储值与计算值应一致");

            // 只改加密主体的一个字节、不更新校验值 → 应被检出（游戏会报"存档完整性校验失败"）
            byte[] raw = BuildFakeSave();
            raw[^1] ^= 0x01;
            Assert(!SaveFile.Parse(raw).OriginalChecksumValid, "篡改后的存档应校验失败");
        });

        Test("校验值算法：MD5(加密主体 + 盐) 小写十六进制", () =>
        {
            // 与游戏 CalcByteArrayMD5 等价：md5("" + salt)
            string expected = Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(
                System.Text.Encoding.UTF8.GetBytes(SaveChecksum.MagicSalt)));
            Assert(SaveChecksum.Compute([]) == expected, "空主体的校验值不对");
        });

        Test("灵蕴为 0 时可设置（条目缺 field 2）", () =>
        {
            var save = SaveFile.Parse(BuildFakeSave(money: 0));
            var v = new ArchiveView(save);
            Assert(v.Money == 0, "初始应为 0");
            v.SetMoney(123);
            Assert(new ArchiveView(SaveFile.Parse(save.ToByteArray())).Money == 123, "设置后");
        });

        Test("越界值被拒绝", () =>
        {
            var v = new ArchiveView(SaveFile.Parse(BuildFakeSave()));
            bool threw = false;
            try { v.SetMoney(-1); } catch (ArgumentOutOfRangeException) { threw = true; }
            Assert(threw, "负数应被拒绝");
            threw = false;
            try { v.SetMoney((long)int.MaxValue + 1); } catch (ArgumentOutOfRangeException) { threw = true; }
            Assert(threw, "超过 int32 应被拒绝");
        });
    }

    /// <summary>按真实存档的结构造一个最小存档：外层 {1:头, 2:XOR(主体)}。</summary>
    internal static byte[] BuildFakeSave(long money = 500)
    {
        var basic = new ProtoMessage();
        basic.SetVarint(4, 10);
        var will = new ProtoMessage();
        will.SetVarint(1, 1);
        will.SetVarint(2, (ulong)money);
        var bag = new ProtoMessage();
        bag.Add(ProtoField.CreateMessage(1, will));
        foreach (var (id, count) in new[] { (2001, 1), (3001, 7) })
        {
            var e = new ProtoMessage();
            e.SetVarint(1, (ulong)id);
            e.SetVarint(2, (ulong)count);
            bag.Add(ProtoField.CreateMessage(2, e));
        }
        var growth = new ProtoMessage();
        growth.SetVarint(1, 30);
        growth.SetVarint(7, 1030);
        var growthGroup = new ProtoMessage();
        growthGroup.Add(ProtoField.CreateMessage(2, growth));
        var role = new ProtoMessage();
        role.Add(ProtoField.CreateMessage(1, basic));
        role.Add(ProtoField.CreateMessage(2, bag));
        role.Add(ProtoField.CreateMessage(3, growthGroup));
        var roleData = new ProtoMessage();
        roleData.Add(ProtoField.CreateMessage(1, role));
        var body = new ProtoMessage();
        body.Add(ProtoField.CreateMessage(1, roleData));

        byte[] plain = body.ToByteArray();
        SaveCrypto.Xor(plain);
        var header = new ProtoMessage();
        header.Add(ProtoField.CreateBytes(1, System.Text.Encoding.UTF8.GetBytes(SaveChecksum.Compute(plain))));
        var outer = new ProtoMessage();
        outer.Add(ProtoField.CreateMessage(1, header));
        outer.Add(ProtoField.CreateBytes(2, plain));
        return outer.ToByteArray();
    }

    static partial void RegisterModTestsImpl();

    static partial void RegisterPakTestsImpl();

    private static void RegisterModTests()
    {
        RegisterModTestsImpl();
        RegisterPakTestsImpl();
    }

    static partial void RegisterRuleTestsImpl();

    private static void RegisterRuleTests() => RegisterRuleTestsImpl();
}
