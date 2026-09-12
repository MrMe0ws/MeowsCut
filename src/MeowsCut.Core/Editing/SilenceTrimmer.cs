using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Media;

namespace MeowsCut.Core.Editing;

/// <summary>Что получилось убрать: новая раскладка и чего она стоила.</summary>
public sealed record SilenceTrimResult(Sequence Sequence, int RemovedCount, TimeSpan RemovedDuration)
{
    public bool IsEmpty => RemovedCount == 0;
}

/// <summary>
/// Выкидывает паузы из видеоряда.
/// </summary>
/// <remarks>
/// Работает над готовой разметкой, а не над файлами: тишину ищет слой ffmpeg
/// и отдаёт отрезки во времени каждого файла, а здесь они превращаются в куски
/// ленты. Так вся арифметика остаётся проверяемой без единого запуска ffmpeg —
/// а арифметика тут злая: клип уже обрезан с обеих сторон, может идти на другой
/// скорости, и одна пауза файла попадает в несколько кусков ленты.
///
/// Куски со звуком выключенным или отсутствующим не трогаем: тишина в них —
/// не пауза в речи, а свойство самого куска, и выкидывать его целиком было бы
/// неожиданностью.
/// </remarks>
public static class SilenceTrimmer
{
    /// <summary>Короче этого остаток клипа не имеет смысла — режем по-другому.</summary>
    private static readonly TimeSpan MinKeep = Clip.MinSourceDuration;

    /// <param name="silenceBySource">Отрезки тишины во времени каждого файла.</param>
    public static SilenceTrimResult Trim(
        Sequence sequence,
        IReadOnlyDictionary<SourceId, IReadOnlyList<TimeRange>> silenceBySource,
        SilenceOptions options)
    {
        var clips = new List<Clip>();
        var removedCount = 0;
        var removedDuration = TimeSpan.Zero;

        foreach (var clip in sequence.Video.Clips)
        {
            if (!clip.HasAudio || !silenceBySource.TryGetValue(clip.SourceId, out var silence))
            {
                clips.Add(clip);
                continue;
            }

            var pieces = Split(clip, silence, options, ref removedCount, ref removedDuration);
            clips.AddRange(pieces);
        }

        if (removedCount == 0)
        {
            return new SilenceTrimResult(sequence, 0, TimeSpan.Zero);
        }

        return new SilenceTrimResult(
            sequence.WithTrack(new VideoTrack(clips)),
            removedCount,
            removedDuration);
    }

    /// <summary>
    /// Разбивает клип на куски, выбрасывая попавшие в него паузы.
    /// </summary>
    /// <remarks>
    /// Отступ по краям паузы возвращает дыхание: без него фразы склеиваются
    /// встык и звучат как обрыв. Отступ съедает саму паузу, а не речь вокруг.
    /// </remarks>
    private static IEnumerable<Clip> Split(
        Clip clip,
        IReadOnlyList<TimeRange> silence,
        SilenceOptions options,
        ref int removedCount,
        ref TimeSpan removedDuration)
    {
        var pieces = new List<Clip>();
        var cursor = clip.SourceRange.Start;
        var first = true;

        foreach (var gap in silence.OrderBy(range => range.Start))
        {
            var start = gap.Start + options.Padding;
            var end = gap.End - options.Padding;

            // Отступы съели паузу целиком — значит, она слишком коротка,
            // чтобы из неё вообще что-то вырезать.
            if (end - start < options.EffectiveMinDuration)
            {
                continue;
            }

            // Обрезаем паузу границами самого клипа: файл длиннее того, что взяли.
            if (start < cursor)
            {
                start = cursor;
            }

            if (end > clip.SourceRange.End)
            {
                end = clip.SourceRange.End;
            }

            if (end - start < options.EffectiveMinDuration || start >= clip.SourceRange.End)
            {
                continue;
            }

            var keep = start - cursor;

            if (keep >= MinKeep)
            {
                pieces.Add(Piece(clip, cursor, start, first));
                first = false;
            }
            else if (keep > TimeSpan.Zero)
            {
                // Огрызок перед паузой прирастает к ней: отдельным клипом
                // в несколько миллисекунд он только мусорил бы на доске.
                removedDuration += Scaled(keep, clip.Speed);
            }

            removedCount++;
            removedDuration += Scaled(end - start, clip.Speed);

            cursor = end;
        }

        if (clip.SourceRange.End - cursor >= MinKeep)
        {
            pieces.Add(Piece(clip, cursor, clip.SourceRange.End, first));
        }
        else if (pieces.Count == 0)
        {
            // Вырезать оказалось нечего или остался один огрызок — оставляем
            // клип как был: пустое место на доске хуже лишней паузы.
            return [clip];
        }

        return pieces;
    }

    /// <summary>
    /// Кусок клипа от одной точки исходника до другой.
    /// </summary>
    /// <remarks>
    /// Отступ перед клипом достаётся только первому куску: остальные встают
    /// вплотную, иначе на месте каждой вырезанной паузы появился бы зазор —
    /// ровно то, от чего избавлялись.
    /// </remarks>
    private static Clip Piece(Clip clip, TimeSpan from, TimeSpan to, bool first) =>
        clip with
        {
            Id = ClipId.New(),
            SourceRange = new TimeRange(from, to),
            LeadingGap = first ? clip.LeadingGap : TimeSpan.Zero
        };

    /// <summary>Время исходника во время ленты: ускоренный клип теряет меньше.</summary>
    private static TimeSpan Scaled(TimeSpan sourceTime, double speed) =>
        speed > 0 ? TimeSpan.FromTicks((long)(sourceTime.Ticks / speed)) : sourceTime;
}
