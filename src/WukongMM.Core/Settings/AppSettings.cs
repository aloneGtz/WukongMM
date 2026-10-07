using System.Text.Json;

namespace WukongMM.Core.Settings;

/// <summary>应用设置：%APPDATA%\WukongMM\settings.json。</summary>
public sealed class AppSettings
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WukongMM");

    private static string FilePath => Path.Combine(DataDirectory, "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>游戏根目录（含 b1.exe 的那一层）。为空时自动查找。</summary>
    public string? GameRoot { get; set; }

    /// <summary>启动游戏时附加 -fileopenlog（pak mod 生效所需）。</summary>
    public bool LaunchWithFileOpenLog { get; set; } = true;

    /// <summary>上次在存档页选择的存档文件。</summary>
    public string? LastSavePath { get; set; }

    /// <summary>界面语言："zh"（默认）或 "en"。</summary>
    public string Language { get; set; } = "zh";

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch (Exception)
        {
            // 设置文件损坏时用默认值
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
