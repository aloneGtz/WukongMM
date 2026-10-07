using System.Text.Json;
using WukongMM.Core.Settings;

namespace WukongMM.Core.Items;

/// <summary>
/// 游戏道具表里的一条（由游戏内导出 mod WukongMMDataExport 生成）。
/// <see cref="IsLevelItem"/>、<see cref="ConsumeType"/> 从导出 mod v0.2（schema 2）起才有，旧表里恒为 false / 0。
/// </summary>
public sealed record GameItemInfo(int Id, string Name, string TypeName, int ItemType, string ItemTypeName, int Quality, int CarryMax, string Brief,
                                  bool IsLevelItem = false, int ConsumeType = 0);

/// <summary>
/// 道具 ID → 名称 / 类别 / 携带上限。
/// 来源优先级：用户备注 &gt; 游戏导出表（&lt;b1&gt;\ModManager\GameData\items.json；英文界面优先用 items.en*.json 的英文名）&gt; 按 ID 段推测。
/// 道具用途（usage.json，导出 mod v0.3）决定哪些道具能在存档页修改、属于哪个细分类别（<see cref="ItemClassifier"/>）。
/// </summary>
public sealed class ItemCatalog
{
    private static string UserNamesPath => Path.Combine(AppSettings.DataDirectory, "item_names.json");

    private readonly Dictionary<int, string> _userNames = [];
    private readonly Dictionary<int, GameItemInfo> _game = [];
    private readonly Dictionary<int, string> _zhNames = [];
    private readonly Dictionary<int, string> _enNames = [];
    private string _culture = "";
    private string _exportedAt = "";

    /// <summary>游戏导出表是否已加载。</summary>
    public bool HasGameData => _game.Count > 0;

    /// <summary>导出表的格式版本（1 = 导出 mod v0.1；2 起含 isLevelItem / consumeType）。</summary>
    public int GameDataSchema { get; private set; }

    /// <summary>导出表里有没有 isLevelItem / consumeType（能否区分酒、关卡道具）。</summary>
    public bool HasExtendedFields => HasGameData && GameDataSchema >= 2;

    /// <summary>有没有英文道具名（游戏切到英文后导出的 items.en*.json）。</summary>
    public bool HasEnglishNames => _enNames.Count > 0;

    /// <summary>道具在配方里的用途（导出 mod v0.3 的 usage.json），没有时为 null。</summary>
    public ItemUsage? Usage { get; private set; }

    public string? GameDataInfo => HasGameData
        ? L.T($"游戏道具表：{_game.Count} 条（语言 {_culture}，导出于 {_exportedAt}）",
              $"Game item table: {_game.Count} entries (language {_culture}, exported {_exportedAt})")
        : null;

    /// <summary>游戏表里的全部道具（按 ID 升序）。</summary>
    public IEnumerable<GameItemInfo> GameItems => _game.Values.OrderBy(g => g.Id);

    public static ItemCatalog Load()
    {
        var catalog = new ItemCatalog();
        try
        {
            if (File.Exists(UserNamesPath))
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(UserNamesPath)) ?? [];
                foreach (var (k, v) in raw)
                    if (int.TryParse(k, out int id) && !string.IsNullOrWhiteSpace(v))
                        catalog._userNames[id] = v;
            }
        }
        catch (Exception)
        {
            // 文件损坏则忽略
        }
        return catalog;
    }

    /// <summary>
    /// 读取游戏导出的道具表；同目录下的 usage.json（道具用途）和 items.&lt;语言&gt;.json（其他语言的道具名）一并读取。
    /// items.json 不存在时返回 false。
    /// </summary>
    public bool LoadGameData(string itemsJsonPath)
    {
        Clear();
        if (!File.Exists(itemsJsonPath))
            return false;
        try
        {
            LoadGameDataJson(File.ReadAllText(itemsJsonPath));
        }
        catch (Exception)
        {
            Clear();
            return false;
        }
        string dir = Path.GetDirectoryName(itemsJsonPath) ?? "";
        Usage = ItemUsage.TryLoad(Path.Combine(dir, "usage.json"));
        foreach (var file in SafeFiles(dir, "items.*.json"))
        {
            string culture = Path.GetFileNameWithoutExtension(file)["items.".Length..];
            var target = culture.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? _enNames
                       : culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? _zhNames
                       : null;
            if (target is null)
                continue;
            try
            {
                LoadNames(File.ReadAllText(file), target);
            }
            catch (Exception)
            {
                // 某个语言的表损坏不影响主表
            }
        }
        return true;
    }

    private void Clear()
    {
        _game.Clear();
        _zhNames.Clear();
        _enNames.Clear();
        GameDataSchema = 0;
        Usage = null;
        _culture = "";
        _exportedAt = "";
    }

    /// <summary>解析导出 mod 的 items.json 内容（格式错误时抛异常）。</summary>
    public void LoadGameDataJson(string json)
    {
        _game.Clear();
        _zhNames.Clear();
        _enNames.Clear();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        foreach (var e in root.GetProperty("items").EnumerateArray())
        {
            var info = new GameItemInfo(
                e.GetProperty("id").GetInt32(),
                Str(e, "name"), Str(e, "typeName"), Int(e, "itemType"), Str(e, "itemTypeName"),
                Int(e, "quality"), Int(e, "carryMax"), Str(e, "brief"),
                e.TryGetProperty("isLevelItem", out var lv) && lv.ValueKind == JsonValueKind.True,
                Int(e, "consumeType"));
            _game[info.Id] = info;
        }
        GameDataSchema = Int(root, "schema") is > 0 and var s ? s : 1;
        _culture = Str(root, "culture");
        _exportedAt = Str(root, "exportedAt");
        // 主表的名字按它自己的语言归类（一般是中文）
        var names = _culture.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? _enNames : _zhNames;
        foreach (var g in _game.Values)
            names[g.Id] = g.Name;
    }

    /// <summary>读取 items.&lt;语言&gt;.json 里的名称。</summary>
    public static void LoadNames(string json, Dictionary<int, string> into)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var e in doc.RootElement.GetProperty("items").EnumerateArray())
            if (Str(e, "name") is { Length: > 0 } name && !name.StartsWith("ItemDesc.", StringComparison.Ordinal))
                into[e.GetProperty("id").GetInt32()] = name;
    }

    /// <summary>测试用：直接设置英文名。</summary>
    public void SetEnglishName(int id, string name) => _enNames[id] = name;

    /// <summary>测试用：直接设置道具用途。</summary>
    public void SetUsage(ItemUsage? usage) => Usage = usage;

    public GameItemInfo? GameInfo(int id) => _game.GetValueOrDefault(id);

    /// <summary>显示用名称：用户备注优先，其次按界面语言取游戏里的名称（英文名没有时用中文名）。</summary>
    public string? NameOf(int id) => _userNames.GetValueOrDefault(id) ?? GameNameOf(id);

    /// <summary>游戏里的名称（不含用户备注）。</summary>
    public string? GameNameOf(int id) =>
        (L.English ? _enNames.GetValueOrDefault(id) : null) ?? _zhNames.GetValueOrDefault(id) ?? _game.GetValueOrDefault(id)?.Name;

    public bool HasUserName(int id) => _userNames.ContainsKey(id);

    /// <summary>道具在游戏表里存在吗？（游戏表未加载时返回 null 表示未知）</summary>
    public bool? ExistsInGame(int id) => HasGameData ? _game.ContainsKey(id) : null;

    /// <summary>携带上限（游戏表里的 CarryMax，0 或未知时返回 null）。</summary>
    public int? CarryMaxOf(int id) => _game.GetValueOrDefault(id) is { CarryMax: > 0 } g ? g.CarryMax : null;

    /// <summary>
    /// 可仓储的丹药（ItemType 4 且 ConsumeType 4，游戏 IsItemSupportStorage）：获得时超出上限的部分进仓储，不受携带上限限制。
    /// 旧版道具表没有 ConsumeType，恒为 false。
    /// </summary>
    public bool IsStorable(int id) => _game.GetValueOrDefault(id) is { ItemType: 4, ConsumeType: 4 };

    /// <summary>存档页的细分类别；<see cref="ItemClass.Hidden"/> 表示不在可修改范围。</summary>
    public ItemClass ClassOf(int id) => ItemClassifier.Classify(id, _game.GetValueOrDefault(id), HasGameData, Usage);

    /// <summary>能否在存档页修改（显示在道具列表 / 新增道具下拉框里）。</summary>
    public bool IsEditable(int id) => ClassOf(id) != ItemClass.Hidden;

    /// <summary>类别：可修改的道具用细分类别；其余用游戏的道具类型（没有游戏表时为"未知"）。</summary>
    public string CategoryOf(int id)
    {
        var c = ClassOf(id);
        if (c != ItemClass.Hidden)
            return ItemClassifier.DisplayName(c);
        return _game.GetValueOrDefault(id) is { } g ? ItemTypeDisplay(g.ItemType, g.ConsumeType) : L.T("未知", "Unknown");
    }

    public void SetUserName(int id, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == GameNameOf(id))
            _userNames.Remove(id);
        else
            _userNames[id] = name.Trim();
    }

    public void Save()
    {
        Directory.CreateDirectory(AppSettings.DataDirectory);
        var raw = _userNames.OrderBy(p => p.Key).ToDictionary(p => p.Key.ToString(), p => p.Value);
        File.WriteAllText(UserNamesPath, JsonSerializer.Serialize(raw, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
    }

    /// <summary>ResB1.ItemType 枚举（来自游戏程序集 Protobuf.RunTime.dll）；消耗品按 ConsumeType 再细分。</summary>
    public static string ItemTypeDisplay(int itemType, int consumeType = 0) => itemType switch
    {
        1 => L.T("灵蕴", "Will"),
        2 => L.T("经验", "Experience"),
        3 => L.T("材料", "Material"),
        4 => consumeType switch
        {
            1 => L.T("酒", "Drink"),
            4 => L.T("丹药", "Pill"),
            5 => L.T("泡酒物", "Soak"),
            _ => L.T("消耗品", "Consumable"),
        },
        5 => L.T("任务道具", "Quest item"),
        6 => L.T("装备", "Equipment"),
        7 => L.T("属性丹药", "Attribute pill"),
        8 => L.T("法术", "Spell"),
        9 => L.T("酒升级", "Drink upgrade"),
        10 => L.T("葫芦升级", "Gourd upgrade"),
        11 => L.T("丹方", "Formula"),
        12 => L.T("灵光点", "Spark"),
        13 => L.T("催化剂", "Catalyst"),
        14 => L.T("传承", "Relic"),
        15 => L.T("古董", "Antique"),
        16 => L.T("珍玩", "Curio"),
        17 => L.T("地图", "Map"),
        18 => L.T("精魄", "Spirit"),
        19 => L.T("种子", "Seed"),
        20 => L.T("珍玩栏扩充", "Curio slot"),
        21 => L.T("人种袋", "Seed pouch"),
        22 => L.T("解锁传承天赋", "Relic talent unlock"),
        23 => L.T("葫芦自动升级", "Gourd auto upgrade"),
        24 => L.T("道具转化", "Item conversion"),
        _ => L.T($"类型{itemType}", $"Type {itemType}"),
    };

    private static IEnumerable<string> SafeFiles(string dir, string pattern)
    {
        try
        {
            return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
}
