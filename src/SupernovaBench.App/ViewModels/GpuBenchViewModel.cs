using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupernovaBench.App.Services;
using SupernovaBench.Core.Benchmarks;
using SupernovaBench.Core.Localization;
using SupernovaBench.Graphics;

namespace SupernovaBench.App.ViewModels;

/// <summary>Benchmark de GPU (cena 3D de jogo): escolha de resolução/duração, progresso, pontuação e ranking.</summary>
public sealed partial class GpuBenchViewModel : LocalizedViewModel
{
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private GameSceneBench? _bench;
    private GpuAdapter? _adapter;
    private BenchmarkResult? _lastResult;
    private Guid? _highlight;
    private string _resolutionId = GpuBenchmark.Resolutions[0].Id;
    private int _seconds = GpuBenchmark.DurationsSeconds[^1];
    private int _ticks;

    public GpuBenchViewModel()
    {
        Resolutions = GpuBenchmark.Resolutions
            .Select(r => new Option(r.Id, () => $"{r.Id.ToUpperInvariant().Replace("P", "p")} · {r.Width}×{r.Height}", r.Id == _resolutionId, o => OnResolution(o.Value)))
            .ToList();
        Durations = GpuBenchmark.DurationsSeconds
            .Select(s => new Option(s.ToString(), () => Loc.T("gpu.duration.seconds", s), s == _seconds, o => OnDuration(int.Parse(o.Value))))
            .ToList();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Tick();

        AppServices.History.Changed += (_, _) => Application.Current?.Dispatcher.InvokeAsync(RefreshRanking);
        StatusText = Loc.T("cpu.ready");
        AdapterName = Loc.T("gpu.adapter.loading");
        RefreshRanking();
        _ = LoadAdapterAsync();
    }

    public IReadOnlyList<Option> Resolutions { get; }
    public IReadOnlyList<Option> Durations { get; }
    public ObservableCollection<RankingRow> Ranking { get; } = new();
    public GpuLive Live { get; } = new();

    [ObservableProperty] private string _adapterName = "";
    [ObservableProperty] private string _adapterDetail = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _hasAdapter;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isRunning;

    public bool IsIdle => !IsRunning;

    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _elapsedText = "0:00";
    [ObservableProperty] private string _fpsNowText = "—";
    [ObservableProperty] private string _scoreText = "—";
    [ObservableProperty] private string _avgFpsText = "—";
    [ObservableProperty] private string _low1Text = "—";
    [ObservableProperty] private bool _hasScore;
    [ObservableProperty] private bool _isNewBest;

    private string PresetId => GpuBenchmark.PresetId(GpuBenchmark.ResolutionById(_resolutionId), _seconds);

    private async Task LoadAdapterAsync()
    {
        _adapter = await AppServices.Gpu.GetMainAdapterAsync();
        await AppServices.Gpu.GetTelemetryAsync();
        HasAdapter = _adapter != null;
        UpdateAdapterTexts();
        Live.Update(AppServices.Gpu.ReadSample());
    }

    private void UpdateAdapterTexts()
    {
        if (_adapter == null)
        {
            AdapterName = Loc.T("gpu.adapter.none");
            AdapterDetail = "";
            return;
        }
        AdapterName = _adapter.Name;
        double gb = _adapter.DedicatedVideoMemory / (double)(1UL << 30);
        AdapterDetail = $"{_adapter.VendorName} · {Math.Ceiling(gb - 0.15):0} GB VRAM" +
                        (_adapter.FeatureLevel != null ? $" · D3D {_adapter.FeatureLevel.Replace('_', '.')}" : "");
    }

    private void OnResolution(string id)
    {
        _resolutionId = id;
        _highlight = null;
        RefreshRanking();
    }

    private void OnDuration(int seconds)
    {
        _seconds = seconds;
        _highlight = null;
        RefreshRanking();
    }

    private bool CanStart() => !IsRunning && HasAdapter;
    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var res = GpuBenchmark.ResolutionById(_resolutionId);
        string preset = PresetId;
        var bench = new GameSceneBench
        {
            Width = res.Width,
            Height = res.Height,
            Duration = TimeSpan.FromSeconds(_seconds),
            AdapterName = _adapter?.Name ?? "",
        };
        _bench = bench;
        IsRunning = true;
        HasScore = false;
        IsNewBest = false;
        Progress = 0;
        FpsNowText = "—";
        StatusText = Loc.T("gpu.status.warmup");
        _clock.Restart();
        _ticks = 0;
        _timer.Start();
        AppServices.Sensors.Pause();
        PowerRequest.KeepAwake();

        try
        {
            double previousBest = AppServices.History.Ranking(TestIds.GpuScene, preset, GameSceneBench.EngineVersion)
                                                     .Where(r => !r.Imported).Select(r => r.Score).DefaultIfEmpty(0).Max();

            var scene = await Task.Factory.StartNew(bench.Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            _clock.Stop();

            if (scene == null)
            {
                StatusText = bench.Cancelled ? Loc.T("cpu.cancelled") : Loc.T("cpu.failed", bench.Error ?? "?");
                return;
            }

            var result = GpuBenchmark.ToResult(scene, preset);
            AppServices.History.Add(result);
            _lastResult = result;
            _highlight = result.Id;
            ShowResult(result);
            IsNewBest = previousBest > 0 && result.Score > previousBest;
            Progress = 100;
            StatusText = Loc.T("cpu.done", Formats.Time(TimeSpan.FromSeconds(result.Seconds)));
            RefreshRanking();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            StatusText = Loc.T("cpu.failed", ex.Message);
        }
        finally
        {
            _timer.Stop();
            AppServices.Sensors.Resume();
            PowerRequest.Release();
            IsRunning = false;
            _bench = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _bench?.Cancel();

    /// <summary>Cancela sem esperar (ao fechar o app).</summary>
    public void StopNow() => _bench?.Cancel();

    private void ShowResult(BenchmarkResult r)
    {
        var c = Loc.Culture;
        ScoreText = r.Score.ToString("N0", c);
        AvgFpsText = r.AvgFps?.ToString("N1", c) ?? "—";
        Low1Text = r.Low1Fps?.ToString("N1", c) ?? "—";
        HasScore = true;
    }

    private void Tick()
    {
        var bench = _bench;
        if (bench == null) return;
        Progress = Math.Min(99.9, bench.Progress * 100);
        FpsNowText = bench.CurrentFps > 0 ? bench.CurrentFps.ToString("N0", Loc.Culture) : "—";
        ElapsedText = Formats.Time(_clock.Elapsed);
        StatusText = Loc.T(bench.IsWarmingUp ? "gpu.status.warmup" : "gpu.status.measuring");
        if (++_ticks % 4 == 0) Live.Update(AppServices.Gpu.ReadSample());
    }

    private void RefreshRanking()
    {
        Ranking.Clear();
        var list = AppServices.History.Ranking(TestIds.GpuScene, PresetId, GameSceneBench.EngineVersion);
        if (list.Count == 0) return;

        var own = list.Where(r => !r.Imported).ToList();
        var shown = new List<BenchmarkResult>();
        if (own.Count > 0) shown.Add(own[0]);
        shown.AddRange(list.Where(r => r.Imported));
        if (_highlight is { } id && list.FirstOrDefault(r => r.Id == id) is { } current && !shown.Contains(current)) shown.Add(current);
        shown = shown.OrderByDescending(r => r.Score).Take(15).ToList();

        double max = shown.Max(r => r.Score);
        int pos = 1;
        foreach (var r in shown)
        {
            string fps = r.AvgFps is { } a ? $"{a.ToString("N0", Loc.Culture)} FPS" : "";
            string detail = r.Imported
                ? $"{Loc.T("cpu.ranking.imported")} · {fps}"
                : $"{Loc.T("cpu.ranking.thisPc")} · {fps} · {r.Timestamp.ToString("d", Loc.Culture)}";
            Ranking.Add(new RankingRow(pos++, r.DeviceName, detail, r.Score.ToString("N0", Loc.Culture),
                                       max > 0 ? 100 * r.Score / max : 0, r.Id == _highlight, r.Imported));
        }
    }

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        UpdateAdapterTexts();
        RefreshRanking();
        if (!IsRunning)
        {
            StatusText = _lastResult != null ? Loc.T("cpu.done", Formats.Time(TimeSpan.FromSeconds(_lastResult.Seconds))) : Loc.T("cpu.ready");
            if (_lastResult != null) ShowResult(_lastResult);
        }
    }
}
