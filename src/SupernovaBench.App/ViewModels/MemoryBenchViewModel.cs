using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupernovaBench.App.Services;
using SupernovaBench.Core;
using SupernovaBench.Core.Benchmarks;
using SupernovaBench.Core.Benchmarks.Memory;
using SupernovaBench.Core.Info;
using SupernovaBench.Core.Localization;

namespace SupernovaBench.App.ViewModels;

/// <summary>Uma célula da tabela (valor de um nível/operação).</summary>
public sealed partial class MemCell : ObservableObject
{
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _isActive;
    public double? Value { get; set; }
}

/// <summary>Uma linha da tabela: Leitura, Gravação, Cópia ou Latência, com as colunas Memória, L1, L2 e L3.</summary>
public sealed class MemRow : LocalizedViewModel
{
    public MemRow(MemOp op)
    {
        Op = op;
        Cells = MemoryBench.Levels.Select(_ => new MemCell()).ToArray();
    }

    public MemOp Op { get; }
    public MemCell[] Cells { get; }
    public string Label => Loc.T("mem.op." + MemoryBench.OpId(Op));
}

/// <summary>Benchmark de cache e memória (estilo AIDA64): tabela ao vivo, pontuação e ranking.</summary>
public sealed partial class MemoryBenchViewModel : LocalizedViewModel
{
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private CancellationTokenSource? _cts;
    private BenchmarkResult? _lastResult;
    private Guid? _highlight;
    private string _ramText = "";

    public MemoryBenchViewModel()
    {
        Rows = MemoryBench.Ops.Select(op => new MemRow(op)).ToList();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => ElapsedText = Formats.Time(_clock.Elapsed);
        AppServices.History.Changed += (_, _) => Application.Current?.Dispatcher.InvokeAsync(RefreshRanking);
        StatusText = Loc.T("cpu.ready");
        RefreshRanking();
        _ = LoadAsync();
    }

    public IReadOnlyList<MemRow> Rows { get; }
    public ObservableCollection<RankingRow> Ranking { get; } = new();
    public string CpuName => MachineInfo.CpuName();

    public string CacheText
    {
        get
        {
            var c = CacheInfo.Detect();
            return $"L1 {Size(c.L1)} · L2 {Size(c.L2)} · L3 {Size(c.L3Total > 0 ? c.L3Total : c.L3)} · {Loc.T("mem.threads", Environment.ProcessorCount)}";
        }
    }

    [ObservableProperty] private string _ramName = "—";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isRunning;

    public bool IsIdle => !IsRunning;

    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _elapsedText = "0:00";
    [ObservableProperty] private string _scoreText = "—";
    [ObservableProperty] private bool _hasScore;
    [ObservableProperty] private bool _isNewBest;

    private async Task LoadAsync()
    {
        try
        {
            var summary = await AppServices.SystemInfo.GetSummaryAsync();
            _ramText = summary.Ram;
            RamName = summary.Ram;
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private bool CanStart() => !IsRunning;
    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        foreach (var row in Rows)
            foreach (var cell in row.Cells)
            {
                cell.Value = null;
                cell.Text = "";
                cell.IsActive = false;
            }

        var bench = new MemoryBench();
        int total = MemoryBench.Levels.Length * MemoryBench.Ops.Length, done = 0;
        var dispatcher = Application.Current.Dispatcher;
        bench.Started = (level, op) => dispatcher.InvokeAsync(() =>
        {
            Cell(level, op).IsActive = true;
            StatusText = Loc.T("mem.status.measuring", Loc.T("mem.op." + MemoryBench.OpId(op)), LevelName(level));
        });
        bench.Measured = (level, op, value) => dispatcher.InvokeAsync(() =>
        {
            var cell = Cell(level, op);
            cell.IsActive = false;
            cell.Value = value;
            cell.Text = Format(op, value);
            Progress = 100.0 * ++done / total;
        });

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsRunning = true;
        HasScore = false;
        IsNewBest = false;
        Progress = 0;
        _clock.Restart();
        _timer.Start();
        AppServices.Sensors.Pause();
        PowerRequest.KeepAwake();

        try
        {
            double previousBest = AppServices.History.Ranking(TestIds.MemoryBench, "standard", MemoryBench.EngineVersion)
                                                     .Where(r => !r.Imported).Select(r => r.Score).DefaultIfEmpty(0).Max();
            var r = await Task.Factory.StartNew(() => bench.Run(ct), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            _clock.Stop();
            if (r == null)
            {
                StatusText = Loc.T("cpu.cancelled");
                return;
            }

            var result = bench.ToResult(r, _ramText);
            AppServices.History.Add(result);
            _lastResult = result;
            _highlight = result.Id;
            ScoreText = result.Score.ToString("N0", Loc.Culture);
            HasScore = true;
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
            foreach (var row in Rows)
                foreach (var cell in row.Cells) cell.IsActive = false;
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _cts?.Cancel();

    public void StopNow() => _cts?.Cancel();

    private MemCell Cell(MemLevel level, MemOp op)
        => Rows[Array.IndexOf(MemoryBench.Ops, op)].Cells[Array.IndexOf(MemoryBench.Levels, level)];

    public static string LevelName(MemLevel level) => level == MemLevel.Memory ? Loc.T("mem.level.memory") : level.ToString();

    public string MemoryHeader => Loc.T("mem.level.memory");

    private static string Format(MemOp op, double v)
    {
        var c = Loc.Culture;
        if (op == MemOp.Latency) return string.Format(c, "{0:0.0} ns", v);
        return string.Format(c, v >= 100 ? "{0:N0} GB/s" : "{0:0.0} GB/s", v);
    }

    private static string Size(long bytes) => bytes >= 1 << 20 ? $"{bytes >> 20} MB" : $"{bytes >> 10} KB";

    private void RefreshRanking()
    {
        var list = AppServices.History.Ranking(TestIds.MemoryBench, "standard", MemoryBench.EngineVersion);
        RankingBuilder.Fill(Ranking, list, _highlight, r =>
        {
            var c = Loc.Culture;
            return string.Format(c, "{0:0.0} GB/s · {1:0.0} ns", r.Metric("mem.read") ?? 0, r.Metric("mem.latency") ?? 0);
        });
    }

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        foreach (var row in Rows)
            for (int i = 0; i < row.Cells.Length; i++)
                if (row.Cells[i].Value is { } v) row.Cells[i].Text = Format(row.Op, v);
        RefreshRanking();
        if (!IsRunning)
        {
            StatusText = _lastResult != null ? Loc.T("cpu.done", Formats.Time(TimeSpan.FromSeconds(_lastResult.Seconds))) : Loc.T("cpu.ready");
            if (_lastResult != null) ScoreText = _lastResult.Score.ToString("N0", Loc.Culture);
        }
    }
}
