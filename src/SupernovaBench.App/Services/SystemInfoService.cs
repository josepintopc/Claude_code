using SupernovaBench.Core.Info;
using SupernovaBench.Core.Localization;
using SupernovaBench.Graphics;
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
            _sections = Task.Run(CollectAll);
        }
        return _sections;
    }

    /// <summary>Seções do Hardware, com a aba de GPU no estilo GPU-Z (DXGI + NVML) no lugar da versão só com WMI.</summary>
    private static List<InfoSection> CollectAll()
    {
        var sections = SystemInfoCollector.CollectAll();
        try
        {
            var gpu = GpuInfoCollector.CollectSection();
            int i = sections.FindIndex(s => s.Id == "gpu");
            if (i >= 0) sections[i] = gpu;
            else sections.Add(gpu);
        }
        catch (Exception ex)
        {
            App.Log(ex); // fica a versão do WMI
        }
        return sections;
    }

    public Task<PcSummary> GetSummaryAsync()
    {
        if (_summary == null || _summary.IsFaulted)
            _summary = Task.Run(SystemInfoCollector.CollectSummary);
        return _summary;
    }
}
