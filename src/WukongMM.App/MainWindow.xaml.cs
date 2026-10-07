using System.Windows;
using WukongMM.App.ViewModels;

namespace WukongMM.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
