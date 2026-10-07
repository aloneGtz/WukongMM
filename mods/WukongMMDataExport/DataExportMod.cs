using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using b1;
using b1.Localization;
using b1.Protobuf.GSProtobufRuntimeAPI;
using CSharpModBase;
using HarmonyLib;
using ResB1;

namespace WukongMMDataExport
{
    /// <summary>
    /// 只读 mod：不修改任何游戏数据。角色初始化完成后（此时数据表与本地化都已加载，且在游戏线程上）
    /// 导出一次道具表到 &lt;b1&gt;\ModManager\GameData\items.json。
    /// </summary>
    public class DataExportMod : ICSharpMod
    {
        internal const string ModName = "WukongMMDataExport";
        private readonly Harmony _harmony = new Harmony(ModName);

        public string Name => ModName;

        public string Version => "0.3.0";

        public void Init()
        {
            Log("Init");
            _harmony.PatchAll(typeof(DataExportMod).Assembly);
        }

        public void DeInit()
        {
            _harmony.UnpatchAll(ModName);
        }

        internal static void Log(string msg) => Console.WriteLine("[" + ModName + "] " + msg);
    }

    [HarmonyPatch(typeof(BGUPlayerCharacterCS), nameof(BGUPlayerCharacterCS.AfterInitAllComp))]
    internal static class AfterPlayerInitPatch
    {
        private static void Postfix()
        {
            try
            {
                Exporter.ExportOnce();
            }
            catch (Exception e)
            {
                DataExportMod.Log("export failed: " + e);
            }
        }
    }

    internal static class Exporter
    {
        private static bool _done;

        public static void ExportOnce()
        {
            if (_done)
                return;
            _done = true;

            string win64 = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            string outDir = Path.GetFullPath(Path.Combine(win64, "..", "..", "ModManager", "GameData"));
            Directory.CreateDirectory(outDir);

            // 三部分互不影响：一个失败不连累其他
            Try("levels", () => ExportLevels(outDir));
            Try("items", () => ExportItems(outDir));
            Try("usage", () => ExportUsage(outDir));
        }

        private static void Try(string what, Action body)
        {
            try
            {
                body();
            }
            catch (Exception e)
            {
                DataExportMod.Log("export " + what + " failed: " + e);
            }
        }

        /// <summary>
        /// items.&lt;语言&gt;.json 总是写（管理器按界面语言取道具名）；
        /// items.json（管理器的主表）只在中文环境或还没有时写，免得切到英文玩一次就把中文名覆盖掉。
        /// </summary>
        private static void ExportItems(string outDir)
        {
            string culture = SafeCulture();
            var table = GSProtobufRuntimeAPI<TBItemDesc, ItemDesc>.Get().GetAll();
            var consumes = GSProtobufRuntimeAPI<TBConsumeDesc, ConsumeDesc>.Get();
            var sb = new StringBuilder(1 << 20);
            sb.Append("{\n  \"schema\": 2,\n");
            sb.Append("  \"exportedAt\": ").Append(Str(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(",\n");
            sb.Append("  \"culture\": ").Append(Str(culture)).Append(",\n");
            sb.Append("  \"items\": [\n");
            int n = 0;
            foreach (var d in table.List)
            {
                if (n++ > 0)
                    sb.Append(",\n");
                sb.Append("    {\"id\": ").Append(d.Id.ToString(CultureInfo.InvariantCulture))
                  .Append(", \"name\": ").Append(Str(Loc("ItemDesc." + d.Id + ".Name", d.Name)))
                  .Append(", \"typeName\": ").Append(Str(Loc("ItemDesc." + d.Id + ".TypeName", d.TypeName)))
                  .Append(", \"itemType\": ").Append(((int)d.ItemType).ToString(CultureInfo.InvariantCulture))
                  .Append(", \"itemTypeName\": ").Append(Str(d.ItemType.ToString()))
                  .Append(", \"quality\": ").Append(((int)d.Quality).ToString(CultureInfo.InvariantCulture))
                  .Append(", \"carryMax\": ").Append(d.CarryMax.ToString(CultureInfo.InvariantCulture))
                  .Append(", \"sellPrice\": ").Append(d.SellPrice.ToString(CultureInfo.InvariantCulture))
                  .Append(", \"isLevelItem\": ").Append((int)d.IsLevelItem == 1 ? "true" : "false")
                  .Append(", \"consumeType\": ").Append(ConsumeTypeOf(consumes, d))
                  .Append(", \"brief\": ").Append(Str(Loc("ItemDesc." + d.Id + ".BriefDesc", d.BriefDesc)))
                  .Append('}');
            }
            sb.Append("\n  ]\n}\n");

            string json = sb.ToString();
            string safeCulture = culture.Length == 0 ? "unknown" : culture.Replace('/', '_').Replace('\\', '_');
            WriteAtomic(Path.Combine(outDir, "items." + safeCulture + ".json"), json);
            string main = Path.Combine(outDir, "items.json");
            if (culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase) || !File.Exists(main))
                WriteAtomic(main, json);
            DataExportMod.Log("exported " + n + " items (" + culture + ")");
        }

        /// <summary>
        /// 道具的用途（v0.3）：打造 / 防具精进 / 炼丹 / 葫芦与酒升级 / 精魄升级 / 真言 / 商店兑换 / 种子产出 / 药材标记 / 装备部位。
        /// 管理器据此判断哪些道具是材料、丹药、种子，并细分类别。只写原始关系，不做判断。
        /// </summary>
        private static void ExportUsage(string outDir)
        {
            var sb = new StringBuilder(256 * 1024);
            sb.Append("{\n  \"schema\": 1,\n");
            sb.Append("  \"exportedAt\": ").Append(Str(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(",\n");

            Section(sb, "forge", () =>
            {
                var equips = GSProtobufRuntimeAPI<TBEquipDesc, EquipDesc>.Get();
                var list = new StringBuilder();
                int n = 0;
                foreach (var d in GSProtobufRuntimeAPI<TBWeaponBuildDesc, WeaponBuildDesc>.Get().GetAll().List)
                {
                    EquipDesc e = null;
                    try { e = equips.FindByID(d.Id); } catch (Exception) { }
                    Row(list, ref n).Append("{\"id\": ").Append(d.Id).Append(", \"requireWeapon\": ").Append(d.RequireWeapon)
                        .Append(", \"position\": ").Append(e == null ? "-1" : ((int)e.EquipPosition).ToString(CultureInfo.InvariantCulture))
                        .Append(", \"positionName\": ").Append(Str(e == null ? "" : e.EquipPosition.ToString()))
                        .Append(", \"cost\": ").Append(Items(d.CostItem))
                        .Append(", \"key\": ").Append(Items(d.KeyItemList))
                        .Append(", \"transform\": ").Append(Items(d.TransformItem)).Append('}');
                }
                return list.ToString();
            });
            Section(sb, "armorEnhance", () =>
            {
                var list = new StringBuilder();
                int n = 0;
                foreach (var d in GSProtobufRuntimeAPI<TBArmorEnhanceConsumeDesc, ArmorEnhanceConsumeDesc>.Get().GetAll().List)
                    Row(list, ref n).Append("{\"quality\": ").Append((int)d.TargetQuality)
                        .Append(", \"position\": ").Append((int)d.Position)
                        .Append(", \"positionName\": ").Append(Str(d.Position.ToString()))
                        .Append(", \"items\": ").Append(Items(d.ItemList)).Append('}');
                return list.ToString();
            });
            Section(sb, "alchemy", () =>
            {
                var list = new StringBuilder();
                int n = 0;
                foreach (var d in GSProtobufRuntimeAPI<TBItemRecipeDesc, ItemRecipeDesc>.Get().GetAll().List)
                    Row(list, ref n).Append("{\"id\": ").Append(d.Id)
                        .Append(", \"target\": ").Append(d.TargetItem == null ? "null" : "[" + d.TargetItem.Id + ", " + d.TargetItem.Num + "]")
                        .Append(", \"materials\": ").Append(Items(d.MaterialList))
                        .Append(", \"keyItem\": [").Append(d.KeyItemId).Append(", ").Append(d.KeyItemNum).Append(']')
                        .Append(", \"isAttrItem\": ").Append((int)d.IsAttrItem == 1 ? "true" : "false").Append('}');
                return list.ToString();
            });
            Section(sb, "hulu", () =>
            {
                var list = new StringBuilder();
                int n = 0;
                foreach (var d in GSProtobufRuntimeAPI<TBHuluDesc, HuluDesc>.Get().GetAll().List)
                    Row(list, ref n).Append("{\"id\": ").Append(d.Id).Append(", \"cost\": ").Append(Items(d.CostItem)).Append('}');
                return list.ToString();
            });
            Section(sb, "wine", () =>
            {
                var list = new StringBuilder();
                int n = 0;
                foreach (var d in GSProtobufRuntimeAPI<TBWineDesc, WineDesc>.Get().GetAll().List)
                    Row(list, ref n).Append("{\"id\": ").Append(d.Id).Append(", \"cost\": ").Append(Items(d.CostItem)).Append('}');
                return list.ToString();
            });
            Section(sb, "soulSkill", () =>
            {
                var list = new StringBuilder();
                int n = 0;
                foreach (var d in GSProtobufRuntimeAPI<TBSoulSkillDesc, SoulSkillDesc>.Get().GetAll().List)
                    if (d.CostItem.Count > 0)
                        Row(list, ref n).Append("{\"id\": ").Append(d.Id).Append(", \"cost\": ").Append(Items(d.CostItem)).Append('}');
                return list.ToString();
            });
            Section(sb, "mantra", () =>
            {
                var list = new StringBuilder();
                int n = 0;
                foreach (var d in GSProtobufRuntimeAPI<TBLockMantraDesc, LockMantraDesc>.Get().GetAll().List)
                    Row(list, ref n).Append("{\"id\": ").Append(d.Id).Append(", \"cost\": ").Append(Items(d.CostItemList)).Append('}');
                return list.ToString();
            });
            Section(sb, "shop", () =>
            {
                var list = new StringBuilder();
                int n = 0;
                foreach (var d in GSProtobufRuntimeAPI<TBShopItemDesc, ShopItemDesc>.Get().GetAll().List)
                    if (d.CostItem.Count > 0)
                        Row(list, ref n).Append("{\"itemId\": ").Append(d.ItemId).Append(", \"cost\": ").Append(Items(d.CostItem)).Append('}');
                return list.ToString();
            });
            Section(sb, "seeds", () =>
            {
                var list = new StringBuilder();
                int n = 0;
                foreach (var d in GSProtobufRuntimeAPI<TBSeedDesc, SeedDesc>.Get().GetAll().List)
                {
                    var outs = new StringBuilder("[");
                    for (int i = 0; i < d.OutputList.Count; i++)
                        outs.Append(i > 0 ? ", " : "").Append(d.OutputList[i].OutputItemId);
                    Row(list, ref n).Append("{\"id\": ").Append(d.Id).Append(", \"outputs\": ").Append(outs.Append(']')).Append('}');
                }
                return list.ToString();
            });
            Section(sb, "medicinal", () =>
            {
                var list = new StringBuilder();
                int n = 0;
                foreach (var d in GSProtobufRuntimeAPI<TBConsumeDesc, ConsumeDesc>.Get().GetAll().List)
                    if ((int)d.IsMedicinalMaterials == 1)
                        Row(list, ref n).Append(d.Id);
                return list.ToString();
            }, last: true);

            WriteAtomic(Path.Combine(outDir, "usage.json"), sb.ToString());
            DataExportMod.Log("exported usage");
        }

        /// <summary>写一节 "name": [ ... ]；这一节读表失败时写空数组并记日志，不影响其他节。</summary>
        private static void Section(StringBuilder sb, string name, Func<string> rows, bool last = false)
        {
            string body;
            try
            {
                body = rows();
            }
            catch (Exception e)
            {
                DataExportMod.Log("usage section " + name + " failed: " + e.Message);
                body = "";
            }
            sb.Append("  ").Append(Str(name)).Append(": [").Append(body.Length > 0 ? "\n" + body + "\n  " : "").Append(']')
              .Append(last ? "\n}\n" : ",\n");
        }

        private static StringBuilder Row(StringBuilder list, ref int n)
        {
            if (n++ > 0)
                list.Append(",\n");
            return list.Append("    ");
        }

        private static string Items(Google.Protobuf.Collections.RepeatedField<ItemOne> items)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Count; i++)
                sb.Append(i > 0 ? ", " : "").Append('[').Append(items[i].Id).Append(", ").Append(items[i].Num).Append(']');
            return sb.Append(']').ToString();
        }

        /// <summary>消耗品的 ConsumeType（1 = 酒，4 = 可仓储的丹药），非消耗品为 0。</summary>
        private static string ConsumeTypeOf(GSProtobufRuntimeAPI<TBConsumeDesc, ConsumeDesc> consumes, ItemDesc d)
        {
            if (d.ItemType != ItemType.Consume)
                return "0";
            try
            {
                var c = consumes.FindByID(d.Id);
                return c == null ? "0" : ((int)c.Type).ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return "0";
            }
        }

        /// <summary>等级表：Id = 等级，NextLevelExp = 升到下一级所需经验，NextLevelGainTalent = 升级获得的灵光点。</summary>
        private static void ExportLevels(string outDir)
        {
            var levels = GSProtobufRuntimeAPI<TBPlayerLevelDesc, PlayerLevelDesc>.Get().GetAll();
            var sb = new StringBuilder(64 * 1024);
            sb.Append("{\n  \"schema\": 1,\n  \"levels\": [\n");
            int n = 0;
            foreach (var l in levels.List)
            {
                if (n++ > 0)
                    sb.Append(",\n");
                sb.Append("    {\"id\": ").Append(l.Id.ToString(CultureInfo.InvariantCulture))
                  .Append(", \"nextLevelExp\": ").Append(l.NextLevelExp.ToString(CultureInfo.InvariantCulture))
                  .Append(", \"nextLevelGainTalent\": ").Append(l.NextLevelGainTalent.ToString(CultureInfo.InvariantCulture))
                  .Append('}');
            }
            sb.Append("\n  ]\n}\n");
            WriteAtomic(Path.Combine(outDir, "levels.json"), sb.ToString());
            DataExportMod.Log("exported " + n + " levels");
        }

        private static void WriteAtomic(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
        }

        private static string SafeCulture()
        {
            try { return GSLocalization.GetCurrentCulture() ?? ""; }
            catch (Exception) { return ""; }
        }

        /// <summary>按当前语言取文本；取不到或返回键本身时用原始字段兜底。</summary>
        private static string Loc(string key, string fallback)
        {
            try
            {
                string s = GSLocalization.GetLocaliztionalFText(key, false).ToString();
                if (!string.IsNullOrEmpty(s) && s != key)
                    return s;
            }
            catch (Exception)
            {
            }
            return fallback ?? "";
        }

        private static string Str(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
