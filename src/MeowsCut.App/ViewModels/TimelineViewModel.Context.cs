using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.ViewModels;

/// <summary>Что оказалось под правой кнопкой мыши на доске.</summary>
public enum ContextTargetKind
{
    None = 0,

    /// <summary>Кусок видеоряда: видео или фото.</summary>
    Clip,

    /// <summary>Кусок звука на дорожке.</summary>
    Audio,

    /// <summary>Надпись над видеорядом.</summary>
    Title,

    /// <summary>Пустое место звуковой дорожки — меню самой дорожки.</summary>
    Track
}

/// <summary>
/// Цель меню по правой кнопке: что выбрано и где оно на доске по времени.
/// </summary>
/// <param name="Start">Начало элемента на доске — к нему пристраивается чип параметра.</param>
/// <param name="End">Конец элемента на доске.</param>
/// <param name="Time">Где именно щёлкнули — туда режет пункт «Разрезать здесь».</param>
public readonly record struct ContextTarget(ContextTargetKind Kind, TimeSpan Start, TimeSpan End, AudioTrackId TrackId, TimeSpan Time = default);

public sealed partial class TimelineViewModel
{
    /// <summary>
    /// Готовит доску к меню по правой кнопке: выделяет то, что под курсором,
    /// чтобы выбранный в меню параметр правил именно этот элемент.
    /// </summary>
    /// <remarks>
    /// Уже выделенный клип остаётся в выделении вместе с соседями: правый клик
    /// по одному из пяти выделенных не должен разбирать выделение — правка
    /// скорости из меню пойдёт на все пять, как и из панели свойств.
    /// </remarks>
    public ContextTarget PrepareContext(double x, double y)
    {
        if (_history is null)
        {
            return default;
        }

        var hit = _hitTester.Test(x, y, Sequence, Metrics);

        if (hit.Title is { } title)
        {
            SelectTitle(title);
            return new ContextTarget(ContextTargetKind.Title, title.TimelineStart, title.TimelineEnd, default, hit.Time);
        }

        if (hit.IsAudio)
        {
            SelectAudio(hit.TrackId, hit.AudioClip);

            return hit.AudioClip is { } audio
                ? new ContextTarget(ContextTargetKind.Audio, audio.TimelineStart, audio.TimelineEnd, hit.TrackId, hit.Time)
                : new ContextTarget(ContextTargetKind.Track, hit.Time, hit.Time, hit.TrackId, hit.Time);
        }

        if (hit.Clip is { } placed)
        {
            if (!IsSelected(placed))
            {
                Select(placed);
            }

            return new ContextTarget(ContextTargetKind.Clip, placed.Start, placed.End, default, hit.Time);
        }

        return default;
    }

    /// <summary>Разрезает кусок под курсором мыши — пункт «Разрезать здесь» в меню.</summary>
    public void SplitAt(ContextTarget target)
    {
        if (_history is null)
        {
            return;
        }

        switch (target.Kind)
        {
            case ContextTargetKind.Clip:
                Execute(new SplitClipCommand(target.Time));
                break;
            case ContextTargetKind.Audio:
                Execute(new SplitAudioClipCommand(target.TrackId, target.Time));
                break;
            default:
                return;
        }

        _history.EndMergeGroup();
    }
}
