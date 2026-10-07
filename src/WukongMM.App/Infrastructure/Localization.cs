using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using WukongMM.Core;

namespace WukongMM.App.Infrastructure;

/// <summary>界面语言的通知源：切换语言时所有 {l:Tr} 绑定和视图模型一起刷新。</summary>
public sealed class LocalizationSource : INotifyPropertyChanged
{
    public static LocalizationSource Instance { get; } = new();

    public bool English => L.English;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>语言切换后触发（视图模型据此刷新自己算出来的文字）。</summary>
    public static event Action? Changed;

    public static void Set(bool english)
    {
        if (L.English == english)
            return;
        L.English = english;
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs(nameof(English)));
        Changed?.Invoke();
    }
}

/// <summary>
/// XAML 里的双语文字：<c>Text="{l:Tr '中文', 'English'}"</c>，随界面语言切换。
/// 注意 XAML 标记扩展里反斜杠是转义符，路径要写成 <c>b1\\ModManager</c>；含英文逗号的文字要用单引号括起来。
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string zh, string en)
    {
        Zh = zh;
        En = en;
    }

    public string Zh { get; set; } = "";

    public string En { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding(nameof(LocalizationSource.English))
        {
            Source = LocalizationSource.Instance,
            Mode = BindingMode.OneWay,
            Converter = new PickConverter(Zh, En),
        };
        return binding.ProvideValue(serviceProvider);
    }

    private sealed class PickConverter(string zh, string en) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is true ? en : zh;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
