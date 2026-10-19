using CommunityToolkit.Mvvm.Input;
using MeowsCut.App.Localization;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// То, что нужно окну экспорта версии 2.0 поверх самих настроек:
/// переключатель «весь ролик / кусок», крупный размер файла, выбор места.
/// </summary>
public sealed partial class ExportViewModel
{
    /// <summary>
    /// Обратная сторона «Выделенного куска» — для сегмента «Весь ролик».
    /// Отдельного состояния нет: два флага с одним смыслом рано или поздно разошлись бы.
    /// </summary>
    public bool WholeSequence
    {
        get => !SelectionOnly;
        set
        {
            if (value)
            {
                SelectionOnly = false;
            }
        }
    }

    /// <summary>Примерный размер результата крупно — то, на что смотрят перед экспортом.</summary>
    public string EstimatedSizeText =>
        SummaryLines.FirstOrDefault(line => line.Label == Strings.EstimatedSize)?.Value ?? "—";

    /// <summary>Выбрать, куда записать ролик, обычным окном Windows.</summary>
    [RelayCommand]
    private void ChooseOutput()
    {
        if (_fileDialogService.PickExportTarget(OutputPath) is { } path)
        {
            OutputPath = path;
        }
    }
}
