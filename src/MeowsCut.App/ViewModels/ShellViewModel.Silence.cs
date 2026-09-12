using CommunityToolkit.Mvvm.Input;
using MeowsCut.App.Formatting;
using MeowsCut.App.Localization;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Media;
using Microsoft.Extensions.Logging;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Убрать паузы: прослушать файлы на доске и выкинуть из видеоряда тишину.
/// </summary>
/// <remarks>
/// Самая долгая из правок: часовой ролик надо честно прослушать. Поэтому она
/// живёт здесь, рядом с индикатором ожидания и разбором ошибок, а не в модели
/// доски — та не знает ни про ffmpeg, ни про то, как показать ожидание.
///
/// Результат приходит на доску одной правкой: сколько бы пауз ни вырезали,
/// Ctrl+Z возвращает монтаж целиком, а не по кусочку.
/// </remarks>
public sealed partial class ShellViewModel
{
    private SilenceOptions _silenceOptions = SilenceOptions.Default;

    [RelayCommand(CanExecute = nameof(CanRemoveSilence))]
    private async Task RemoveSilenceAsync(CancellationToken cancellationToken)
    {
        if (Project is not { } project)
        {
            return;
        }

        if (_dialogService.AskSilenceOptions(_silenceOptions) is not { } options)
        {
            return;
        }

        _silenceOptions = options;

        BusyText = Strings.BusySilenceSearch;
        IsBusy = true;
        try
        {
            var silence = await DetectSilenceAsync(project, options, cancellationToken).ConfigureAwait(true);
            var result = SilenceTrimmer.Trim(project.Sequence, silence, options);

            if (result.IsEmpty)
            {
                ToolsetStatus = Strings.SilenceNothingFound;
                return;
            }

            Timeline.ApplySequence(result.Sequence, Strings.RemoveSilence);

            ToolsetStatus = string.Format(
                Strings.SilenceRemoved,
                result.RemovedCount,
                DisplayFormat.Duration(result.RemovedDuration));
        }
        catch (OperationCanceledException)
        {
            // Отмена — не ошибка.
        }
        catch (MeowsCutException ex)
        {
            _logger.LogWarning(ex, "Не удалось убрать паузы");
            _dialogService.ShowError(_errorPresenter.Present(ex));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Прослушивает каждый файл, попавший на видеоряд со звуком.
    /// </summary>
    /// <remarks>
    /// По файлам, а не по клипам: один файл может лежать на доске десятком
    /// кусков, и слушать его десять раз было бы тратой минут на пустом месте.
    /// </remarks>
    private async Task<IReadOnlyDictionary<SourceId, IReadOnlyList<TimeRange>>> DetectSilenceAsync(
        Project project,
        SilenceOptions options,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<SourceId, IReadOnlyList<TimeRange>>();

        foreach (var clip in project.Sequence.Video.Clips)
        {
            if (!clip.HasAudio || result.ContainsKey(clip.SourceId))
            {
                continue;
            }

            if (project.Find(clip.SourceId) is not { } source)
            {
                continue;
            }

            result[clip.SourceId] = await _silenceDetector
                .DetectAsync(source.FilePath, options, cancellationToken)
                .ConfigureAwait(true);
        }

        return result;
    }

    private bool CanRemoveSilence() =>
        CanStartWork && Project is { } project && project.Sequence.HasAudio;
}
