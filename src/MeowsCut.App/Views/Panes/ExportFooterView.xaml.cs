using System.Windows;
using System.Windows.Controls;

namespace MeowsCut.App.Views.Panes;

/// <summary>Низ окна экспорта: проверка фрагмента, ход экспорта и готовый файл.</summary>
public partial class ExportFooterView : UserControl
{
    public ExportFooterView() => InitializeComponent();

    private void OnClose(object sender, RoutedEventArgs e) => Window.GetWindow(this)?.Close();
}
