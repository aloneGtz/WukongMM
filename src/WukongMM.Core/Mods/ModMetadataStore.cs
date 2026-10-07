using System.Text.Json;
using System.Text.Json.Serialization;
using WukongMM.Core.Game;

namespace WukongMM.Core.Mods;

/// <summary>用户给 mod 加的备注（不含启停状态——状态以文件位置为准）。</summary>
public sealed class ModMetadata
{
    public string? DisplayName { get; set; }
    public string? Notes { get; set; }
    public string? SourceUrl { get; set; }
    public DateTime FirstSeen { get; set; }
}

/// <summary>用户自定义的 mod 分组。</summary>
public sealed class CustomModDefinition
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public List<string> RelativePaths { get; set; } = [];
}

public sealed class ModMetadataDocument
{
    public int Version { get; set; } = 1;
    public Dictionary<string, ModMetadata> Mods { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<CustomModDefinition> CustomMods { get; set; } = [];
}

/// <summary>&lt;b1&gt;\ModManager\mods.json 的读写。</summary>
public sealed class ModMetadataStore(GamePaths paths)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ModMetadataDocument Data { get; private set; } = new();

    public void Load()
    {
        if (!File.Exists(paths.ModMetadataFile))
        {
            Data = new ModMetadataDocument();
            return;
        }
        var doc = JsonSerializer.Deserialize<ModMetadataDocument>(File.ReadAllText(paths.ModMetadataFile), Options)
                  ?? new ModMetadataDocument();
        // 反序列化出来的字典默认区分大小写，换成不区分的
        doc.Mods = new Dictionary<string, ModMetadata>(doc.Mods, StringComparer.OrdinalIgnoreCase);
        Data = doc;
    }

    public void Save()
    {
        Directory.CreateDirectory(paths.ManagerData);
        string tmp = paths.ModMetadataFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Data, Options));
        File.Move(tmp, paths.ModMetadataFile, overwrite: true);
    }

    public ModMetadata? Get(string id) => Data.Mods.GetValueOrDefault(id);

    public ModMetadata GetOrCreate(string id)
    {
        if (!Data.Mods.TryGetValue(id, out var meta))
        {
            meta = new ModMetadata { FirstSeen = DateTime.Now };
            Data.Mods[id] = meta;
        }
        return meta;
    }

    public string DisplayNameOf(ModEntry mod) =>
        Get(mod.Id)?.DisplayName is { Length: > 0 } name ? name : mod.DefaultName;
}
