using System.Windows;
using System.Windows.Threading;
using WukongMM.App.Infrastructure;

namespace WukongMM.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        base.OnStartup(e);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Dialogs.Error(WukongMM.Core.L.T($"发生了未处理的错误：\n{e.Exception.Message}", $"An unexpected error occurred:\n{e.Exception.Message}"));
        e.Handled = true;
    }
}
