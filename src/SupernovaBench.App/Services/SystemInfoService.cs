using SupernovaBench.Core.Info;
using SupernovaBench.Core.Localization;
using SupernovaBench.Hardware;

namespace SupernovaBench.App.Services;

/// <summary>Cache das informações de hardware (WMI é lento: coletamos uma vez por idioma).</summary>
public sealed class SystemInfoService
{
    private Task<List<InfoSection>>? _sections;
    private string? _sectionsLanguage;
    private Task<PcSummary>? _summary;

    public Task<List<InfoSection>> GetSectionsAsync(bool refresh = false)
    {
        if (refresh || _sections == null || _sectionsLanguage != Loc.Current || _sections.IsFaulted)
        {
            _sectionsLanguage = Loc.Current;
            _sections = Task.Run(SystemInfoCollector.CollectAll);
        }
        return _sections;
    }

    public Task<PcSummary> GetSummaryAsync()
    {
        if (_summary == null || _summary.IsFaulted)
            _summary = Task.Run(SystemInfoCollector.CollectSummary);
        return _summary;
    }
}
