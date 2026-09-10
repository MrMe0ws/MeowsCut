using System.Windows;
using MeowsCut.App.ViewModels;

namespace MeowsCut.App.Views.Dialogs;

/// <summary>
/// Финальный шаг: пресеты площадок и параметры вывода.
/// </summary>
/// <remarks>
/// Отдельным окном, а не колонкой в главном: за монтажом эти настройки не нужны,
/// а места занимали треть экрана. Модели живут в контейнере и переживают закрытие —
/// вернувшись, пользователь застаёт свои настройки и ход экспорта на месте.
/// </remarks>
public partial class ExportWindow : Window
{
    public ExportWindow(PresetsViewModel presets, ExportViewModel export)
    {
        InitializeComponent();

        Presets.DataContext = presets;
        Export.DataContext = export;
    }
}
