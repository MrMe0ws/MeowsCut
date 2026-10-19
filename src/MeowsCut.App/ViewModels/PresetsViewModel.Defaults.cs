using MeowsCut.Core.Editing;
using MeowsCut.Core.Presets;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Пресет по умолчанию: обычный монтаж для интернета, а не стикер Telegram.
/// </summary>
/// <remarks>
/// Какой пресет стоит заранее, решают сами JSON-файлы пресетов (поле <c>defaultFor</c>):
/// горизонтальный и квадратный монтаж получают «Для интернета · 1080p», вертикальный —
/// «Вертикальное видео». Пока пользователь не выбрал пресет сам, смена формата кадра
/// меняет и пресет; после его выбора — больше никогда.
/// </remarks>
public sealed partial class PresetsViewModel
{
    /// <summary>Пресет выбран руками — подбирать его по ориентации больше нельзя.</summary>
    private bool _userPicked;

    private bool _selectingDefault;

    private static FrameOrientation OrientationOf(Project project) =>
        FrameOrientations.Of(project.Sequence.Format.Size);

    private void SelectDefault(Project project)
    {
        if (_provider.DefaultFor(OrientationOf(project)) is not { } preset)
        {
            ApplyTemplateOnly(SelectedPreset);
            return;
        }

        _selectingDefault = true;
        try
        {
            _suppressAutoSelect = true;
            SelectedGroup = Groups.FirstOrDefault(group => group.Id == preset.GroupId) ?? SelectedGroup;
            _suppressAutoSelect = false;

            // Тот же пресет мог уже стоять — тогда событие не придёт, а шаблон
            // всё равно надо наложить на только что открытый проект.
            if (Equals(SelectedPreset, preset))
            {
                ApplyTemplateOnly(preset);
            }
            else
            {
                SelectedPreset = Presets.FirstOrDefault(item => item.Id == preset.Id) ?? preset;
            }
        }
        finally
        {
            _suppressAutoSelect = false;
            _selectingDefault = false;
        }
    }
}
