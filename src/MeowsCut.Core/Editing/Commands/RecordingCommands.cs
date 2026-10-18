using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Media;

namespace MeowsCut.Core.Editing.Commands;

/// <summary>
/// Положить дубль озвучки на доску.
/// </summary>
/// <remarks>
/// Запись ложится на предпочтительную дорожку — ту, на которую писали прошлый дубль
/// или которую выбрал пользователь, — только если это место на ней свободно.
/// Иначе заводится новая дорожка: затереть уже уложенный звук молча было бы хуже,
/// чем лишняя полоса на доске. Обе ветки — одна правка, Ctrl+Z убирает дубль целиком.
/// </remarks>
public sealed class AddRecordingCommand(AudioClip clip, AudioTrackId? preferredTrack, AudioTrack fallbackTrack)
    : IEditCommand
{
    public string Title => "Записать озвучку";

    public AudioClip Clip { get; } = clip;

    public AudioTrackId? PreferredTrack { get; } = preferredTrack;

    /// <summary>Пустая дорожка на случай, если на предпочтительной места нет.</summary>
    public AudioTrack FallbackTrack { get; } = fallbackTrack;

    public Sequence Apply(Sequence sequence)
    {
        if (PreferredTrack is { } id &&
            sequence.FindTrack(id) is { } track &&
            track.IsFree(Clip.TimelineRange))
        {
            return sequence.WithTrack(track.Add(Clip));
        }

        if (sequence.FindTrack(FallbackTrack.Id) is not null)
        {
            throw new EditOperationException($"Аудиодорожка {FallbackTrack.Id} уже есть на доске.");
        }

        return sequence.WithTracks([.. sequence.AudioTracks, FallbackTrack with { Clips = [Clip] }]);
    }
}

/// <summary>Как файл записи превращается в кусок звука на доске.</summary>
public static class RecordingTake
{
    /// <summary>
    /// Кусок звука из записанного файла.
    /// </summary>
    /// <param name="timelineStart">Где стоял плейхед, когда пошёл ролик.</param>
    /// <param name="leadIn">
    /// Какой момент файла совпадает с <paramref name="timelineStart"/>. Микрофон
    /// включают раньше ролика, поэтому в начале файла лежит лишнее — оно отрезается
    /// обрезкой, а не удаляется: потянув левый край, его можно вернуть.
    /// Отрицательное значение значит, что звук пошёл позже ролика, и кусок
    /// встаёт на столько же правее.
    /// </param>
    /// <returns>null, если после выравнивания от записи ничего не осталось.</returns>
    public static AudioClip? Create(MediaSource source, TimeSpan timelineStart, TimeSpan leadIn)
    {
        var sourceStart = leadIn > TimeSpan.Zero ? leadIn : TimeSpan.Zero;
        var start = leadIn < TimeSpan.Zero ? timelineStart - leadIn : timelineStart;

        if (source.Duration - sourceStart < AudioClip.MinDuration)
        {
            return null;
        }

        return AudioClip.FromSource(source, start) with
        {
            SourceRange = new TimeRange(sourceStart, source.Duration)
        };
    }
}
