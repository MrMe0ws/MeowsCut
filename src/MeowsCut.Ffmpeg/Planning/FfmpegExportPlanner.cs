using System.Globalization;
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
public sealed class FfmpegExportPlanner : IExportPlanner
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
        var streamCopy = CanStreamCopy(project, settings, warnings);

        var arguments = streamCopy
            ? BuildStreamCopyArguments(project, settings, partPath)
            : BuildTranscodeArguments(project, settings, capabilities, partPath);

        CollectWarnings(project, settings, streamCopy, warnings);

        var summary = BuildSummary(project, settings, streamCopy);

        var stage = new ExportStage(
            streamCopy ? StageKind.Remux : StageKind.Transcode,
            streamCopy ? "Копирование без перекодирования" : "Обработка видео",
            arguments,
            Weight: 1d,
            sequence.Duration,
            partPath);

        return new ExportPlan([stage], sequence.Duration, summary, warnings, outputPath);
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
        string outputPath)
    {
        var sequence = project.Sequence;
        var sources = project.UsedSources();

        var indexBySource = new Dictionary<SourceId, int>();
        for (var i = 0; i < sources.Count; i++)
        {
            indexBySource[sources[i].Id] = i;
        }

        var graph = _graphBuilder.Build(sequence, indexBySource, settings);

        var builder = FfmpegArgumentBuilder.Create()
            .HideBanner()
            .OverwriteOutput()
            .NoStdin()
            .LogLevel("error")
            .ProgressToStdout();

        foreach (var source in sources)
        {
            builder.Input(source.FilePath);
        }

        builder.FilterComplex(graph.Text);
        builder.Map($"[{graph.VideoLabel}]");

        if (graph.AudioLabel is { } audioLabel)
        {
            builder.Map($"[{audioLabel}]");
        }

        AppendVideoOptions(builder, settings, capabilities);

        if (graph.HasAudio)
        {
            AppendAudioOptions(builder, settings);
        }
        else
        {
            builder.NoAudio();
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
        var encoder = EncoderCatalog.ResolveVideoEncoder(codec, capabilities);

        builder.VideoCodec(encoder);

        var rateControl = RateControlPolicy.Resolve(settings.Video.RateControl, codec);

        switch (rateControl)
        {
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

        foreach (var (key, value) in EncoderCatalog.SpeedOptions(codec, encoder, settings.Video.Speed))
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
        List<PlanWarning> warnings)
    {
        var sequence = project.Sequence;

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

        if (streamCopy && !sequence.Video.Clips[0].IsFullSource)
        {
            warnings.Add(new PlanWarning(
                PlanWarningKind.StreamCopyKeyframeSnap,
                "Без перекодирования границы обрезки сместятся к ближайшему ключевому кадру."));
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
