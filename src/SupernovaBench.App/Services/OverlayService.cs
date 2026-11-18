using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SupernovaBench.Core.Extras;
using SupernovaBench.Core.Info;
using SupernovaBench.Core.Localization;
using SupernovaBench.Games;

namespace SupernovaBench.App.Services;

/// <summary>Parte do tempo de uma sessão em cada situação (para as barras do resumo).</summary>
public sealed record ShareRow(string Label, double Percent, string PercentText);

/// <summary>
/// FPS dos jogos (ETW), detector de gargalo e overlay. A medição só roda enquanto o overlay está ligado ou a página
/// "Monitor de jogo" está aberta. Atualiza uma vez por segundo.
/// </summary>
public sealed partial class OverlayService : ObservableObject
{
    private readonly FrameRateMonitor _fps = new();
    private readonly BottleneckAnalyzer _live = new(windowSeconds: 5);
    private readonly DispatcherTimer _timer;
    private readonly int _selfPid = Environment.ProcessId;
    private BottleneckAnalyzer? _sessionAnalyzer;
    private DateTime _sessionStart;
    private string _sessionApp = "";
    private int _pageUsers;
    private int? _refreshHz;
    private Views.OverlayWindow? _window;

    public OverlayService()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
    }

    // ------------------------------------------------------------------ estado ao vivo

    [ObservableProperty] private bool _isMeasuring;
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private string _appName = "";
    [ObservableProperty] private bool _hasFps;
    [ObservableProperty] private string _fpsText = "—";
    [ObservableProperty] private string _frameTimeText = "";
    [ObservableProperty] private string _cpuText = "";
    [ObservableProperty] private string _gpuText = "";
    [ObservableProperty] private Bottleneck _bottleneck;
    [ObservableProperty] private string _bottleneckText = "";
    [ObservableProperty] private string _bottleneckAdvice = "";

    // ------------------------------------------------------------------ gravação de sessão

    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _hasSummary;
    [ObservableProperty] private string _summaryTitle = "";
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private string _summaryVerdict = "";
    [ObservableProperty] private string _summaryAdvice = "";
    public ObservableCollection<ShareRow> SummaryShares { get; } = new();

    // ------------------------------------------------------------------ overlay

    public bool OverlayEnabled
    {
        get => AppServices.Settings.OverlayEnabled;
        set
        {
            if (AppServices.Settings.OverlayEnabled == value) return;
            AppServices.Settings.OverlayEnabled = value;
            AppServices.TrySaveSettings();
            OnPropertyChanged();
            UpdateRunning();
        }
    }

    public bool ShowSensors
    {
        get => AppServices.Settings.OverlayShowSensors;
        set { AppServices.Settings.OverlayShowSensors = value; AppServices.TrySaveSettings(); OnPropertyChanged(); }
    }

    public bool ShowBottleneck
    {
        get => AppServices.Settings.OverlayShowBottleneck;
        set { AppServices.Settings.OverlayShowBottleneck = value; AppServices.TrySaveSettings(); OnPropertyChanged(); }
    }

    public double OverlayOpacity
    {
        get => AppServices.Settings.OverlayOpacity;
        set { AppServices.Settings.OverlayOpacity = Math.Clamp(value, 0.3, 1); AppServices.TrySaveSettings(); OnPropertyChanged(); }
    }

    public string OverlayCorner
    {
        get => AppServices.Settings.OverlayCorner;
        set
        {
            AppServices.Settings.OverlayCorner = value;
            AppServices.TrySaveSettings();
            OnPropertyChanged();
            _window?.PlaceAt(value);
        }
    }

    public void ToggleOverlay() => OverlayEnabled = !OverlayEnabled;

    public void StartIfEnabled() => UpdateRunning();

    /// <summary>A página Monitor de jogo abriu/fechou (a medição roda enquanto alguém usa).</summary>
    public void Acquire()
    {
        _pageUsers++;
        UpdateRunning();
    }

    public void Release()
    {
        _pageUsers = Math.Max(0, _pageUsers - 1);
        UpdateRunning();
    }

    private void UpdateRunning()
    {
        bool want = OverlayEnabled || _pageUsers > 0 || IsRecording;
        if (want && !IsMeasuring)
        {
            if (_fps.Start())
            {
                IsMeasuring = true;
                ErrorText = "";
                _refreshHz = ForegroundWindow.RefreshRate();
                _timer.Start();
            }
            else
            {
                ErrorText = _fps.Error == "admin" ? Loc.T("monitor.error.admin") : Loc.T("monitor.error.etw", _fps.Error ?? "?");
            }
        }
        else if (!want && IsMeasuring)
        {
            _timer.Stop();
            _fps.Stop();
            IsMeasuring = false;
            _live.Reset();
            ClearLive();
        }

        if (OverlayEnabled && IsMeasuring)
        {
            _window ??= new Views.OverlayWindow { DataContext = this };
            _window.PlaceAt(OverlayCorner);
            if (!_window.IsVisible) _window.Show();
        }
        else if (_window != null)
        {
            _window.Close();
            _window = null;
        }
    }

    private void ClearLive()
    {
        AppName = "";
        HasFps = false;
        FpsText = "—";
        FrameTimeText = CpuText = GpuText = BottleneckText = BottleneckAdvice = "";
        Bottleneck = Bottleneck.Unknown;
    }

    // ------------------------------------------------------------------ a cada segundo

    private void Tick()
    {
        try
        {
            double now = _fps.NowMs;
            _fps.Counter.Prune(now);

            // Jogo = janela em foco; se ela não entrega quadros (ex.: o próprio Supernova em foco), o processo com mais quadros.
            var fg = ForegroundWindow.Current();
            FrameSnapshot? snap = null;
            string? name = null;
            if (fg is { } f && f.Pid != _selfPid && _fps.Counter.Snapshot(f.Pid, now) is { } s)
            {
                snap = s;
                name = f.Name;
            }
            else if (_fps.Counter.Busiest(now) is { } busy && busy != _selfPid && _fps.Counter.Snapshot(busy, now) is { } s2)
            {
                snap = s2;
                name = ForegroundWindow.ProcessName(busy);
            }

            var m = AppServices.Sensors.Monitor;
            var gpuSample = AppServices.Gpu.ReadSample();
            double? gpuLoad = gpuSample?.LoadPercent ?? m.GpuLoad;
            double? gpuTemp = gpuSample?.TemperatureC ?? m.GpuTemperature;
            var (total, available) = MemoryStatus.Read();
            double? ramLoad = total > 0 ? 100.0 * (total - available) / total : null;
            var sample = new PerfSample(gpuLoad, m.CpuLoad, m.CpuMaxCoreLoad, m.GpuMemoryLoad, ramLoad, snap?.Fps, _refreshHz);

            var c = Loc.Culture;
            CpuText = Loc.T("monitor.cpuLine", Fmt(m.CpuLoad, "0", "%"), Fmt(m.CpuMaxCoreLoad, "0", "%"), Fmt(m.CpuTemperature, "0", " °C"));
            GpuText = Loc.T("monitor.gpuLine", Fmt(gpuLoad, "0", "%"), Fmt(m.GpuMemoryLoad, "0", "%"), Fmt(gpuTemp, "0", " °C"));

            if (snap == null)
            {
                HasFps = false;
                AppName = Loc.T("monitor.noGame");
                FpsText = "—";
                FrameTimeText = "";
                Bottleneck = Bottleneck.Unknown;
                BottleneckText = Loc.T("monitor.waiting");
                BottleneckAdvice = "";
                return;
            }

            HasFps = true;
            AppName = name ?? snap.ProcessId.ToString(c);
            FpsText = snap.Fps.ToString("0", c);
            FrameTimeText = string.Format(c, "{0:0.0} ms", snap.FrameTimeMs);
            Bottleneck = _live.Add(sample);
            BottleneckText = Loc.T(BottleneckAnalyzer.Key(Bottleneck));
            BottleneckAdvice = Loc.T(BottleneckAnalyzer.Key(Bottleneck) + ".advice");
            if (IsRecording) _sessionAnalyzer?.Add(sample);

            _window?.KeepOnTop();
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private static string Fmt(double? v, string format, string unit)
        => v is { } x ? x.ToString(format, Loc.Culture) + unit : "—";

    // ------------------------------------------------------------------ sessão

    /// <summary>Começa a gravar o jogo que está entregando quadros agora.</summary>
    public bool StartRecording()
    {
        double now = _fps.NowMs;
        var fg = ForegroundWindow.Current();
        int? pid = fg is { } f && f.Pid != _selfPid && _fps.Counter.Snapshot(f.Pid, now) != null ? f.Pid : _fps.Counter.Busiest(now);
        if (pid == null || pid == _selfPid) return false;
        _fps.Counter.StartSession(pid.Value);
        _sessionAnalyzer = new BottleneckAnalyzer(windowSeconds: 5);
        _sessionStart = DateTime.Now;
        _sessionApp = ForegroundWindow.ProcessName(pid.Value) ?? pid.Value.ToString(Loc.Culture);
        IsRecording = true;
        HasSummary = false;
        return true;
    }

    public void StopRecording()
    {
        if (!IsRecording) return;
        IsRecording = false;
        var session = _fps.Counter.StopSession();
        var analyzer = _sessionAnalyzer;
        _sessionAnalyzer = null;
        UpdateRunning();
        if (session == null || session.Frames < 10) return;

        var c = Loc.Culture;
        SummaryTitle = Loc.T("monitor.summary.title", _sessionApp, Formats.Time(DateTime.Now - _sessionStart));
        SummaryText = Loc.T("monitor.summary.fps", session.AverageFps.ToString("0", c), session.Low1Fps.ToString("0", c),
                            session.Frames.ToString("N0", c));
        var dominant = analyzer?.Dominant ?? Bottleneck.Unknown;
        SummaryVerdict = Loc.T(BottleneckAnalyzer.Key(dominant));
        SummaryAdvice = Loc.T(BottleneckAnalyzer.Key(dominant) + ".advice");
        SummaryShares.Clear();
        if (analyzer != null)
            foreach (var (b, share) in analyzer.Shares().OrderByDescending(kv => kv.Value))
                SummaryShares.Add(new ShareRow(Loc.T(BottleneckAnalyzer.Key(b)), share * 100, (share * 100).ToString("0", c) + "%"));
        HasSummary = true;
    }

    public void Shutdown()
    {
        _timer.Stop();
        _window?.Close();
        _window = null;
        _fps.Dispose();
    }
}
