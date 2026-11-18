using SupernovaBench.Core.Localization;

namespace SupernovaBench.App.Services;

public static class Formats
{
    public static string Time(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    /// <summary>Tamanho em bytes com unidade (base 1024), no idioma atual.</summary>
    public static string Bytes(double b)
    {
        if (b <= 0) return "—";
        string[] u = { "B", "KB", "MB", "GB", "TB", "PB" };
        int i = 0;
        while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
        return string.Format(Loc.Culture, "{0:0.##} {1}", b, u[i]);
    }
}
