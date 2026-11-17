using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupernovaBench.App.Controls;
using SupernovaBench.Core.Localization;
using SupernovaBench.Hardware;

namespace SupernovaBench.App.Services;

/// <summary>
/// Mantém o <see cref="SensorMonitor"/> rodando: lê o hardware numa thread de fundo e aplica na thread da interface.
/// Expõe os valores principais já formatados e o histórico para o gráfico da Visão geral.
/// </summary>
public sealed partial class SensorService : ObservableObject, IDisposable
{
    public const int HistoryLength = 120;

    private readonly CancellationTokenSource _cts = new();
    private bool _started;

    public SensorMonitor Monitor { get; } = new();

    public ChartSeries CpuTempSeries { get; } = new("CPU", Color.FromRgb(0xFF, 0x7A, 0x2F), HistoryLength);
    public ChartSeries GpuTempSeries { get; } = new("GPU", Color.FromRgb(0x9B, 0x6B, 0xFF), HistoryLength);
    public IReadOnlyList<ChartSeries> TemperatureSeries { get; }

    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private int _revision;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _cpuTempText = "—";
    [ObservableProperty] private string _cpuLoadText = "—";
    [ObservableProperty] private string _cpuPowerText = "—";
    [ObservableProperty] private string _cpuClockText = "—";
    [ObservableProperty] private string _gpuTempText = "—";
    [ObservableProperty] private string _gpuLoadText = "—";
    [ObservableProperty] private string _gpuName = "";
    [ObservableProperty] private bool _hasCpuProblem;
    [ObservableProperty] private bool _canInstallPawnIO;
    [ObservableProperty] private string _problemText = "";
    [ObservableProperty] private string _installStatus = "";

    public int IntervalMs { get; set; } = 1000;
    public bool IsElevated { get; } = Elevation.IsElevated;

    public event EventHandler? Updated;

    public SensorService()
    {
        TemperatureSeries = new[] { CpuTempSeries, GpuTempSeries };
        StatusText = Loc.T("sensors.starting");
        Loc.LanguageChanged += (_, _) =>
        {
            foreach (var r in Monitor.Rows) r.Relocalize();
            RefreshTexts();
        };
    }

    /// <summary>Inicia a detecção e o laço de leitura (chamar na thread da interface).</summary>
    public async void Start()
    {
        if (_started) return;
        _started = true;
        try
        {
            await Monitor.OpenAsync();
            if (Monitor.OpenError != null)
                StatusText = Loc.T("sensors.openFailed", Monitor.OpenError.Message);

            while (!_cts.IsCancellationRequested)
            {
                var poll = await Task.Run(Monitor.Poll);
                if (_cts.IsCancellationRequested) break;
                Monitor.Apply(poll);
                if (!IsReady && Monitor.IsOpen)
                {
                    IsReady = true;
                    StatusText = "";
                }
                RefreshTexts();
                CpuTempSeries.Push(Monitor.CpuTemperature);
                GpuTempSeries.Push(Monitor.GpuTemperature);
                Revision++;
                Updated?.Invoke(this, EventArgs.Empty);
                await Task.Delay(IntervalMs, _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            App.Log(ex);
            StatusText = Loc.T("sensors.openFailed", ex.Message);
        }
    }

    private void RefreshTexts()
    {
        var c = Loc.Culture;
        string F(float? v, string fmt) => v is { } x ? string.Format(c, fmt, x) : "—";
        CpuTempText = F(Monitor.CpuTemperature, "{0:0} °C");
        CpuLoadText = F(Monitor.CpuLoad, "{0:0} %");
        CpuPowerText = F(Monitor.CpuPower, "{0:0} W");
        CpuClockText = F(Monitor.CpuClock, "{0:0} MHz");
        GpuTempText = F(Monitor.GpuTemperature, "{0:0} °C");
        GpuLoadText = F(Monitor.GpuLoad, "{0:0} %");
        GpuName = Monitor.GpuName ?? "";

        if (!IsReady)
        {
            HasCpuProblem = false;
            return;
        }
        var problem = Monitor.DiagnoseCpuSensors();
        HasCpuProblem = problem != SensorMonitor.CpuSensorProblem.None;
        CanInstallPawnIO = problem == SensorMonitor.CpuSensorProblem.PawnIOMissing;
        ProblemText = problem switch
        {
            SensorMonitor.CpuSensorProblem.NotElevated => Loc.T("sensors.problem.notElevated"),
            SensorMonitor.CpuSensorProblem.PawnIOMissing => Loc.T("sensors.problem.pawnio"),
            SensorMonitor.CpuSensorProblem.ReadFailed => Loc.T("sensors.problem.readFailed"),
            _ => "",
        };
    }

    [RelayCommand]
    private async Task InstallPawnIOAsync()
    {
        InstallStatus = Loc.T("sensors.installing");
        try
        {
            int code = await PawnIO.InstallWithWingetAsync();
            InstallStatus = code == 0 ? Loc.T("sensors.installDone") : Loc.T("sensors.installFailed", $"winget {code}");
        }
        catch (Exception ex)
        {
            InstallStatus = Loc.T("sensors.installFailed", ex.Message);
        }
    }

    [RelayCommand]
    private void OpenPawnIOSite() => Shell.Open(PawnIO.Website);

    [RelayCommand]
    private void ResetMinMax() => Monitor.RequestResetMinMax();

    public void Dispose()
    {
        _cts.Cancel();
        Monitor.Dispose();
    }
}
