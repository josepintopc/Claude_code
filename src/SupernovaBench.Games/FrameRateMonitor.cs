using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using SupernovaBench.Core.Extras;
using PixelRect = SupernovaBench.Core.Extras.PixelRect;

namespace SupernovaBench.Games;

/// <summary>
/// FPS de qualquer jogo DirectX 9 a 12, contando os eventos "Present" que o próprio Windows publica (ETW),
/// a mesma técnica do PresentMon. Não injeta nada no jogo. Precisa de administrador.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FrameRateMonitor : IDisposable
{
    public const string SessionName = "SupernovaBench-FPS";
    private static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9 = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private const int DxgiPresentStart = 42, DxgiPresentMpoStart = 55, D3D9PresentStart = 1;

    private readonly Stopwatch _clock = new();
    private TraceEventSession? _session;
    private Thread? _thread;
    private double _offsetMs;

    public FrameCounter Counter { get; } = new();
    public bool IsRunning => _session != null && _thread is { IsAlive: true };
    public string? Error { get; private set; }

    /// <summary>Agora, na mesma escala de tempo dos eventos (ms).</summary>
    public double NowMs => _clock.Elapsed.TotalMilliseconds + _offsetMs;

    public bool Start()
    {
        if (IsRunning) return true;
        Error = null;
        try
        {
            if (TraceEventSession.IsElevated() != true)
            {
                Error = "admin";
                return false;
            }
            // Sessão de uma execução anterior que fechou sem parar: Create a substitui.
            _session = new TraceEventSession(SessionName, TraceEventSessionOptions.Create) { StopOnDispose = true };
            _session.EnableProvider(Dxgi, TraceEventLevel.Verbose, ulong.MaxValue);
            _session.EnableProvider(D3D9, TraceEventLevel.Verbose, ulong.MaxValue);
            bool synced = false;
            _session.Source.AllEvents += e =>
            {
                int id = (int)e.ID;
                bool present = (e.ProviderGuid == Dxgi && (id == DxgiPresentStart || id == DxgiPresentMpoStart))
                               || (e.ProviderGuid == D3D9 && id == D3D9PresentStart);
                if (!present) return;
                double t = e.TimeStampRelativeMSec;
                if (!synced)
                {
                    // Alinha o relógio local com o dos eventos no primeiro quadro.
                    _offsetMs = t - _clock.Elapsed.TotalMilliseconds;
                    synced = true;
                }
                Counter.AddPresent(e.ProcessID, t);
            };
            _clock.Restart();
            _thread = new Thread(() =>
            {
                try { _session.Source.Process(); }
                catch (Exception ex) { Error = ex.Message; }
            })
            { IsBackground = true, Name = "Supernova FPS (ETW)" };
            _thread.Start();
            return true;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        try { _session?.Dispose(); }
        catch (Exception) { }
        _session = null;
        _thread?.Join(2000);
        _thread = null;
    }

    public void Dispose() => Stop();
}

/// <summary>Janela em primeiro plano e taxa de atualização do monitor.</summary>
[SupportedOSPlatform("windows")]
public static class ForegroundWindow
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    private const uint MONITOR_DEFAULTTOPRIMARY = 1, MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>(janela, pid) em primeiro plano.</summary>
    public static (IntPtr Hwnd, int Pid)? CurrentWindow()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out int pid);
        return pid > 0 ? (hwnd, pid) : null;
    }

    /// <summary>Área inteira do monitor onde a janela está (pixels físicos). Sem janela, o monitor principal.</summary>
    public static PixelRect MonitorBounds(IntPtr hwnd)
    {
        var mon = MonitorFromWindow(hwnd, hwnd == IntPtr.Zero ? MONITOR_DEFAULTTOPRIMARY : MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref info))
            return new PixelRect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom);
        return new PixelRect(0, 0, 1920, 1080);
    }

    /// <summary>(pid, nome do processo) da janela em foco.</summary>
    public static (int Pid, string Name)? Current()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out int pid);
        if (pid <= 0) return null;
        try
        {
            using var p = Process.GetProcessById(pid);
            return (pid, p.ProcessName);
        }
        catch (Exception)
        {
            return (pid, pid.ToString());
        }
    }

    public static string? ProcessName(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Taxa de atualização do monitor principal (Hz).</summary>
    public static int? RefreshRate()
    {
        var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        return EnumDisplaySettings(null, -1, ref dm) && dm.dmDisplayFrequency > 1 ? dm.dmDisplayFrequency : null;
    }
}
