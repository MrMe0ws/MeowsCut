using System.Globalization;
using System.Text;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Ffmpeg.Arguments;

/// <summary>
/// Подмешивает отдельные аудиодорожки к звуку видеоряда.
/// </summary>
/// <remarks>
/// Дорожки именно смешиваются, а не склеиваются: в этом весь смысл наложения
/// звука. Каждый кусок отрезается от источника, при необходимости меняет темп
/// и тональность, получает громкость и затухания и сдвигается на своё место
/// через adelay. Всё вместе идёт в amix с normalize=0 — иначе ffmpeg делит
/// громкость на число входов, и добавление второй дорожки внезапно приглушает
/// первую.
/// </remarks>
public sealed class AudioMixBuilder
{
    /// <summary>Дальше этого предела rubberband уже не звучит музыкально.</summary>
    private const int MaxSemitones = 12;

    /// <summary>Есть ли что подмешивать: пустые и выключенные дорожки не считаются.</summary>
    public static bool HasWork(Sequence sequence) => sequence.AudibleTracks.Any();

    /// <param name="baseLabel">Звук видеоряда после concat.</param>
    /// <returns>Метка смешанного звука.</returns>
    public string Append(
        StringBuilder builder,
        Sequence sequence,
        IReadOnlyDictionary<SourceId, int> inputIndexBySource,
        string baseLabel,
        bool supportsPitchShift)
    {
        var labels = new List<string> { baseLabel };
        var index = 0;

        foreach (var track in sequence.AudibleTracks)
        {
            foreach (var clip in track.InTimelineOrder())
            {
                if (!inputIndexBySource.TryGetValue(clip.SourceId, out var input))
                {
                    // Источник не попал во входы — молча пропустить нельзя, но и падать
                    // посреди экспорта незачем: такой кусок просто не звучит.
                    continue;
                }

                var label = "x" + index.ToString(CultureInfo.InvariantCulture);
                AppendClip(builder, clip, track, input, label, supportsPitchShift);
                labels.Add(label);
                index++;
            }
        }

        if (labels.Count == 1)
        {
            return baseLabel;
        }

        foreach (var label in labels)
        {
            builder.Append($"[{label}]");
        }

        // duration=first: звук, уехавший за конец видеоряда, обрезается по картинке.
        builder
            .Append("amix=inputs=")
            .Append(labels.Count.ToString(CultureInfo.InvariantCulture))
            .Append(":duration=first:dropout_transition=0:normalize=0")
            .Append("[amix];");

        return "amix";
    }

    private static void AppendClip(
        StringBuilder builder,
        AudioClip clip,
        AudioTrack track,
        int input,
        string label,
        bool supportsPitchShift)
    {
        var chain = new List<string>
        {
            $"atrim=start={Time(clip.SourceRange.Start)}:end={Time(clip.SourceRange.End)}",
            "asetpts=PTS-STARTPTS"
        };

        if (clip.IsSpeedChanged)
        {
            chain.AddRange(SpeedFilter.AudioTempoFilters(clip.Speed));
        }

        if (clip.IsPitchShifted)
        {
            chain.AddRange(PitchFilters(clip.PitchSemitones, supportsPitchShift));
        }

        var gain = clip.Gain * track.Gain;
        if (Math.Abs(gain - 1d) > 0.0001)
        {
            chain.Add($"volume={SpeedFilter.FormatFactor(gain)}");
        }

        if (clip.FadeIn > TimeSpan.Zero)
        {
            chain.Add($"afade=t=in:st=0:d={Time(clip.FadeIn)}");
        }

        if (clip.FadeOut > TimeSpan.Zero)
        {
            var start = clip.Duration - clip.FadeOut;
            chain.Add($"afade=t=out:st={Time(start)}:d={Time(clip.FadeOut)}");
        }

        if (clip.TimelineStart > TimeSpan.Zero)
        {
            var delay = (long)Math.Round(clip.TimelineStart.TotalMilliseconds);
            chain.Add($"adelay={delay.ToString(CultureInfo.InvariantCulture)}:all=1");
        }

        builder.Append($"[{input}:a]").Append(string.Join(',', chain)).Append($"[{label}];");
    }

    /// <summary>
    /// Сдвиг тональности без изменения длительности.
    /// </summary>
    /// <remarks>
    /// rubberband делает это в один фильтр и звучит заметно чище. Если его нет
    /// в сборке ffmpeg, остаётся классическая связка: пересемплировать (звук едет
    /// вместе с темпом), вернуть частоту дискретизации и вернуть темп обратно.
    /// </remarks>
    private static IEnumerable<string> PitchFilters(int semitones, bool supportsPitchShift)
    {
        var clamped = Math.Clamp(semitones, -MaxSemitones, MaxSemitones);
        var ratio = Math.Pow(2d, clamped / 12d);

        if (supportsPitchShift)
        {
            yield return $"rubberband=pitch={SpeedFilter.FormatFactor(ratio)}";
            yield break;
        }

        yield return $"asetrate=48000*{SpeedFilter.FormatFactor(ratio)}";
        yield return "aresample=48000";

        // asetrate ускорил звук ровно во столько же раз — темп возвращаем обратным множителем.
        foreach (var filter in SpeedFilter.AudioTempoFilters(1d / ratio))
        {
            yield return filter;
        }
    }

    private static string Time(TimeSpan value) => FfmpegArgumentBuilder.FormatTime(value);
}
