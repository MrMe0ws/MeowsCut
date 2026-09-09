using System.Windows.Controls;

namespace MeowsCut.App.Views.Panes;

/// <summary>
/// Панель экспорта. DataContext приходит из ShellWindow, логика — в ExportViewModel.
/// </summary>
public partial class ExportPanelView : UserControl
{
    public ExportPanelView() => InitializeComponent();
}
