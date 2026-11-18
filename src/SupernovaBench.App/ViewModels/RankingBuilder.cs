using System.Collections.ObjectModel;
using SupernovaBench.Core.Benchmarks;
using SupernovaBench.Core.Localization;

namespace SupernovaBench.App.ViewModels;

/// <summary>Monta as linhas do ranking: o melhor resultado deste PC, os importados e o resultado recém-saído (destacado).</summary>
public static class RankingBuilder
{
    public static void Fill(ObservableCollection<RankingRow> target, IReadOnlyList<BenchmarkResult> list, Guid? highlight,
                            Func<BenchmarkResult, string> extra, bool bestPerDevice = false)
    {
        target.Clear();
        if (list.Count == 0) return;

        var own = list.Where(r => !r.Imported).ToList();
        var shown = new List<BenchmarkResult>();
        // No disco, cada unidade testada aparece (com o seu melhor resultado).
        if (bestPerDevice) shown.AddRange(own.GroupBy(r => r.DeviceName).Select(g => g.First()));
        else if (own.Count > 0) shown.Add(own[0]);
        shown.AddRange(list.Where(r => r.Imported));
        if (highlight is { } id && list.FirstOrDefault(r => r.Id == id) is { } current && !shown.Contains(current)) shown.Add(current);
        shown = shown.OrderByDescending(r => r.Score).Take(15).ToList();

        double max = shown.Max(r => r.Score);
        int pos = 1;
        foreach (var r in shown)
        {
            string info = extra(r);
            string detail = r.Imported
                ? $"{Loc.T("cpu.ranking.imported")} · {info}"
                : $"{Loc.T("cpu.ranking.thisPc")} · {info} · {r.Timestamp.ToString("d", Loc.Culture)}";
            target.Add(new RankingRow(pos++, r.DeviceName, detail, r.Score.ToString("N0", Loc.Culture),
                                      max > 0 ? 100 * r.Score / max : 0, r.Id == highlight, r.Imported));
        }
    }
}
