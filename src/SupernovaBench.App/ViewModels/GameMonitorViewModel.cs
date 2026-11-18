using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupernovaBench.App.Services;
using SupernovaBench.Core;
using SupernovaBench.Core.Localization;

namespace SupernovaBench.App.ViewModels;

/// <summary>Monitor de jogo: overlay de FPS, detector de gargalo ao vivo e gravação de sessão.</summary>
public sealed partial class GameMonitorViewModel : LocalizedViewModel, IPageLifecycle
{
    public GameMonitorViewModel()
    {
        var o = Overlay;
        Corners = new[] { "topleft", "topright", "bottomleft", "bottomright" }
            .Select(k => new Option(k, () => Loc.T("monitor.corner." + k), o.OverlayCorner == k, opt => o.OverlayCorner = opt.Value))
            .ToList();
        Opacities = new[] { 0.6, 0.85, 1.0 }
            .Select(v => new Option(v.ToString(System.Globalization.CultureInfo.InvariantCulture), () => $"{v * 100:0}%",
                                    Math.Abs(o.OverlayOpacity - v) < 0.01, opt => o.OverlayOpacity = v))
            .ToList();
        o.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(OverlayService.IsRecording) or nameof(OverlayService.HasFps))
            {
                StartRecordingCommand.NotifyCanExecuteChanged();
                StopRecordingCommand.NotifyCanExecuteChanged();
            }
        };
    }

    public OverlayService Overlay => AppServices.Overlay;
    public IReadOnlyList<Option> Corners { get; }
    public IReadOnlyList<Option> Opacities { get; }

    public void Activated() => Overlay.Acquire();

    public void Deactivated() => Overlay.Release();

    private bool CanStart() => !Overlay.IsRecording && Overlay.HasFps;
    private bool CanStop() => Overlay.IsRecording;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void StartRecording()
    {
        if (!Overlay.StartRecording())
            MessageBox.Show(Loc.T("monitor.record.noGame"), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void StopRecording() => Overlay.StopRecording();
}
