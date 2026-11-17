namespace SupernovaBench.App.Services;

public static class Formats
{
    public static string Time(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}
