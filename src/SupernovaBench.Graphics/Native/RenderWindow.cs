using System.Runtime.InteropServices;
using static SupernovaBench.Graphics.Native.Win32;

namespace SupernovaBench.Graphics.Native;

/// <summary>
/// Janela Win32 crua usada pelos testes de GPU. Deve ser criada, bombeada e destruída na mesma thread (a de renderização).
/// ALT sozinho não pausa a renderização (SC_KEYMENU é ignorado); ESC ou fechar a janela pedem para parar.
/// <para>Baseado no ResetExplode (MIT, © 2026 Gustavo / GustavoReset).</para>
/// </summary>
internal sealed class RenderWindow : IDisposable
{
    private readonly WndProc _proc; // mantido vivo enquanto a janela existir
    private readonly string _class;
    private readonly IntPtr _hInst;

    public IntPtr Handle { get; private set; }

    /// <summary>O usuário pediu para fechar (ESC ou botão fechar).</summary>
    public bool CloseRequested { get; private set; }

    private RenderWindow(string title, int clientW, int clientH, bool fullscreen)
    {
        _proc = WindowProc;
        _class = "SupernovaBench.Render." + Environment.TickCount64;
        _hInst = GetModuleHandleW(null);
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = _hInst,
            hCursor = LoadCursorW(IntPtr.Zero, (IntPtr)32512), // IDC_ARROW
            lpszClassName = _class,
        };
        RegisterClassExW(ref wc);

        int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
        int x, y, w, h;
        uint style;
        if (fullscreen)
        {
            style = WS_POPUP | WS_VISIBLE;
            (x, y, w, h) = (0, 0, sw, sh);
        }
        else
        {
            style = WS_OVERLAPPEDWINDOW | WS_VISIBLE;
            var r = new RECT { Right = clientW, Bottom = clientH };
            AdjustWindowRect(ref r, style, false);
            w = r.Right - r.Left;
            h = r.Bottom - r.Top;
            x = Math.Max(0, (sw - w) / 2);
            y = Math.Max(0, (sh - h) / 2);
        }
        Handle = CreateWindowExW(0, _class, title, style, x, y, w, h, IntPtr.Zero, IntPtr.Zero, _hInst, IntPtr.Zero);
        if (Handle == IntPtr.Zero)
        {
            UnregisterClassW(_class, _hInst);
            throw new InvalidOperationException("CreateWindowExW failed");
        }
        SetForegroundWindow(Handle);
    }

    /// <summary>Janela com área útil do tamanho pedido (ou tela cheia sem bordas).</summary>
    public static RenderWindow Create(string title, int width, int height, bool fullscreen) => new(title, width, height, fullscreen);

    /// <summary>Janela que cabe em 85% da tela mantendo a proporção da resolução de renderização.</summary>
    public static RenderWindow CreateFitted(string title, int renderW, int renderH)
    {
        int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
        double scale = Math.Min(1.0, Math.Min(sw * 0.85 / renderW, sh * 0.85 / renderH));
        return new RenderWindow(title, Math.Max(320, (int)(renderW * scale)), Math.Max(180, (int)(renderH * scale)), false);
    }

    /// <summary>Processa as mensagens pendentes (chamar uma vez por frame).</summary>
    public void Pump()
    {
        while (PeekMessageW(out var msg, IntPtr.Zero, 0, 0, 1))
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    public (int Width, int Height) ClientSize
    {
        get
        {
            GetClientRect(Handle, out var rc);
            return (rc.Right - rc.Left, rc.Bottom - rc.Top);
        }
    }

    public void SetTitle(string text) => SetWindowTextW(Handle, text);

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // ALT sozinho abriria o "modo menu" da janela, um loop modal que pausaria a renderização.
        if (msg == WM_SYSCOMMAND && ((long)wParam & 0xFFF0) == SC_KEYMENU) return IntPtr.Zero;
        if (msg == WM_CLOSE || (msg == WM_KEYDOWN && (long)wParam == VK_ESCAPE))
        {
            CloseRequested = true;
            return IntPtr.Zero;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            DestroyWindow(Handle);
            Handle = IntPtr.Zero;
        }
        UnregisterClassW(_class, _hInst);
        GC.KeepAlive(_proc);
    }
}
