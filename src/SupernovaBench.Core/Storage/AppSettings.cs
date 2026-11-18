namespace SupernovaBench.Core.Storage;

/// <summary>Preferências do usuário, salvas em settings.json.</summary>
public sealed class AppSettings
{
    public string Language { get; set; } = "pt-BR";
    public string Theme { get; set; } = "cosmic";
    public int SensorIntervalMs { get; set; } = 1000;

    /// <summary>Baixar capas de jogos do CDN da Steam (o nome do jogo vai para a busca da loja Steam).</summary>
    public bool DownloadCovers { get; set; } = true;

    /// <summary>Overlay de FPS por cima dos jogos (liga/desliga também com Ctrl+Shift+O).</summary>
    public bool OverlayEnabled { get; set; }
    public string OverlayCorner { get; set; } = "topleft";
    public double OverlayOpacity { get; set; } = 0.85;
    public bool OverlayShowSensors { get; set; } = true;
    public bool OverlayShowBottleneck { get; set; } = true;

    public static AppSettings Load(string? path = null)
    {
        var s = JsonFile.Load<AppSettings>(path ?? AppPaths.SettingsFile) ?? new AppSettings();
        s.SensorIntervalMs = Math.Clamp(s.SensorIntervalMs, 250, 10_000);
        return s;
    }

    public void Save(string? path = null) => JsonFile.Save(path ?? AppPaths.SettingsFile, this);
}
