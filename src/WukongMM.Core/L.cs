namespace WukongMM.Core;

/// <summary>
/// 界面语言（中文 / 英文）。所有给用户看的文字都写成 <c>L.T("中文", "English")</c>，按当前语言取一个。
/// 默认中文；命令行工具（开发用）始终是中文。
/// </summary>
public static class L
{
    public static bool English { get; set; }

    public static string T(string zh, string en) => English ? en : zh;

    /// <summary>设置文件里的语言代码。</summary>
    public static string Code => English ? "en" : "zh";
}
