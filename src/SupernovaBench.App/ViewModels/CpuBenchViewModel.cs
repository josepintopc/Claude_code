using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupernovaBench.App.Services;
using SupernovaBench.Core.Benchmarks;
using SupernovaBench.Core.Benchmarks.Cpu;
using SupernovaBench.Core.Localization;

namespace SupernovaBench.App.ViewModels;

/// <summary>Retângulo de um bloco sendo renderizado (desenhado por cima da imagem, como no Cinebench).</summary>
public sealed partial class BucketMarker : ObservableObject
{
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _w;
    [ObservableProperty] private double _h;
    [ObservableProperty] private bool _isVisible;
}

public sealed record RankingRow(int Position, string Label, string Detail, string ScoreText, double Percent, bool IsCurrent, bool IsImported);

/// <summary>Benchmark de CPU estilo Cinebench: imagem progressiva, blocos ativos, pontuação e ranking.</summary>
public sealed partial class CpuBenchViewModel : LocalizedViewModel
{
    private readonly CpuRenderBench _renderer = new(RenderPreset.Standard);
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private CancellationTokenSource? _cts;
    private BenchmarkResult? _lastResult;
    private Guid? _highlight;

    public CpuBenchViewModel()
    {
        Bitmap = new WriteableBitmap(_renderer.Width, _renderer.Height, 96, 96, PixelFormats.Bgra32, null);
        var bg = unchecked((int)0xFF0B0A14);
        Array.Fill(_renderer.Pixels, bg);
        CopyPixels();
        for (int i = 0; i < Environment.ProcessorCount; i++) Markers.Add(new BucketMarker());

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(120) };
        _timer.Tick += (_, _) => Tick();

        AppServices.History.Changed += (_, _) => Application.Current?.Dispatcher.InvokeAsync(RefreshRanking);
        StatusText = Loc.T("cpu.ready");
        RefreshRanking();
    }

    public SensorService Sensors => AppServices.Sensors;
    public WriteableBitmap Bitmap { get; }
    public int ImageWidth => _renderer.Width;
    public int ImageHeight => _renderer.Height;
    public ObservableCollection<BucketMarker> Markers { get; } = new();
    public ObservableCollection<RankingRow> Ranking { get; } = new();

    public string ResolutionText => Loc.T("cpu.resolution", _renderer.Width, _renderer.Height, _renderer.Preset.SamplesPerPixel);
    public string CpuName => SupernovaBench.Core.MachineInfo.CpuName();

    // ------------------------------------------------------------------ modo

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMulti), nameof(IsSingle), nameof(IsSustained), nameof(ModeDescription), nameof(ThreadsText))]
    private CpuBenchMode _mode = CpuBenchMode.MultiCore;

    partial void OnModeChanged(CpuBenchMode value)
    {
        _highlight = null;
        RefreshRanking();
    }

    public bool IsMulti { get => Mode == CpuBenchMode.MultiCore; set { if (value) Mode = CpuBenchMode.MultiCore; } }
    public bool IsSingle { get => Mode == CpuBenchMode.SingleCore; set { if (value) Mode = CpuBenchMode.SingleCore; } }
    public bool IsSustained { get => Mode == CpuBenchMode.Sustained; set { if (value) Mode = CpuBenchMode.Sustained; } }

    public string ModeDescription => Mode switch
    {
        CpuBenchMode.SingleCore => Loc.T("cpu.mode.single.desc"),
        CpuBenchMode.Sustained => Loc.T("cpu.mode.sustained.desc"),
        _ => Loc.T("cpu.mode.multi.desc", Environment.ProcessorCount),
    };

    public string ThreadsText => CpuBenchmark.ThreadsFor(Mode).ToString(Loc.Culture);

    // ------------------------------------------------------------------ estado

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isRunning;

    public bool IsIdle => !IsRunning;

    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _elapsedText = "0:00";
    [ObservableProperty] private string _rateText = "—";
    [ObservableProperty] private string _passesText = "—";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _scoreText = "—";
    [ObservableProperty] private bool _hasScore;
    [ObservableProperty] private bool _isNewBest;

    private bool CanStart() => !IsRunning;
    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var mode = Mode;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsRunning = true;
        IsNewBest = false;
        HasScore = false;
        ScoreText = "—";
        Progress = 0;
        StatusText = Loc.T("cpu.running");
        _clock.Restart();
        _timer.Start();
        PowerRequest.KeepAwake();

        try
        {
            var previousBest = AppServices.History.Ranking(CpuBenchmark.TestId(mode), _renderer.Preset.Id, CpuRenderBench.EngineVersion)
                                                  .Where(r => !r.Imported).Select(r => r.Score).DefaultIfEmpty(0).Max();

            var result = await Task.Run(() => CpuBenchmark.Run(_renderer, mode, ct));
            _clock.Stop();
            Tick();

            if (result == null)
            {
                StatusText = Loc.T("cpu.cancelled");
                return;
            }

            AppServices.History.Add(result);
            _lastResult = result;
            _highlight = result.Id;
            ScoreText = result.Score.ToString("N0", Loc.Culture);
            HasScore = true;
            IsNewBest = previousBest > 0 && result.Score > previousBest;
            StatusText = Loc.T("cpu.done", FormatTime(TimeSpan.FromSeconds(result.Seconds)));
            Progress = 100;
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
            PowerRequest.Release();
            foreach (var m in Markers) m.IsVisible = false;
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _cts?.Cancel();

    // ------------------------------------------------------------------ atualização da tela

    private void Tick()
    {
        CopyPixels();

        var active = _renderer.Active;
        for (int i = 0; i < Markers.Count; i++)
        {
            var m = Markers[i];
            int b = i < active.Length ? Volatile.Read(ref active[i]) : -1;
            if (b < 0 || !IsRunning)
            {
                m.IsVisible = false;
                continue;
            }
            var bucket = _renderer.Buckets[b];
            m.X = bucket.X;
            m.Y = bucket.Y;
            m.W = bucket.W;
            m.H = bucket.H;
            m.IsVisible = true;
        }

        var elapsed = _clock.Elapsed;
        int total = _renderer.Buckets.Count;
        double passFraction = (double)_renderer.BucketsDone / total;
        Progress = Mode == CpuBenchMode.Sustained && IsRunning
            ? Math.Min(99.9, 100 * elapsed.TotalSeconds / CpuBenchmark.SustainedDuration.TotalSeconds)
            : 100 * passFraction;
        ElapsedText = FormatTime(elapsed);
        long samples = _renderer.SamplesDone;
        RateText = elapsed.TotalSeconds > 0.2 ? FormatRate(samples / elapsed.TotalSeconds) : "—";
        PassesText = Mode == CpuBenchMode.Sustained ? _renderer.PassesDone.ToString(Loc.Culture) : "—";
    }

    private void CopyPixels()
        => Bitmap.WritePixels(new Int32Rect(0, 0, _renderer.Width, _renderer.Height), _renderer.Pixels, _renderer.Width * 4, 0);

    private static string FormatTime(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private static string FormatRate(double perSecond) => perSecond switch
    {
        >= 1e6 => (perSecond / 1e6).ToString("0.0", Loc.Culture) + " M",
        >= 1e3 => (perSecond / 1e3).ToString("0.0", Loc.Culture) + " k",
        _ => perSecond.ToString("0", Loc.Culture),
    };

    // ------------------------------------------------------------------ ranking

    private void RefreshRanking()
    {
        Ranking.Clear();
        var list = AppServices.History.Ranking(CpuBenchmark.TestId(Mode), _renderer.Preset.Id, CpuRenderBench.EngineVersion);
        if (list.Count == 0) return;

        // Melhor resultado deste PC + todos os importados, mais o resultado que acabou de sair (destacado).
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
            string detail = r.Imported
                ? $"{Loc.T("cpu.ranking.imported")} · {r.Threads} threads"
                : $"{Loc.T("cpu.ranking.thisPc")} · {r.Threads} threads · {r.Timestamp.ToString("d", Loc.Culture)}";
            Ranking.Add(new RankingRow(pos++, r.CpuName, detail, r.Score.ToString("N0", Loc.Culture),
                                       max > 0 ? 100 * r.Score / max : 0, r.Id == _highlight, r.Imported));
        }
    }

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        RefreshRanking();
        if (!IsRunning)
        {
            StatusText = _lastResult != null ? Loc.T("cpu.done", FormatTime(TimeSpan.FromSeconds(_lastResult.Seconds))) : Loc.T("cpu.ready");
            if (_lastResult != null) ScoreText = _lastResult.Score.ToString("N0", Loc.Culture);
        }
    }
}
