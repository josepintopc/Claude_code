using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SupernovaBench.Core.Extras;

namespace SupernovaBench.App.Views;

/// <summary>
/// Visor de FPS: janela transparente, sempre por cima, que não recebe cliques nem foco (não atrapalha o jogo).
/// É posicionada em pixels físicos no monitor do jogo, então acerta o canto em qualquer monitor e escala de DPI.
/// Funciona sobre jogos em janela, tela cheia sem bordas e na tela cheia do Windows 10/11; a tela cheia exclusiva
/// "pura" (jogos antigos) não deixa nada por cima.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    private const int MarginDip = 12;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    private string _corner = OverlayLayout.DefaultCorner;
    private PixelRect? _monitor;

    public OverlayWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Apply();
        Loaded += (_, _) => Apply();
    }

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        int ex = GetWindowLong(Handle, GWL_EXSTYLE);
        SetWindowLong(Handle, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE);
        Apply();
    }

    /// <summary>Coloca o visor no canto pedido do monitor do jogo (área inteira da tela, em pixels físicos).</summary>
    public void PlaceOn(PixelRect monitor, string corner)
    {
        _monitor = monitor;
        _corner = corner;
        Apply();
    }

    private void Apply()
    {
        var hwnd = Handle;
        if (hwnd == IntPtr.Zero || _monitor is not { } monitor || !GetWindowRect(hwnd, out var r)) return;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int margin = (int)Math.Round(MarginDip * scale);
        var (x, y) = OverlayLayout.Place(_corner, monitor, r.Right - r.Left, r.Bottom - r.Top, margin);
        if (x != r.Left || y != r.Top)
            SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Alguns jogos se colocam por cima de tudo: reafirma o "sempre no topo" sem roubar o foco.</summary>
    public void KeepOnTop()
    {
        var hwnd = Handle;
        if (hwnd != IntPtr.Zero && IsVisible)
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }
}
