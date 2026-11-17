using SupernovaBench.Core.Benchmarks;
using SupernovaBench.Core.Localization;

namespace SupernovaBench.Core.Tests;

public class LocalizationTests
{
    [Fact]
    public void All_languages_have_exactly_the_same_keys()
    {
        var reference = Loc.Table(Loc.DefaultLanguage);
        Assert.NotEmpty(reference);
        foreach (var lang in Loc.Languages)
        {
            var table = Loc.Table(lang.Code);
            var missing = reference.Keys.Except(table.Keys).ToList();
            var extra = table.Keys.Except(reference.Keys).ToList();
            Assert.True(missing.Count == 0, $"{lang.Code} sem: {string.Join(", ", missing)}");
            Assert.True(extra.Count == 0, $"{lang.Code} sobrando: {string.Join(", ", extra)}");
        }
    }

    [Fact]
    public void Placeholders_match_between_languages()
    {
        var reference = Loc.Table(Loc.DefaultLanguage);
        var rx = new System.Text.RegularExpressions.Regex(@"\{(\d+)[^}]*\}");
        foreach (var lang in Loc.Languages)
        {
            var table = Loc.Table(lang.Code);
            foreach (var (key, value) in reference)
            {
                var a = rx.Matches(value).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
                var b = rx.Matches(table[key]).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
                Assert.True(a.SequenceEqual(b), $"{lang.Code}:{key} tem placeholders diferentes");
            }
        }
    }

    [Fact]
    public void Every_test_id_has_a_translated_name()
    {
        foreach (var lang in Loc.Languages)
            foreach (var id in TestIds.All)
                Assert.True(Loc.Table(lang.Code).ContainsKey(TestIds.NameKey(id)), $"{lang.Code} sem nome para {id}");
    }

    [Fact]
    public void Unknown_language_falls_back_to_portuguese_and_unknown_key_returns_key()
    {
        try
        {
            Loc.SetLanguage("xx-YY");
            Assert.Equal("pt-BR", Loc.Current);
            Assert.Equal("no.such.key", Loc.T("no.such.key"));
            Loc.SetLanguage("en");
            Assert.Equal("en", Loc.Current);
            Assert.NotEqual(Loc.Table("pt-BR")["nav.overview"], Loc.T("nav.overview"));
        }
        finally
        {
            Loc.SetLanguage("pt-BR");
        }
    }
}
