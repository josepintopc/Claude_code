namespace SupernovaBench.Core.Storage;

/// <summary>Preferências do usuário, salvas em settings.json.</summary>
public sealed class AppSettings
{
    public string Language { get; set; } = "pt-BR";
    public string Theme { get; set; } = "cosmic";
    public int SensorIntervalMs { get; set; } = 1000;

    public static AppSettings Load(string? path = null)
    {
        var s = JsonFile.Load<AppSettings>(path ?? AppPaths.SettingsFile) ?? new AppSettings();
        s.SensorIntervalMs = Math.Clamp(s.SensorIntervalMs, 250, 10_000);
        return s;
    }

    public void Save(string? path = null) => JsonFile.Save(path ?? AppPaths.SettingsFile, this);
}
