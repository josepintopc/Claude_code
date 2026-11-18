using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupernovaBench.App.Services;
using SupernovaBench.Core;
using SupernovaBench.Core.Benchmarks;
using SupernovaBench.Core.Extras;
using SupernovaBench.Core.Localization;
using SupernovaBench.Core.Storage;

namespace SupernovaBench.App.ViewModels;

public sealed record CommunityRowView(int Position, string Device, string Detail, string ScoreText, double Percent, bool IsMine);

/// <summary>Comparação online: resultados da comunidade (arquivo público no GitHub) e envio do seu resultado.</summary>
public sealed partial class CommunityViewModel : LocalizedViewModel
{
    private static readonly HttpClient Http = CreateClient();
    private List<BenchmarkResult> _community = new();
    private (string Test, string Preset, string Engine)? _selected;
    private DateTime? _updated;
    private string _statusKey = "community.status.loading";

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"SupernovaBench/{AppInfo.Version}");
        return c;
    }

    public CommunityViewModel()
    {
        AppServices.History.Changed += (_, _) => Application.Current?.Dispatcher.InvokeAsync(RebuildTests);
        LoadCache();
        _ = RefreshAsync();
    }

    public ObservableCollection<Option> Tests { get; } = new();
    public ObservableCollection<CommunityRowView> Rows { get; } = new();

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _myPositionText = "";
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _canSubmit;
    [ObservableProperty] private bool _isLoading;

    private static string CacheFile => Path.Combine(AppPaths.Root, "community.json");

    private void LoadCache()
    {
        try
        {
            if (File.Exists(CacheFile))
            {
                _community = CommunityResults.Parse(File.ReadAllText(CacheFile));
                _updated = File.GetLastWriteTime(CacheFile);
                _statusKey = "community.status.cached";
            }
        }
        catch (Exception) { /* cache corrompido: ignora */ }
        RebuildTests();
        UpdateStatus();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            using var resp = await Http.GetAsync(CommunityResults.RawUrl(AppInfo.RepositoryUrl));
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                _statusKey = "community.status.private";
                return;
            }
            resp.EnsureSuccessStatusCode();
            string json = await resp.Content.ReadAsStringAsync();
            _community = CommunityResults.Parse(json);
            await File.WriteAllTextAsync(CacheFile, json);
            _updated = DateTime.Now;
            _statusKey = "community.status.online";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _statusKey = _community.Count > 0 ? "community.status.offlineCached" : "community.status.offline";
        }
        catch (Exception ex)
        {
            App.Log(ex);
            _statusKey = "community.status.invalid";
        }
        finally
        {
            IsLoading = false;
            RebuildTests();
            UpdateStatus();
        }
    }

    private void UpdateStatus()
        => StatusText = Loc.T(_statusKey, _community.Count, _updated?.ToString("g", Loc.Culture) ?? "—");

    private IEnumerable<BenchmarkResult> Own => AppServices.History.All.Where(r => !r.Imported);

    /// <summary>Um chip por combinação de teste + preset + versão do motor (só se comparam resultados iguais).</summary>
    private void RebuildTests()
    {
        var combos = _community.Concat(Own)
                               .GroupBy(r => (r.Test, r.Preset, r.EngineVersion))
                               .Select(g => g.Key)
                               .OrderBy(k => TestIds.All.ToList().IndexOf(k.Test)).ThenBy(k => k.Preset)
                               .ToList();
        if (_selected == null || !combos.Contains(_selected.Value)) _selected = combos.Count > 0 ? combos[0] : null;
        Tests.Clear();
        foreach (var k in combos)
        {
            var key = k;
            string label = Loc.T(TestIds.NameKey(k.Test)) + (k.Preset is "standard" or "" ? "" : $" · {k.Preset}");
            Tests.Add(new Option($"{k.Test}|{k.Preset}|{k.EngineVersion}", () => label, _selected == key, _ => Select(key)));
        }
        RebuildRanking();
    }

    private void Select((string, string, string) key)
    {
        _selected = key;
        RebuildRanking();
    }

    private BenchmarkResult? MyBest()
        => _selected is { } s ? AppServices.History.Ranking(s.Test, s.Preset, s.Engine).FirstOrDefault(r => !r.Imported) : null;

    private void RebuildRanking()
    {
        Rows.Clear();
        IsEmpty = _selected == null;
        CanSubmit = false;
        MyPositionText = "";
        if (_selected is not { } s) return;

        var community = _community.Where(r => r.Test == s.Test && r.Preset == s.Preset && r.EngineVersion == s.Engine);
        var mine = MyBest();
        var rank = CommunityResults.Rank(community, mine, top: 50);
        double max = rank.Rows.Count > 0 ? rank.Rows.Max(r => r.Result.Score) : 1;
        foreach (var row in rank.Rows)
        {
            var r = row.Result;
            string detail = row.IsMine ? Loc.T("cpu.ranking.thisPc") : (r.Device != null || TestIds.IsGpu(r.Test) ? r.CpuName : "");
            if (r.AvgFps is { } fps) detail = $"{detail} · {fps.ToString("0", Loc.Culture)} FPS".TrimStart(' ', '·');
            Rows.Add(new CommunityRowView(row.Position, r.DeviceName, detail, r.Score.ToString("N0", Loc.Culture),
                                          max > 0 ? 100 * r.Score / max : 0, row.IsMine));
        }
        CanSubmit = mine != null && !_community.Any(r => r.Id == mine.Id);
        MyPositionText = rank.MyPosition is { } pos
            ? Loc.T("community.position", pos, rank.Total, rank.TopPercent!.Value.ToString("0", Loc.Culture))
            : Loc.T("community.noMine");
    }

    [RelayCommand]
    private void Submit()
    {
        if (_selected is not { } s || MyBest() is not { } mine) return;
        if (MessageBox.Show(Loc.T("community.submit.confirm", mine.DeviceName, mine.Score.ToString("N0", Loc.Culture)), AppInfo.Name,
                            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        Shell.Open(CommunityResults.IssueUrl(AppInfo.RepositoryUrl, mine, Loc.T(TestIds.NameKey(s.Test))));
        StatusText = Loc.T("community.submit.opened");
    }

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        RebuildTests();
        UpdateStatus();
    }
}
