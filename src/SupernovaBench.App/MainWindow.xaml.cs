using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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

    // ------------------------------------------------------------------ atalho global do overlay (Ctrl+Shift+O)

    private const int HotkeyId = 0x5342, WmHotkey = 0x0312;
    private const uint ModControl = 0x2, ModShift = 0x4, ModNoRepeat = 0x4000, VkO = 0x4F;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        if (HwndSource.FromHwnd(handle) is { } source)
        {
            source.AddHook(HotkeyHook);
            if (!RegisterHotKey(handle, HotkeyId, ModControl | ModShift | ModNoRepeat, VkO))
                App.Log(new Exception("Atalho Ctrl+Shift+O já está em uso por outro programa."));
        }
    }

    private IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            AppServices.Overlay.ToggleOverlay();
            handled = true;
        }
        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId);
        base.OnClosed(e);
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
