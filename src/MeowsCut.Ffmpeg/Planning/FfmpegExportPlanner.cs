using System.Globalization;
using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Export;
using MeowsCut.Core.Media;
using MeowsCut.Core.Processing;
using MeowsCut.Ffmpeg.Arguments;

namespace MeowsCut.Ffmpeg.Planning;

/// <summary>
/// Превращает запрос на экспорт в план стадий.
/// </summary>
/// <remarks>
/// Выбор стратегии — единственное место, где решается «перекодировать или скопировать».
/// По умолчанию берётся точный путь через filter_complex; копирование потоков
/// применяется, только когда правки этого не запрещают и пользователь сам выбрал
/// быстрый режим, потому что границы обрезки при копировании прилипают к ключевым кадрам.
/// </remarks>
public sealed class FfmpegExportPlanner(AppPaths paths) : IExportPlanner
{
    private readonly FilterGraphBuilder _graphBuilder = new();

    public ExportPlan CreatePlan(ExportRequest request, MediaCapabilities capabilities)
    {
        var settings = request.Settings.Normalized();
        var project = request.Project;
        var sequence = project.Sequence;

        if (sequence.IsEmpty)
        {
            throw new Core.Diagnostics.EditOperationException("На таймлайне нет клипов — нечего экспортировать.");
        }

        var outputPath = ResolveOutputPath(settings);
        var partPath = outputPath + ".meowscut.part";

        var warnings = new List<PlanWarning>();
        // Проверяем до всякой работы: без источника нечего подавать на вход ffmpeg,
        // и без этой проверки падение случилось бы глубоко внутри сборки графа фильтров.
        if (project.MissingSourceIds() is { Count: > 0 })
        {
            throw new EditOperationException(
                "На доске есть кусок, чей файл в проекте не найден. Добавьте файл заново.");
        }

        var streamCopy = CanStreamCopy(project, settings, warnings);

        // Решение о втором проходе принимается по исходным настройкам: ниже целевой
        // размер уже превратится в обычный битрейт и режим станет неотличим.
        var twoPass = NeedsTwoPasses(settings, streamCopy);

        // Целевой размер разворачивается в конкретный битрейт: дальше по коду
        // это обычное кодирование с ограничением, а не отдельный режим.
        var effective = ResolveTargetSize(settings, sequence.Duration);

        // Отсюда и ниже режим двух проходов уже неотличим от обычного, поэтому
        // отказ от видеокарты фиксируется здесь: журнал первого прохода она не ведёт.
        if (twoPass && effective.Video.Hardware != HardwareAcceleration.None)
        {
            effective = effective with
            {
                Video = effective.Video with { Hardware = HardwareAcceleration.None }
            };
        }

        var stages = new List<ExportStage>();
        var cleanup = new List<string>();

        if (!streamCopy && CanConcatWithoutReencoding(project, settings))
        {
            stages.AddRange(BuildConcatStages(project, settings, partPath, cleanup));
        }
        else if (streamCopy)
        {
            stages.Add(new ExportStage(
                StageKind.Remux,
                "Копирование без перекодирования",
                BuildStreamCopyArguments(project, effective, partPath),
                Weight: 1d,
                sequence.Duration,
                partPath));
        }
        else if (twoPass)
        {
            // Папку статистики создаём заранее: ffmpeg не создаёт её сам и просто падает.
            Directory.CreateDirectory(paths.TempDirectory);

            var logPrefix = Path.Combine(paths.TempDirectory, "pass-" + Guid.NewGuid().ToString("N"));

            stages.Add(new ExportStage(
                StageKind.PassOne,
                "Анализ видео",
                BuildTranscodeArguments(project, effective, capabilities, NullOutput, 1, logPrefix),
                // Первый проход быстрее второго: он не пишет файл и не трогает звук.
                Weight: 0.45,
                sequence.Duration,
                OutputFile: null));

            stages.Add(new ExportStage(
                StageKind.PassTwo,
                "Кодирование видео",
                BuildTranscodeArguments(project, effective, capabilities, partPath, 2, logPrefix),
                Weight: 0.55,
                sequence.Duration,
                partPath));

            cleanup.Add(logPrefix + "-0.log");
            cleanup.Add(logPrefix + "-0.log.mbtree");
        }
        else
        {
            stages.Add(new ExportStage(
                StageKind.Transcode,
                "Обработка видео",
                BuildTranscodeArguments(project, effective, capabilities, partPath, pass: null, logPrefix: null),
                Weight: 1d,
                sequence.Duration,
                partPath));
        }

        CollectWarnings(project, settings, streamCopy, twoPass, capabilities, warnings);

        var summary = BuildSummary(project, settings, streamCopy);

        return new ExportPlan(stages, sequence.Duration, summary, warnings, outputPath)
        {
            CleanupPaths = cleanup
        };
    }

    /// <summary>Пустой вывод первого прохода: файл не пишется, считается только статистика.</summary>
    private const string NullOutput = "NUL";

    /// <summary>
    /// Превращает «уложиться в размер» в битрейт. Считается по ожидаемой длительности
    /// результата, а не исходника — при вырезании и ускорении это разные величины.
    /// </summary>
    private static ExportSettings ResolveTargetSize(ExportSettings settings, TimeSpan duration)
    {
        if (settings.Video.RateControl is not RateControl.TargetSize target)
        {
            return settings;
        }

        var audioKbps = settings.Audio.Enabled ? settings.Audio.BitrateKbps : 0;
        var videoKbps = RateControlPolicy.BitrateForTargetSizeKbps(target.Bytes, duration, audioKbps);

        return settings with
        {
            Video = settings.Video with { RateControl = new RateControl.ConstantBitrate(videoKbps) }
        };
    }

    /// <summary>
    /// Нужен ли второй проход: он даёт заметно более ровное качество при заданном
    /// битрейте, но удваивает время, поэтому включается только явно или там,
    /// где без него не уложиться в размер.
    /// </summary>
    /// <summary>
    /// Аппаратное кодирование с оговорками.
    /// </summary>
    /// <remarks>
    /// Отключается молча в трёх случаях: этой пары кодек+железо нет в сборке ffmpeg,
    /// два прохода (видеокарты не ведут журнал первого прохода) и целевой размер,
    /// который через два прохода и делается. Лучше выйти медленнее, но с тем файлом,
    /// который заказывали.
    /// </remarks>
    private static HardwareAcceleration ResolveHardware(
        ExportSettings settings,
        MediaCapabilities capabilities)
    {
        var hardware = settings.Video.Hardware;

        if (hardware == HardwareAcceleration.None)
        {
            return HardwareAcceleration.None;
        }

        if (settings.Video.Advanced.TwoPass || settings.Video.RateControl is RateControl.TargetSize)
        {
            return HardwareAcceleration.None;
        }

        return HardwareEncoders.IsAvailable(settings.Video.Codec, hardware, capabilities)
            ? hardware
            : HardwareAcceleration.None;
    }

    private static bool NeedsTwoPasses(ExportSettings settings, bool streamCopy) =>
        !streamCopy &&
        (settings.Video.Advanced.TwoPass || settings.Video.RateControl is RateControl.TargetSize) &&
        settings.Video.Codec != VideoCodec.Copy;

    /// <summary>
    /// Можно ли склеить куски без перекодирования.
    /// </summary>
    /// <remarks>
    /// Демультиплексор concat склеивает потоки как есть, поэтому требует, чтобы у всех
    /// частей совпадали кодеки и параметры. Гарантировать это можно только для кусков
    /// одного файла — на разнородных источниках склейка даёт битый результат, поэтому
    /// такой случай честнее отправить на перекодирование.
    /// </remarks>
    private static bool CanConcatWithoutReencoding(Project project, ExportSettings settings)
    {
        if (!settings.PreferStreamCopy)
        {
            return false;
        }

        var sequence = project.Sequence;
        if (sequence.ClipCount < 2)
        {
            return false;
        }

        // Демультиплексор concat склеивает части встык и о пустых местах не знает:
        // зазор просто исчез бы, а ролик стал короче задуманного.
        if (sequence.HasGaps || sequence.HasImages || sequence.HasVideoTail)
        {
            return false;
        }

        if (settings.Video.Resolution is not ResolutionSpec.Original ||
            settings.Video.FrameRate is not FrameRateSpec.Original)
        {
            return false;
        }

        if (Math.Abs(settings.Audio.MasterVolume - 1d) > 0.0001)
        {
            return false;
        }

        var firstSourceId = sequence.Video.Clips[0].SourceId;

        foreach (var clip in sequence.Video.Clips)
        {
            if (clip.SourceId != firstSourceId ||
                clip.IsSpeedChanged ||
                !clip.Transform.IsIdentity ||
                Math.Abs(clip.Audio.Volume - 1d) > 0.0001 ||
                !clip.Audio.Enabled)
            {
                return false;
            }
        }

        var source = project.Find(firstSourceId);
        var sourceVideo = CodecNames.VideoFromProbeName(source?.Info.PrimaryVideo?.CodecName);

        if (sourceVideo is null || !CompatibilityMatrix.Supports(settings.Container, sourceVideo.Value))
        {
            return false;
        }

        if (settings.Audio.Enabled && source?.Info.HasAudio == true)
        {
            var sourceAudio = CodecNames.AudioFromProbeName(source.Info.PrimaryAudio?.CodecName);
            if (sourceAudio is null || !CompatibilityMatrix.Supports(settings.Container, sourceAudio.Value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Стадии быстрой склейки: каждый кусок вырезается копированием потоков,
    /// затем всё соединяется одним проходом без перекодирования.
    /// </summary>
    private static IEnumerable<ExportStage> BuildConcatStages(
        Project project,
        ExportSettings settings,
        string partPath,
        List<string> cleanup)
    {
        var sequence = project.Sequence;
        var source = project.Require(sequence.Video.Clips[0].SourceId);
        var extension = Path.GetExtension(source.FilePath);

        var workDirectory = Path.Combine(
            Path.GetDirectoryName(partPath) ?? Path.GetTempPath(),
            "meowscut-segments-" + Guid.NewGuid().ToString("N")[..8]);

        var segments = new List<string>();
        var stages = new List<ExportStage>();

        for (var i = 0; i < sequence.ClipCount; i++)
        {
            var clip = sequence.Video.Clips[i];
            var segment = Path.Combine(workDirectory, $"seg-{i:000}{extension}");
            segments.Add(segment);

            var builder = FfmpegArgumentBuilder.Create()
                .HideBanner()
                .OverwriteOutput()
                .NoStdin()
                .LogLevel("error")
                .ProgressToStdout()
                .InputSeek(clip.SourceRange.Start)
                .Input(source.FilePath)
                .Option("-t", FfmpegArgumentBuilder.FormatTime(clip.SourceRange.Duration))
                .CopyAllStreams()
                .NoSubtitles();

            if (!settings.Audio.Enabled)
            {
                builder.NoAudio();
            }

            stages.Add(new ExportStage(
                StageKind.SegmentExtract,
                $"Вырезание фрагмента {i + 1} из {sequence.ClipCount}",
                builder.Output(segment).Build(),
                // Копирование идёт на порядок быстрее склейки в общем времени,
                // но каждый кусок всё равно занимает свою долю прогресса.
                Weight: 0.6 / sequence.ClipCount,
                clip.TimelineDuration,
                segment));
        }

        var listFile = Path.Combine(workDirectory, "concat.txt");

        var concatArguments = FfmpegArgumentBuilder.Create()
            .HideBanner()
            .OverwriteOutput()
            .NoStdin()
            .LogLevel("error")
            .ProgressToStdout()
            .InputFormat("concat")
            .InputOption("-safe", "0")
            .Input(listFile)
            .CopyAllStreams()
            .Format(settings.Container.MuxerName())
            .Output(partPath)
            .Build();

        stages.Add(new ExportStage(
            StageKind.Concat,
            "Сборка фрагментов",
            concatArguments,
            Weight: 0.4,
            sequence.Duration,
            partPath)
        {
            Concat = new ConcatSpec(listFile, segments)
        });

        cleanup.AddRange(segments);
        cleanup.Add(listFile);
        cleanup.Add(workDirectory);

        return stages;
    }

    /// <summary>
    /// Вход ffmpeg для одного источника.
    /// </summary>
    /// <remarks>
    /// Фотография отдаёт один-единственный кадр, и trim в графе фильтров вырезал бы
    /// из неё пустоту. Поэтому картинка подаётся зациклённой: -loop 1 повторяет кадр,
    /// -framerate задаёт темп, а -t ограничивает вход — без него ffmpeg читал бы
    /// картинку бесконечно и экспорт не закончился бы никогда.
    /// </remarks>
    private static void AppendInput(FfmpegArgumentBuilder builder, MediaSource source, Sequence sequence)
    {
        if (!source.IsImage)
        {
            builder.Input(source.FilePath);
            return;
        }

        var frameRate = sequence.Format.FrameRate;
        var rate = frameRate.IsZero
            ? "30"
            : $"{frameRate.Numerator.ToString(CultureInfo.InvariantCulture)}/{frameRate.Denominator.ToString(CultureInfo.InvariantCulture)}";

        // Секунда сверху: точка выхода последнего клипа обязана попасть внутрь входа.
        var limit = LastSourceMoment(sequence, source.Id) + TimeSpan.FromSeconds(1);

        builder
            .InputOption("-loop", "1")
            .InputOption("-framerate", rate)
            .InputOption("-t", FfmpegArgumentBuilder.FormatTime(limit))
            .Input(source.FilePath);
    }

    /// <summary>Самая дальняя точка выхода среди клипов этого источника.</summary>
    private static TimeSpan LastSourceMoment(Sequence sequence, SourceId sourceId)
    {
        var last = TimeSpan.Zero;

        foreach (var clip in sequence.Video.Clips)
        {
            if (clip.SourceId == sourceId && clip.SourceRange.End > last)
            {
                last = clip.SourceRange.End;
            }
        }

        return last;
    }

    /// <summary>
    /// Копирование потоков возможно, только если ни одна правка не требует пересчёта кадров.
    /// </summary>
    private static bool CanStreamCopy(Project project, ExportSettings settings, List<PlanWarning> warnings)
    {
        if (settings.Video.Codec != VideoCodec.Copy && !settings.PreferStreamCopy)
        {
            return false;
        }

        var sequence = project.Sequence;

        if (sequence.ClipCount != 1)
        {
            return false;
        }

        // Чёрная вставка перед клипом и кадры фотографии существуют только
        // после перерисовки: копировать тут нечего.
        if (sequence.HasGaps || sequence.HasImages || sequence.HasVideoTail)
        {
            return false;
        }

        // Вшитые субтитры существуют только после перерисовки кадра.
        if (settings.Subtitles.IsBurnedIn)
        {
            return false;
        }

        // Подмешанный звук существует только внутри графа фильтров: копирование
        // потоков отдало бы исходную дорожку, будто наложения и не было.
        if (sequence.HasAudioTracks)
        {
            warnings.Add(new PlanWarning(
                PlanWarningKind.AudioMixNeedsEncode,
                "Наложенный звук требует перекодирования — быстрая склейка не подходит."));
            return false;
        }

        var clip = sequence.Video.Clips[0];

        if (clip.IsSpeedChanged || !clip.Transform.IsIdentity)
        {
            return false;
        }

        if (settings.Video.Resolution is not ResolutionSpec.Original ||
            settings.Video.FrameRate is not FrameRateSpec.Original)
        {
            return false;
        }

        if (Math.Abs(settings.Audio.MasterVolume - 1d) > 0.0001 ||
            Math.Abs(clip.Audio.Volume - 1d) > 0.0001)
        {
            return false;
        }

        var source = project.Find(clip.SourceId);
        var sourceVideo = CodecNames.VideoFromProbeName(source?.Info.PrimaryVideo?.CodecName);

        if (sourceVideo is null || !CompatibilityMatrix.Supports(settings.Container, sourceVideo.Value))
        {
            return false;
        }

        if (settings.Audio.Enabled && clip.HasAudio)
        {
            var sourceAudio = CodecNames.AudioFromProbeName(source?.Info.PrimaryAudio?.CodecName);
            if (sourceAudio is null || !CompatibilityMatrix.Supports(settings.Container, sourceAudio.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<string> BuildStreamCopyArguments(
        Project project,
        ExportSettings settings,
        string outputPath)
    {
        var clip = project.Sequence.Video.Clips[0];
        var source = project.Require(clip.SourceId);

        var builder = FfmpegArgumentBuilder.Create()
            .HideBanner()
            .OverwriteOutput()
            .NoStdin()
            .LogLevel("error")
            .ProgressToStdout();

        if (clip.SourceRange.Start > TimeSpan.Zero)
        {
            builder.InputSeek(clip.SourceRange.Start);
        }

        builder.Input(source.FilePath);

        if (!clip.IsFullSource)
        {
            // -to после -i отсчитывается от точки входа, заданной -ss.
            builder.Option("-t", FfmpegArgumentBuilder.FormatTime(clip.SourceRange.Duration));
        }

        builder.CopyAllStreams();

        if (!settings.Audio.Enabled || !clip.HasAudio)
        {
            builder.NoAudio();
        }

        builder.NoSubtitles();

        foreach (var (key, value) in EncoderCatalog.ContainerOptions(settings.Container, VideoCodec.Copy))
        {
            builder.Option(key, value);
        }

        builder.Format(settings.Container.MuxerName());

        return builder.Output(outputPath).Build();
    }

    private IReadOnlyList<string> BuildTranscodeArguments(
        Project project,
        ExportSettings settings,
        MediaCapabilities capabilities,
        string outputPath,
        int? pass,
        string? logPrefix)
    {
        var sequence = project.Sequence;
        var sources = project.UsedSources();

        var indexBySource = new Dictionary<SourceId, int>();
        for (var i = 0; i < sources.Count; i++)
        {
            indexBySource[sources[i].Id] = i;
        }

        // В первом проходе звук не нужен, и цепочку для него строить нельзя:
        // висящий выход фильтра ffmpeg считает ошибкой, а не мелочью.
        var graphSettings = pass == 1
            ? settings with { Audio = AudioSettings.Disabled }
            : settings;

        _graphBuilder.SupportsPitchShift = capabilities.HasFilter("rubberband");

        var graph = _graphBuilder.Build(sequence, indexBySource, graphSettings);

        var builder = FfmpegArgumentBuilder.Create()
            .HideBanner()
            .OverwriteOutput()
            .NoStdin()
            .LogLevel("error")
            .ProgressToStdout();

        foreach (var source in sources)
        {
            AppendInput(builder, source, sequence);
        }

        // Дорожка субтитров — отдельный вход. В первом проходе она не нужна:
        // он считает статистику картинки и ничего не пишет.
        var embedSubtitles = settings.Subtitles.IsEmbedded &&
                             SubtitleFormats.SupportsEmbedded(settings.Container) &&
                             pass != 1;

        if (embedSubtitles)
        {
            builder.Input(settings.Subtitles.FilePath!);
        }

        builder.FilterComplex(graph.Text);
        builder.Map($"[{graph.VideoLabel}]");

        // В первом проходе звук не нужен: он только считает статистику картинки.
        var withAudio = graph.HasAudio && pass != 1;

        if (withAudio && graph.AudioLabel is { } audioLabel)
        {
            builder.Map($"[{audioLabel}]");
        }

        if (embedSubtitles)
        {
            builder.Map($"{sources.Count}:s:0");
            builder.Option("-c:s", SubtitleFormats.SubtitleCodec(settings.Container)!);
        }

        AppendVideoOptions(builder, settings, capabilities);

        if (withAudio)
        {
            AppendAudioOptions(builder, settings);
        }
        else
        {
            builder.NoAudio();
        }

        if (pass is { } passNumber && logPrefix is not null)
        {
            builder.Pass(passNumber, logPrefix);
        }

        if (pass == 1)
        {
            // Первый проход ничего не пишет на диск.
            builder.Format("null");
            return builder.Output(NullOutput).Build();
        }

        foreach (var (key, value) in EncoderCatalog.ContainerOptions(settings.Container, settings.Video.Codec))
        {
            builder.Option(key, value);
        }

        builder.Format(settings.Container.MuxerName());

        return builder.Output(outputPath).Build();
    }

    private static void AppendVideoOptions(
        FfmpegArgumentBuilder builder,
        ExportSettings settings,
        MediaCapabilities capabilities)
    {
        var codec = settings.Video.Codec;
        var hardware = ResolveHardware(settings, capabilities);

        var encoder = hardware == HardwareAcceleration.None
            ? EncoderCatalog.ResolveVideoEncoder(codec, capabilities)
            : HardwareEncoders.Name(codec, hardware)!;

        builder.VideoCodec(encoder);

        var rateControl = RateControlPolicy.Resolve(settings.Video.RateControl, codec);

        switch (rateControl)
        {
            case RateControl.ConstantQuality quality when hardware != HardwareAcceleration.None:
                foreach (var (key, value) in HardwareEncoders.QualityOptions(hardware, quality.Crf))
                {
                    builder.Option(key, value);
                }

                break;

            case RateControl.ConstantQuality quality:
                builder.Crf(quality.Crf);
                if (codec is VideoCodec.Vp9 or VideoCodec.Av1)
                {
                    // Для VP9 и AV1 постоянное качество включается только при нулевом битрейте.
                    builder.ZeroVideoBitrate();
                }

                break;

            case RateControl.ConstantBitrate constant:
                builder.VideoBitrateKbps(constant.Kbps);
                builder.MaxRateKbps(constant.Kbps * 2);
                builder.BufferSizeKbps(constant.Kbps * 4);
                break;

            case RateControl.TargetSize:
                // Подбор под размер появится вместе с пресетами стикеров.
                builder.Crf(RateControlPolicy.CrfRange(codec).Default);
                break;
        }

        var speedOptions = hardware == HardwareAcceleration.None
            ? EncoderCatalog.SpeedOptions(codec, encoder, settings.Video.Speed)
            : HardwareEncoders.SpeedOptions(hardware, settings.Video.Speed);

        foreach (var (key, value) in speedOptions)
        {
            builder.Option(key, value);
        }

        var advanced = settings.Video.Advanced;

        builder.PixelFormat(advanced.PixelFormat ?? EncoderCatalog.DefaultPixelFormat(codec));

        if (!string.IsNullOrWhiteSpace(advanced.Profile))
        {
            builder.Option("-profile:v", advanced.Profile);
        }

        if (advanced.KeyframeIntervalFrames is { } gop)
        {
            builder.Option("-g", gop.ToString(CultureInfo.InvariantCulture));
        }

        if (advanced.RawOptions is not null)
        {
            foreach (var (key, value) in advanced.RawOptions)
            {
                builder.Option(key, value);
            }
        }
    }

    private static void AppendAudioOptions(FfmpegArgumentBuilder builder, ExportSettings settings)
    {
        builder.AudioCodec(EncoderCatalog.AudioEncoder(settings.Audio.Codec));
        builder.AudioBitrateKbps(settings.Audio.BitrateKbps);

        if (settings.Audio.SampleRateHz is { } sampleRate)
        {
            builder.AudioSampleRate(sampleRate);
        }

        if (settings.Audio.Channels is { } channels)
        {
            builder.AudioChannels(channels);
        }
    }

    private static void CollectWarnings(
        Project project,
        ExportSettings settings,
        bool streamCopy,
        bool twoPass,
        MediaCapabilities capabilities,
        List<PlanWarning> warnings)
    {
        var sequence = project.Sequence;

        // Молча кодировать процессором вместо видеокарты нельзя: пользователь
        // ждал минуту вместо десяти и должен понимать, почему вышло иначе.
        if (settings.Video.Hardware != HardwareAcceleration.None &&
            (twoPass || ResolveHardware(settings, capabilities) == HardwareAcceleration.None))
        {
            warnings.Add(new PlanWarning(
                PlanWarningKind.HardwareUnavailable,
                twoPass
                    ? "Два прохода и подбор под размер видеокарта не умеет — кодирует процессор."
                    : "Выбранный аппаратный энкодер недоступен — кодирует процессор."));
        }

        if (settings.Video.Resolution.IsUpscale(sequence.Format.Size))
        {
            warnings.Add(new PlanWarning(
                PlanWarningKind.Upscale,
                "Разрешение выше исходного: файл станет больше, а деталей не прибавится."));
        }

        if (settings.Video.FrameRate.IsIncrease(sequence.Format.FrameRate))
        {
            warnings.Add(new PlanWarning(
                PlanWarningKind.FrameRateIncrease,
                "Частота кадров выше исходной: новые кадры будут дублироваться."));
        }

        if ((streamCopy && !sequence.Video.Clips[0].IsFullSource) ||
            (!streamCopy && CanConcatWithoutReencoding(project, settings)))
        {
            warnings.Add(new PlanWarning(
                PlanWarningKind.StreamCopyKeyframeSnap,
                "Без перекодирования границы обрезки сместятся к ближайшему ключевому кадру."));
        }

        if (settings.Subtitles.IsEmbedded && !SubtitleFormats.SupportsEmbedded(settings.Container))
        {
            warnings.Add(new PlanWarning(
                PlanWarningKind.SubtitlesDropped,
                "Этот контейнер не носит дорожку субтитров — выберите MP4, MKV или вшейте их в кадр."));
        }

        if (!settings.Audio.Enabled && sequence.HasAudio)
        {
            warnings.Add(new PlanWarning(
                PlanWarningKind.AudioDropped,
                "Звук не попадёт в результат."));
        }

        foreach (var source in project.UsedSources())
        {
            if (source.Info.PrimaryVideo?.IsVariableFrameRate == true && streamCopy)
            {
                warnings.Add(new PlanWarning(
                    PlanWarningKind.VariableFrameRateSource,
                    $"«{source.DisplayName}» записан с переменной частотой кадров: " +
                    "без перекодирования возможен рассинхрон звука."));
            }
        }

        if (settings.Video.RateControl is RateControl.Auto or RateControl.ConstantQuality && !streamCopy)
        {
            warnings.Add(new PlanWarning(
                PlanWarningKind.SizeEstimateUnavailable,
                "При постоянном качестве размер файла зависит от содержимого — оценка приблизительная."));
        }
    }

    private static ExportSummary BuildSummary(Project project, ExportSettings settings, bool streamCopy)
    {
        var sequence = project.Sequence;
        var resolution = settings.Video.Resolution.Resolve(sequence.Format.Size);
        var fps = settings.Video.FrameRate.Effective(sequence.Format.FrameRate);

        var sourceVideo = project.UsedSources().FirstOrDefault()?.Info.PrimaryVideo;

        long? videoBitrate;
        string bitrateLabel;

        if (streamCopy)
        {
            videoBitrate = sourceVideo?.BitrateBps;
            bitrateLabel = "как в исходнике";
        }
        else
        {
            var rateControl = settings.Video.RateControl;
            videoBitrate = RateControlPolicy.EstimateVideoBitrateBps(
                rateControl, settings.Video.Codec, resolution, fps);

            bitrateLabel = rateControl switch
            {
                RateControl.ConstantBitrate constant => $"{constant.Kbps} кбит/с",
                RateControl.ConstantQuality quality => $"качество CRF {quality.Crf}",
                RateControl.TargetSize target => $"целевой размер {target.Bytes / 1024} КБ",
                _ => $"авто (CRF {RateControlPolicy.CrfRange(settings.Video.Codec).Default})"
            };
        }

        var audioEnabled = settings.Audio.Enabled && sequence.HasAudio;
        var audioBitrate = audioEnabled ? settings.Audio.BitrateKbps * 1000L : 0L;

        var audioLabel = audioEnabled
            ? (streamCopy
                ? "как в исходнике"
                : $"{settings.Audio.Codec.DisplayName()}, {settings.Audio.BitrateKbps} кбит/с")
            : "без звука";

        var codecLabel = streamCopy
            ? $"{sourceVideo?.CodecName ?? "как в исходнике"} (копирование)"
            : settings.Video.Codec.DisplayName();

        return new ExportSummary(
            resolution,
            fps,
            codecLabel,
            bitrateLabel,
            audioLabel,
            sequence.Duration,
            sequence.ClipCount,
            OutputSizeEstimator.Estimate(videoBitrate, audioBitrate, sequence.Duration),
            streamCopy);
    }

    /// <summary>
    /// Готовит путь результата. Перезапись чужого файла без спроса — самая обидная
    /// ошибка видеоредактора, поэтому по умолчанию имя дополняется номером.
    /// </summary>
    private static string ResolveOutputPath(ExportSettings settings)
    {
        var path = settings.OutputPath;

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new Core.Diagnostics.EditOperationException("Не указан файл результата.");
        }

        if (settings.Overwrite != OverwritePolicy.AutoRename || !File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return path;
    }
}
