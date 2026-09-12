using System.Globalization;
using System.Text;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Export;
using MeowsCut.Core.Media;

namespace MeowsCut.Ffmpeg.Arguments;

/// <summary>Готовый граф фильтров и метки, которые нужно отдать в -map.</summary>
public sealed record FilterGraph(string Text, string? VideoLabel, string? AudioLabel)
{
    public bool HasVideo => VideoLabel is not null;

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
    /// Приведение куска к общему виду перед склейкой: формат пикселя и SAR.
    /// </summary>
    /// <remarks>
    /// Нужно там, где в ряду есть сгенерированные части — чёрная вставка зазора
    /// и кадры фотографии. У них SAR и формат пикселя свои, и concat отказывается
    /// работать, даже если картинка на глаз одинаковая.
    /// </remarks>
    private const string NormalizeFilters = "format=yuv420p,setsar=1";

    private readonly AudioMixBuilder _mixer = new();

    /// <summary>
    /// Есть ли в сборке ffmpeg фильтр rubberband. Задаётся снаружи — сборки бывают разные,
    /// а без него сдвиг тональности собирается из asetrate и atempo.
    /// </summary>
    public bool SupportsPitchShift { get; set; } = true;

    /// <summary>
    /// Умеет ли сборка рисовать надписи. drawtext требует собранного libfreetype,
    /// и в урезанных сборках его нет: тогда титры просто не попадают в граф,
    /// а планировщик предупреждает об этом отдельно.
    /// </summary>
    public bool SupportsTitles { get; set; } = true;

    /// <summary>
    /// Где лежит текст каждой надписи. Готовит их планировщик: сам граф —
    /// сборщик строки, файлов он не трогает.
    /// </summary>
    public IReadOnlyDictionary<TitleId, string> TitleTextFiles { get; set; } =
        new Dictionary<TitleId, string>();

    /// <param name="inputIndexBySource">Соответствие источника номеру входа -i.</param>
    public FilterGraph Build(
        Sequence sequence,
        IReadOnlyDictionary<SourceId, int> inputIndexBySource,
        ExportSettings settings)
    {
        var clips = sequence.Video.Clips;

        // Видеоряда может не быть вовсе — например, когда проверяют кусок хвоста,
        // где идёт только музыка. Тогда весь отрезок и есть чёрный кадр.
        if (clips.Count == 0 && !sequence.HasVideoTail)
        {
            throw new InvalidOperationException("Последовательность пуста.");
        }

        if (settings.IsAudioOnly)
        {
            return BuildAudioOnly(sequence, inputIndexBySource, settings);
        }

        // Отдельные дорожки — тоже звук: без них ролик, у которого своя звуковая
        // дорожка выключена, а подложена другая, вышел бы немым.
        var wantsAudio = settings.Audio.Enabled && sequence.HasAnyAudio;

        var builder = new StringBuilder();
        var videoLabels = new List<string>(clips.Count);
        var audioLabels = new List<string>(clips.Count);

        // Куски приходится приводить к одному знаменателю, как только в ряду
        // появляется что-то сгенерированное: concat требует совпадения размера,
        // формата пикселя и SAR, а чёрная вставка и кадр фотографии рождаются
        // заново и параметров источника знать не могут.
        // Поворот и выбранный вручную формат ролика — сюда же: и то и другое
        // разводит размеры кусков, а concat требует их точного совпадения.
        var normalize = sequence.NeedsUniformFrame;

        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            var input = inputIndexBySource[clip.SourceId];

            if (clip.HasLeadingGap)
            {
                AppendGapChains(builder, sequence, clip.LeadingGap, i, videoLabels, audioLabels, wantsAudio);
            }

            AppendVideoChain(builder, clip, input, i, videoLabels, normalize, sequence.Format);

            if (wantsAudio)
            {
                AppendAudioChain(builder, clip, input, i, audioLabels);
            }
        }

        // Звук, выходящий за видеоряд, продлевает ролик чёрным кадром. Без этого
        // amix обрезал бы музыку по последнему кадру картинки.
        if (sequence.HasVideoTail)
        {
            AppendGapChains(
                builder,
                sequence,
                sequence.VideoTail,
                clips.Count,
                videoLabels,
                audioLabels,
                wantsAudio);
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
    /// Звук без картинки: то же дерево цепочек, только без видеоветок.
    /// </summary>
    /// <remarks>
    /// Тишину зазоров и хвоста приходится строить и здесь. Без неё куски звука
    /// сомкнулись бы вплотную, музыка с отдельной дорожки встала бы не на своё
    /// место, и вытащенная дорожка разошлась бы с роликом, из которого её взяли.
    /// </remarks>
    private FilterGraph BuildAudioOnly(
        Sequence sequence,
        IReadOnlyDictionary<SourceId, int> inputIndexBySource,
        ExportSettings settings)
    {
        if (!sequence.HasAnyAudio)
        {
            throw new InvalidOperationException("В выбранном куске нет звука.");
        }

        var builder = new StringBuilder();
        var labels = new List<string>();
        var clips = sequence.Video.Clips;

        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];

            if (clip.HasLeadingGap)
            {
                var gapLabel = "ga" + i.ToString(CultureInfo.InvariantCulture);
                AppendSilence(builder, gapLabel, clip.LeadingGap);
                labels.Add(gapLabel);
            }

            AppendAudioChain(builder, clip, inputIndexBySource[clip.SourceId], i, labels);
        }

        if (sequence.HasVideoTail)
        {
            var tailLabel = "gat";
            AppendSilence(builder, tailLabel, sequence.VideoTail);
            labels.Add(tailLabel);
        }

        string audioOut;
        if (labels.Count == 1)
        {
            audioOut = labels[0];
        }
        else
        {
            foreach (var label in labels)
            {
                builder.Append($"[{label}]");
            }

            var count = labels.Count.ToString(CultureInfo.InvariantCulture);
            builder.Append($"concat=n={count}:v=0:a=1[ac];");
            audioOut = "ac";
        }

        audioOut = _mixer.Append(builder, sequence, inputIndexBySource, audioOut, SupportsPitchShift);
        audioOut = AppendOutputAudioChain(builder, settings, audioOut);

        return new FilterGraph(builder.ToString().TrimEnd(';'), VideoLabel: null, audioOut);
    }

    /// <summary>Тишина заданной длины: ею заполняются зазоры и клипы без звука.</summary>
    private static void AppendSilence(StringBuilder builder, string label, TimeSpan duration)
    {
        builder
            .Append($"anullsrc=channel_layout={SilenceLayout}:sample_rate={SilenceSampleRate}")
            .Append($",atrim=duration={Time(duration)}")
            .Append(",asetpts=PTS-STARTPTS")
            .Append($"[{label}];");
    }

    /// <summary>
    /// Пустое место: чёрный кадр и тишина ровно на нужную длину.
    /// </summary>
    /// <remarks>
    /// Так рисуются и зазор между клипами, и хвост после последнего клипа, когда
    /// звук длиннее видеоряда. Пустота — такой же участок ролика, как клип, и concat
    /// обязан получить её отдельной частью. Размер и частота кадров берутся
    /// у последовательности: своего источника у пустоты нет, а разъехавшийся размер
    /// ломает склейку.
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

        AppendSilence(builder, audioLabel, gap);
        audioLabels.Add(audioLabel);
    }

    private static void AppendVideoChain(
        StringBuilder builder,
        Clip clip,
        int input,
        int index,
        List<string> labels,
        bool normalize,
        SequenceFormat format)
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
        AppendFades(chain, clip);

        if (normalize)
        {
            // Размер тоже: фотография почти наверняка не совпадает с роликом,
            // а concat со съехавшим размером просто не соберётся. Как именно
            // кусок ложится в кадр — выбор пользователя: горизонтальное видео
            // в вертикальном формате либо стоит полосой с полями, либо
            // заполняет кадр с обрезкой по бокам.
            chain.AddRange(ScaleFilters(format.Size, format.Fit));
            chain.Add(NormalizeFilters);
        }

        builder.Append($"[{input}:v]").Append(string.Join(',', chain)).Append($"[{label}];");
        labels.Add(label);
    }

    /// <summary>
    /// Появление из чёрного и уход в чёрное.
    /// </summary>
    /// <remarks>
    /// Отсчёт идёт от нуля, потому что стоит после setpts=PTS-STARTPTS: клип
    /// к этому месту уже начинается с нулевой метки времени. Длины берутся
    /// подрезанные — заказанное затухание длиннее клипа обязано ужаться,
    /// иначе fade=out начался бы за его концом и не показался вовсе.
    /// </remarks>
    private static void AppendFades(List<string> chain, Clip clip)
    {
        var fadeIn = clip.EffectiveFadeIn;
        if (fadeIn > TimeSpan.Zero)
        {
            chain.Add($"fade=t=in:st=0:d={Time(fadeIn)}");
        }

        var fadeOut = clip.EffectiveFadeOut;
        if (fadeOut > TimeSpan.Zero)
        {
            var start = clip.TimelineDuration - fadeOut;
            chain.Add($"fade=t=out:st={Time(start)}:d={Time(fadeOut)}");
        }
    }

    /// <summary>
    /// Масштаб, поворот и смещение кадра внутри клипа. При увеличении лишнее
    /// обрезается, при уменьшении добавляются поля — иначе размеры клипов
    /// в concat разъедутся.
    /// </summary>
    private static void AppendTransform(List<string> chain, ClipTransform transform)
    {
        if (transform.IsIdentity)
        {
            return;
        }

        // Поворот идёт первым: масштаб и смещение пользователь задаёт, глядя
        // на уже развёрнутый кадр, и применять их к исходной ориентации значило бы
        // двигать картинку не в ту сторону.
        AppendRotation(chain, transform.Rotation);

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

    /// <summary>
    /// Поворот на прямой угол. transpose работает без потерь и без интерполяции,
    /// в отличие от rotate с произвольным углом.
    /// </summary>
    private static void AppendRotation(List<string> chain, int rotation)
    {
        switch (rotation)
        {
            case 90:
                chain.Add("transpose=1");
                break;

            case 180:
                // Отражение по обеим осям дешевле двух transpose подряд
                // и не требует дважды переписывать кадр в памяти.
                chain.Add("hflip");
                chain.Add("vflip");
                break;

            case 270:
                chain.Add("transpose=2");
                break;
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
            AppendSilence(builder, label, clip.TimelineDuration);
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

    private string AppendOutputVideoChain(
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

        // Надписи — туда же и по той же причине: их размер задан долей высоты
        // готового кадра, а не исходного.
        if (sequence.HasTitles && SupportsTitles)
        {
            chain.AddRange(TitleFilter.Build(sequence.Titles, TitleTextFiles));
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
        var chain = new List<string>();

        if (Math.Abs(settings.Audio.MasterVolume - 1d) > 0.0001)
        {
            chain.Add($"volume={SpeedFilter.FormatFactor(settings.Audio.MasterVolume)}");
        }

        // Нормализация последней: ей нужен тот сигнал, который уйдёт в файл,
        // включая уже применённый общий множитель громкости.
        if (settings.Audio.NormalizeLoudness)
        {
            chain.Add(LoudnessFilter());
        }

        if (chain.Count == 0)
        {
            return inputLabel;
        }

        builder.Append($"[{inputLabel}]").Append(string.Join(',', chain)).Append("[aout];");

        return "aout";
    }

    /// <summary>
    /// Приведение громкости к EBU R128 одним проходом.
    /// </summary>
    /// <remarks>
    /// Двухпроходный loudnorm точнее, но требует прогнать весь звук заранее и
    /// подставить измеренные числа второй командой — это удвоенное ожидание ради
    /// разницы, которую на слух не поймать. Однопроходный режим достаточно ровный,
    /// а главное — предсказуемо не даёт перегруза по пику.
    /// </remarks>
    private static string LoudnessFilter()
    {
        var target = AudioSettings.LoudnessTargetLufs.ToString("0.#", CultureInfo.InvariantCulture);
        var peak = AudioSettings.LoudnessTruePeakDb.ToString("0.#", CultureInfo.InvariantCulture);

        return $"loudnorm=I={target}:TP={peak}:LRA=11";
    }

    private static string Time(TimeSpan value) => FfmpegArgumentBuilder.FormatTime(value);
}
