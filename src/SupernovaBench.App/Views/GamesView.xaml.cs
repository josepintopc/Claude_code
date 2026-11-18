using System.Windows;
using System.Windows.Controls;
using SupernovaBench.App.ViewModels;

namespace SupernovaBench.App.Views;

public partial class GamesView : UserControl
{
    /// <summary>Largura de um cartão (150) + margem (16).</summary>
    private const double CardWidth = 166;

    public GamesView() => InitializeComponent();

    /// <summary>Informa ao ViewModel quantas capas cabem por linha (a grade é virtualizada por linhas).</summary>
    private void Grid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is GamesViewModel vm && e.NewSize.Width > 0)
            vm.SetColumns((int)((e.NewSize.Width - 18) / CardWidth));
    }
}
