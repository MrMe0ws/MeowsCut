using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Export;
using MeowsCut.Core.Media;

namespace MeowsCut.Core.Presets;

/// <summary>
/// Применяет пресет к проекту и настройкам вывода.
/// </summary>
/// <remarks>
/// Каждое изменение попадает в список: пресет трогает не только вывод, но и монтаж —
/// например, укорачивает последовательность под лимит длительности. Пользователь
/// должен видеть, что именно с его проектом произошло.
/// </remarks>
public sealed class PresetApplier(IPresetValidator validator) : IPresetApplier
{
    public PresetApplyResult Apply(
        PresetDefinition preset,
        Project project,
        ExportSettings settings,
        DurationFitMode durationFit = DurationFitMode.Trim)
    {
        var changes = new List<PresetChange>();
        var template = preset.Template;

        var container = template.Container ?? settings.Container;
        var video = settings.Video;
        var audio = settings.Audio;

        if (template.Container is { } newContainer && newContainer != settings.Container)
        {
            changes.Add(new PresetChange($"Формат: {newContainer}"));
        }

        if (template.VideoCodec is { } codec && codec != video.Codec)
        {
            video = video with { Codec = codec };
            changes.Add(new PresetChange($"Видеокодек: {codec.DisplayName()}"));
        }

        video = ApplyRateControl(template, video, changes);
        video = ApplyResolution(template, video, project, changes);
        video = ApplyFrameRate(template, video, project, changes);

        if (template.PixelFormat is { Length: > 0 } pixelFormat)
        {
            video = video with { Advanced = video.Advanced with { PixelFormat = pixelFormat } };
        }

        audio = ApplyAudio(template, audio, changes);

        var updatedProject = ApplyDurationLimit(preset.Constraints, project, durationFit, changes);

        var result = settings with
        {
            Container = container,
            Video = video,
            Audio = audio,

            // Пресет всегда точный: копирование потоков ломает и размер, и кодек.
            PreferStreamCopy = false
        };

        result = result.Normalized();

        return new PresetApplyResult(
            updatedProject,
            result,
            changes,
            validator.Validate(preset.Constraints, updatedProject, result));
    }

    private static VideoSettings ApplyRateControl(
        PresetTemplate template,
        VideoSettings video,
        List<PresetChange> changes)
    {
        if (template.TargetSizeBytes is { } bytes)
        {
            changes.Add(new PresetChange($"Ограничение размера: {bytes / 1024} КБ"));
            return video with { RateControl = new RateControl.TargetSize(bytes) };
        }

        if (template.BitrateKbps is { } kbps)
        {
            changes.Add(new PresetChange($"Битрейт: {kbps} кбит/с"));
            return video with { RateControl = new RateControl.ConstantBitrate(kbps) };
        }

        if (template.CrfOverride is { } crf)
        {
            changes.Add(new PresetChange($"Качество: CRF {crf}"));
            return video with { RateControl = new RateControl.ConstantQuality(crf) };
        }

        return video;
    }

    private static VideoSettings ApplyResolution(
        PresetTemplate template,
        VideoSettings video,
        Project project,
        List<PresetChange> changes)
    {
        if (template.Width is not { } width || template.Height is not { } height)
        {
            return video;
        }

        var fit = template.Fit ?? FitMode.Cover;
        var current = video.Resolution.Resolve(project.Sequence.Format.Size);
        var target = new FrameSize(width, height);

        if (current != target)
        {
            changes.Add(new PresetChange($"Разрешение: {target}"));
        }

        return video with { Resolution = new ResolutionSpec.Custom(width, height, fit) };
    }

    private static VideoSettings ApplyFrameRate(
        PresetTemplate template,
        VideoSettings video,
        Project project,
        List<PresetChange> changes)
    {
        if (template.Fps is not { } fps)
        {
            return video;
        }

        var current = video.FrameRate.Effective(project.Sequence.Format.FrameRate);
        if (Math.Abs(current - fps) > 0.01)
        {
            changes.Add(new PresetChange($"Частота кадров: {fps:0.##}"));
        }

        return video with { FrameRate = new FrameRateSpec.Fixed(fps) };
    }

    private static AudioSettings ApplyAudio(
        PresetTemplate template,
        AudioSettings audio,
        List<PresetChange> changes)
    {
        if (template.AudioEnabled is false)
        {
            if (audio.Enabled)
            {
                changes.Add(new PresetChange("Звук выключен"));
            }

            return AudioSettings.Disabled;
        }

        if (template.AudioCodec is { } codec && codec != audio.Codec)
        {
            audio = audio with { Codec = codec };
            changes.Add(new PresetChange($"Аудиокодек: {codec.DisplayName()}"));
        }

        if (template.AudioBitrateKbps is { } bitrate && bitrate != audio.BitrateKbps)
        {
            audio = audio with { BitrateKbps = bitrate };
            changes.Add(new PresetChange($"Битрейт звука: {bitrate} кбит/с"));
        }

        if (template.AudioSampleRateHz is { } sampleRate)
        {
            audio = audio with { SampleRateHz = sampleRate };
        }

        if (template.AudioChannels is { } channels)
        {
            audio = audio with { Channels = channels };
        }

        return audio;
    }

    /// <summary>
    /// Приводит длительность к лимиту площадки — обрезкой или ускорением.
    /// </summary>
    /// <remarks>
    /// Обрезка сохраняет темп, ускорение сохраняет содержание. Для стикера из
    /// десятисекундного ролика оба варианта осмысленны, поэтому выбор оставлен
    /// пользователю, а не зашит в пресет.
    /// </remarks>
    private static Project ApplyDurationLimit(
        PresetConstraints constraints,
        Project project,
        DurationFitMode mode,
        List<PresetChange> changes)
    {
        if (mode == DurationFitMode.Keep)
        {
            return project;
        }

        if (constraints.MaxDuration is not { } limit || project.Sequence.Duration <= limit)
        {
            return project;
        }

        if (mode == DurationFitMode.SpeedUp)
        {
            var factor = project.Sequence.Duration.TotalSeconds / limit.TotalSeconds;
            var clips = project.Sequence.Video.Clips
                .Select(clip => clip.WithSpeed(clip.Speed * factor))
                .ToArray();

            changes.Add(new PresetChange(
                $"Ускорено в {factor:0.##}× — чтобы уложиться в {limit.TotalSeconds:0.#} с"));

            return project.WithSequence(project.Sequence.WithTrack(new VideoTrack(clips)));
        }

        var sliced = project.Sequence.Slice(new TimeRange(TimeSpan.Zero, limit));

        changes.Add(new PresetChange($"Обрезано до {limit.TotalSeconds:0.#} с"));
        return project.WithSequence(sliced);
    }
}
