using System.Globalization;
using System.IO;
using SupernovaBench.App.ViewModels;
using SupernovaBench.Core.Localization;
using SupernovaBench.Core.Storage;

namespace SupernovaBench.App.Services;

/// <summary>Serviços únicos do app (configurações, histórico, sensores, informações do sistema).</summary>
public static class AppServices
{
    public static AppSettings Settings { get; private set; } = new();
    public static ResultHistory History { get; private set; } = null!;
    public static SensorService Sensors { get; private set; } = null!;
    public static SystemInfoService SystemInfo { get; } = new();
    public static GpuService Gpu { get; } = new();
    public static MainViewModel? Main { get; set; }

    /// <summary>Só lê as preferências e aplica o idioma (usado quando o app já está aberto).</summary>
    public static void LoadSettingsOnly()
    {
        bool firstRun = !File.Exists(AppPaths.SettingsFile);
        Settings = AppSettings.Load();
        if (firstRun)
            Settings.Language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt" ? "pt-BR" : "en";
        Loc.SetLanguage(Settings.Language);
    }

    public static void Init()
    {
        LoadSettingsOnly();
        TrySaveSettings();
        ThemeManager.Apply(Settings.Theme);
        History = new ResultHistory();
        Sensors = new SensorService { IntervalMs = Settings.SensorIntervalMs };
    }

    public static void TrySaveSettings()
    {
        try { Settings.Save(); }
        catch (Exception ex) { App.Log(ex); }
    }

    public static void Shutdown()
    {
        Sensors?.Dispose();
    }
}
