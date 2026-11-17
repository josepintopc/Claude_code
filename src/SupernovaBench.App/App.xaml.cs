using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using SupernovaBench.App.Services;
using SupernovaBench.Core;
using SupernovaBench.Core.Localization;
using SupernovaBench.Core.Storage;

namespace SupernovaBench.App;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, @"Local\SupernovaBench.SingleInstance", out bool created);
        if (!created)
        {
            AppServices.LoadSettingsOnly();
            MessageBox.Show(Loc.T("app.alreadyRunning"), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log(args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
        AppServices.Init();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        AppServices.Sensors.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppServices.Shutdown();
        if (_singleInstance != null)
        {
            try { _singleInstance.ReleaseMutex(); } catch (ApplicationException) { }
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        MessageBox.Show(Loc.T("app.unhandled", e.Exception.Message, AppPaths.LogFile), AppInfo.Name,
                        MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    internal static void Log(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(DateTimeOffset.Now.ToString("O")).Append("] ").Append(AppInfo.Name).Append(' ').AppendLine(AppInfo.Version);
            sb.AppendLine(ex.ToString()).AppendLine();
            File.AppendAllText(AppPaths.LogFile, sb.ToString());
        }
        catch
        {
            // Sem como registrar: ignora.
        }
    }
}
