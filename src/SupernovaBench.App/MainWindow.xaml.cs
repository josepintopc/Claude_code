using System.ComponentModel;
using System.Windows;
using SupernovaBench.App.Services;
using SupernovaBench.App.ViewModels;
using SupernovaBench.Core;
using SupernovaBench.Core.Localization;

namespace SupernovaBench.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        AppServices.Main = _vm;
        DataContext = _vm;
        StateChanged += (_, _) => UpdateMaximizedState();
        UpdateMaximizedState();
    }

    private void UpdateMaximizedState()
    {
        // Com WindowChrome, a janela maximizada passa um pouco da tela: compensa com margem.
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_vm.IsBenchmarkRunning)
        {
            var answer = MessageBox.Show(this, Loc.T("cpu.leaveWarning"), AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _vm.StopBenchmarks();
        }
        base.OnClosing(e);
    }
}
