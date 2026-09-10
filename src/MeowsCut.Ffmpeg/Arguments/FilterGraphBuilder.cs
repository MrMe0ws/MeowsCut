using System.Globalization;
using System.Text;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Export;
using MeowsCut.Core.Media;

namespace MeowsCut.Ffmpeg.Arguments;

/// <summary>Готовый граф фильтров и метки, которые нужно отдать в -map.</summary>
public sealed record FilterGraph(string Text, string VideoLabel, string? AudioLabel)
{
    public bool HasAudio => AudioLabel is not null;
}

/// <summary>
/// Строит -filter_complex из последовательности клипов.
/// </summary>
/// <remarks>
/// Основная стратегия экспорта — один проход: каждый клип превращается в пару
/// цепочек trim/atrim, всё склеивается concat, затем один раз применяются
/// масштаб, частота кадров и формат пикселя. Это даёт точность кадр-в-кадр
/// и линейный прогресс без временных файлов.
/// </remarks>
public sealed class FilterGraphBuilder
{
    private const int SilenceSampleRate = 48_000;
    private const string SilenceLayout = "stereo";

    /// <summary>
    /// Приведение куска к общему виду перед склейкой. Нужно только там, где в ряду
    /// есть сгенерированные части: у них SAR и формат пикселя свои, и concat
    /// отказывается работать, даже если картинка на глаз одинаковая.
    /// </summary>
    private const string NormalizeFilters = "format=yuv420p,setsar=1";

    private readonly AudioMixBuilder _mixer = new();

    /// <summary>
    /// Есть ли в сборке ffmpeg фильтр rubberband. Задаётся снаружи — сборки бывают разные,
    /// а без него сдвиг тональности собирается из asetrate и atempo.
    /// </summary>
    public bool SupportsPitchShift { get; set; } = true;

    /// <param name="inputIndexBySource">Соответствие источника номеру входа -i.</param>
    public FilterGraph Build(
        Sequence sequence,
        IReadOnlyDictionary<SourceId, int> inputIndexBySource,
        ExportSettings settings)
    {
        var clips = sequence.Video.Clips;
        if (clips.Count == 0)
        {
            throw new InvalidOperationException("Последовательность пуста.");
        }

        // Отдельные дорожки — тоже звук: без них ролик, у которого своя звуковая
        // дорожка выключена, а подложена другая, вышел бы немым.
        var wantsAudio = settings.Audio.Enabled && sequence.HasAnyAudio;

        var builder = new StringBuilder();
        var videoLabels = new List<string>(clips.Count);
        var audioLabels = new List<string>(clips.Count);

        // При зазорах все куски приходится приводить к одному знаменателю: concat
        // требует совпадения размера, формата пикселя и SAR, а чёрная вставка
        // рождается заново и знать параметры источника не может.
        var hasGaps = sequence.Video.HasGaps;

        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            var input = inputIndexBySource[clip.SourceId];

            if (clip.HasLeadingGap)
            {
                AppendGapChains(builder, sequence, clip.LeadingGap, i, videoLabels, audioLabels, wantsAudio);
            }

            AppendVideoChain(builder, clip, input, i, videoLabels, hasGaps);

            if (wantsAudio)
            {
                AppendAudioChain(builder, clip, input, i, audioLabels);
            }
        }

        var videoOut = AppendConcat(builder, videoLabels, audioLabels, wantsAudio, out var audioOut);

        videoOut = AppendOutputVideoChain(builder, sequence, settings, videoOut);

        if (wantsAudio && audioOut is not null)
        {
            audioOut = _mixer.Append(builder, sequence, inputIndexBySource, audioOut, SupportsPitchShift);
            audioOut = AppendOutputAudioChain(builder, settings, audioOut);
        }

        // Лишняя точка с запятой в конце графа — синтаксическая ошибка для ffmpeg.
        return new FilterGraph(builder.ToString().TrimEnd(';'), videoOut, wantsAudio ? audioOut : null);
    }

    /// <summary>
    /// Пустое место на дорожке: чёрный кадр и тишина ровно на длину зазора.
    /// </summary>
    /// <remarks>
    /// Зазор — такой же участок ролика, как клип, и concat обязан получить его
    /// отдельной частью. Размер и частота кадров берутся у последовательности:
    /// у пустоты своего источника нет, а разъехавшийся размер ломает склейку.
    /// </remarks>
    private static void AppendGapChains(
        StringBuilder builder,
        Sequence sequence,
        TimeSpan gap,
        int index,
        List<string> videoLabels,
        List<string> audioLabels,
        bool wantsAudio)
    {
        var label = "g" + index.ToString(CultureInfo.InvariantCulture);
        var size = sequence.Format.Size;
        var frameRate = sequence.Format.FrameRate;

        // Дробью, а не числом: 30000/1001 в виде 29.97 даёт дрейф на длинном зазоре.
        var rate = frameRate.IsZero
            ? "30"
            : $"{frameRate.Numerator.ToString(CultureInfo.InvariantCulture)}/{frameRate.Denominator.ToString(CultureInfo.InvariantCulture)}";

        builder
            .Append($"color=c=black:s={size.Width}x{size.Height}:r={rate}:d={Time(gap)}")
            .Append($",{NormalizeFilters}")
            .Append($"[{label}];");

        videoLabels.Add(label);

        if (!wantsAudio)
        {
            return;
        }

        var audioLabel = "ga" + index.ToString(CultureInfo.InvariantCulture);

        builder
            .Append($"anullsrc=channel_layout={SilenceLayout}:sample_rate={SilenceSampleRate}")
            .Append($",atrim=duration={Time(gap)}")
            .Append(",asetpts=PTS-STARTPTS")
            .Append($"[{audioLabel}];");

        audioLabels.Add(audioLabel);
    }

    private static void AppendVideoChain(
        StringBuilder builder,
        Clip clip,
        int input,
        int index,
        List<string> labels,
        bool normalize)
    {
        var label = "v" + index.ToString(CultureInfo.InvariantCulture);

        var chain = new List<string>
        {
            $"trim=start={Time(clip.SourceRange.Start)}:end={Time(clip.SourceRange.End)}",
            clip.IsSpeedChanged
                ? $"setpts=(PTS-STARTPTS)/{SpeedFilter.FormatFactor(clip.Speed)}"
                : "setpts=PTS-STARTPTS"
        };

        AppendTransform(chain, clip.Transform);

        if (normalize)
        {
            chain.Add(NormalizeFilters);
        }

        builder.Append($"[{input}:v]").Append(string.Join(',', chain)).Append($"[{label}];");
        labels.Add(label);
    }

    /// <summary>
    /// Масштаб и смещение кадра внутри клипа. При увеличении лишнее обрезается,
    /// при уменьшении добавляются поля — иначе размеры клипов в concat разъедутся.
    /// </summary>
    private static void AppendTransform(List<string> chain, ClipTransform transform)
    {
        if (transform.IsIdentity)
        {
            return;
        }

        var zoom = SpeedFilter.FormatFactor(transform.Zoom);
        var dx = SpeedFilter.FormatFactor(transform.OffsetX);
        var dy = SpeedFilter.FormatFactor(transform.OffsetY);

        chain.Add($"scale=iw*{zoom}:ih*{zoom}");

        if (transform.Zoom >= 1d)
        {
            chain.Add($"crop=w=iw/{zoom}:h=ih/{zoom}:x=(iw-ow)/2+ow*{dx}:y=(ih-oh)/2+oh*{dy}");
        }
        else
        {
            chain.Add($"pad=w=iw/{zoom}:h=ih/{zoom}:x=(ow-iw)/2+ow*{dx}:y=(oh-ih)/2+oh*{dy}:color=black");
        }
    }

    private static void AppendAudioChain(
        StringBuilder builder,
        Clip clip,
        int input,
        int index,
        List<string> labels)
    {
        var label = "a" + index.ToString(CultureInfo.InvariantCulture);

        if (!clip.HasAudio)
        {
            // Клип без звука обязан отдать тишину нужной длины: concat разваливается,
            // если у частей разное число потоков.
            builder
                .Append($"anullsrc=channel_layout={SilenceLayout}:sample_rate={SilenceSampleRate}")
                .Append($",atrim=duration={Time(clip.TimelineDuration)}")
                .Append(",asetpts=PTS-STARTPTS")
                .Append($"[{label}];");

            labels.Add(label);
            return;
        }

        var chain = new List<string>
        {
            $"atrim=start={Time(clip.SourceRange.Start)}:end={Time(clip.SourceRange.End)}",
            "asetpts=PTS-STARTPTS"
        };

        chain.AddRange(SpeedFilter.AudioTempoFilters(clip.Speed));

        if (Math.Abs(clip.Audio.Volume - 1d) > 0.0001)
        {
            chain.Add($"volume={SpeedFilter.FormatFactor(clip.Audio.Volume)}");
        }

        builder.Append($"[{input}:a]").Append(string.Join(',', chain)).Append($"[{label}];");
        labels.Add(label);
    }

    private static string AppendConcat(
        StringBuilder builder,
        IReadOnlyList<string> videoLabels,
        IReadOnlyList<string> audioLabels,
        bool wantsAudio,
        out string? audioOut)
    {
        audioOut = null;

        if (videoLabels.Count == 1 && !wantsAudio)
        {
            return videoLabels[0];
        }

        if (videoLabels.Count == 1 && wantsAudio)
        {
            audioOut = audioLabels[0];
            return videoLabels[0];
        }

        for (var i = 0; i < videoLabels.Count; i++)
        {
            builder.Append($"[{videoLabels[i]}]");
            if (wantsAudio)
            {
                builder.Append($"[{audioLabels[i]}]");
            }
        }

        var count = videoLabels.Count.ToString(CultureInfo.InvariantCulture);
        var audioFlag = wantsAudio ? "1" : "0";

        builder.Append($"concat=n={count}:v=1:a={audioFlag}[vc]");

        if (wantsAudio)
        {
            builder.Append("[ac]");
            audioOut = "ac";
        }

        builder.Append(';');
        return "vc";
    }

    private static string AppendOutputVideoChain(
        StringBuilder builder,
        Sequence sequence,
        ExportSettings settings,
        string inputLabel)
    {
        var target = settings.Video.Resolution.Resolve(sequence.Format.Size);
        var fps = settings.Video.FrameRate.Resolve(sequence.Format.FrameRate);
        var pixelFormat = settings.Video.Advanced.PixelFormat;

        var chain = new List<string>();

        if (target != sequence.Format.Size && !target.IsEmpty)
        {
            chain.AddRange(ScaleFilters(target, ResolveFitMode(settings.Video.Resolution)));
        }

        if (fps is { } fixedFps)
        {
            chain.Add($"fps={SpeedFilter.FormatFactor(fixedFps)}");
        }

        // Субтитры вшиваются после масштабирования: иначе текст масштабировался бы
        // вместе с кадром и на маленьком выходе превращался в кашу.
        if (settings.Subtitles.IsBurnedIn)
        {
            chain.Add(SubtitleFilter.Build(settings.Subtitles));
        }

        if (!string.IsNullOrWhiteSpace(pixelFormat))
        {
            chain.Add($"format={pixelFormat}");
        }

        if (chain.Count == 0)
        {
            return inputLabel;
        }

        builder.Append($"[{inputLabel}]").Append(string.Join(',', chain)).Append("[vout];");
        return "vout";
    }

    private static FitMode ResolveFitMode(ResolutionSpec spec) =>
        spec is ResolutionSpec.Custom custom ? custom.Fit : FitMode.Contain;

    private static IEnumerable<string> ScaleFilters(FrameSize target, FitMode fit)
    {
        var w = target.Width.ToString(CultureInfo.InvariantCulture);
        var h = target.Height.ToString(CultureInfo.InvariantCulture);

        switch (fit)
        {
            case FitMode.Cover:
                yield return $"scale={w}:{h}:force_original_aspect_ratio=increase";
                yield return $"crop={w}:{h}";
                break;

            case FitMode.Stretch:
                yield return $"scale={w}:{h}";
                break;

            default:
                yield return $"scale={w}:{h}:force_original_aspect_ratio=decrease";
                yield return $"pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:color=black";
                break;
        }
    }

    private static string AppendOutputAudioChain(StringBuilder builder, ExportSettings settings, string inputLabel)
    {
        if (Math.Abs(settings.Audio.MasterVolume - 1d) < 0.0001)
        {
            return inputLabel;
        }

        builder
            .Append($"[{inputLabel}]")
            .Append($"volume={SpeedFilter.FormatFactor(settings.Audio.MasterVolume)}")
            .Append("[aout];");

        return "aout";
    }

    private static string Time(TimeSpan value) => FfmpegArgumentBuilder.FormatTime(value);
}
