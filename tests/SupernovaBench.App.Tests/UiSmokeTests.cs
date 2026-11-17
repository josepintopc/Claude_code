using System.IO;
using System.Windows;
using System.Windows.Threading;
using SupernovaBench.App;
using SupernovaBench.App.Services;
using SupernovaBench.App.ViewModels;
using SupernovaBench.Core.Localization;
using SupernovaBench.Core.Storage;

namespace SupernovaBench.App.Tests;

/// <summary>
/// Abre a janela de verdade (WPF) e passa por todas as páginas, nos dois temas e nos dois idiomas.
/// Pega erros que só aparecem em tempo de execução: recurso com tipo errado, template quebrado, binding que lança exceção.
/// </summary>
public class UiSmokeTests
{
    [Fact]
    public void Every_page_loads_in_both_themes_and_languages()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { Run(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(5)), "a interface não terminou de carregar");
        if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    private static void Run()
    {
        AppPaths.OverrideRoot(Path.Combine(Path.GetTempPath(), "supernova-ui-tests-" + Guid.NewGuid().ToString("N")));
        var app = new App();
        app.InitializeComponent();
        AppServices.Init();

        var window = new MainWindow
        {
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
        };
        window.Show();
        Pump();

        var vm = (MainViewModel)window.DataContext;
        var visited = new List<string>();
        foreach (var theme in new[] { ThemeManager.Cosmic, ThemeManager.Light })
        {
            ThemeManager.Apply(theme);
            foreach (var lang in new[] { "pt-BR", "en" })
            {
                Loc.SetLanguage(lang);
                foreach (var item in vm.Groups.SelectMany(g => g.Items))
                {
                    vm.Navigate(item.Key);
                    Pump();
                    Assert.NotNull(vm.CurrentPage);
                    Assert.Same(item, vm.CurrentItem);
                    visited.Add($"{theme}/{lang}/{item.Key}");
                }
            }
        }
        Assert.True(visited.Count >= 4 * 14, $"páginas visitadas: {visited.Count}");

        window.Close();
        AppServices.Shutdown();
    }

    /// <summary>Processa a fila do WPF (layout, bindings, carregamentos) até ficar ociosa.</summary>
    private static void Pump()
    {
        for (int i = 0; i < 3; i++)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }
}
