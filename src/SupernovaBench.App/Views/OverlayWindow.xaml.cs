using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SupernovaBench.App.Views;

/// <summary>
/// Janela transparente, sempre por cima e que não recebe cliques nem foco (não atrapalha o jogo).
/// Funciona sobre jogos em janela ou "tela cheia sem bordas"; em tela cheia exclusiva o Windows não deixa nada por cima.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private string _corner = "topleft";

    public OverlayWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => PlaceAt(_corner);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE);
    }

    /// <summary>Canto da tela principal: topleft, topright, bottomleft ou bottomright.</summary>
    public void PlaceAt(string corner)
    {
        _corner = corner;
        var area = SystemParameters.WorkArea;
        double w = ActualWidth > 0 ? ActualWidth : 200, h = ActualHeight > 0 ? ActualHeight : 110;
        Left = corner.EndsWith("right", StringComparison.Ordinal) ? area.Right - w : area.Left;
        Top = corner.StartsWith("bottom", StringComparison.Ordinal) ? area.Bottom - h : area.Top;
    }

    /// <summary>Alguns jogos se colocam por cima de tudo: reafirma o "sempre no topo" sem roubar o foco.</summary>
    public void KeepOnTop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }
}
