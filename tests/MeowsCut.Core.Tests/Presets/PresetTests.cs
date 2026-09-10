using FluentAssertions;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Export;
using MeowsCut.Core.Presets;
using Microsoft.Extensions.Logging.Abstractions;
using static MeowsCut.Core.Tests.Editing.Fake;

namespace MeowsCut.Core.Tests.Presets;

/// <summary>
/// Пресеты поставляются данными: провайдер обязан читать их из ресурсов
/// и переживать чужие и битые файлы.
/// </summary>
public class JsonPresetProviderTests
{
    private static JsonPresetProvider Provider() =>
        new(new AppPaths(), NullLogger<JsonPresetProvider>.Instance);

    [Fact]
    public void Built_in_groups_are_loaded()
    {
        var groups = Provider().Groups;

        groups.Select(group => group.Id).Should().Contain(["telegram", "general"]);
    }

    [Fact]
    public void Telegram_group_has_the_sticker_preset()
    {
        var presets = Provider().GetPresets("telegram");

        presets.Should().NotBeEmpty();
        presets.Should().Contain(preset => preset.Id == "telegram.video-sticker");
    }

    [Fact]
    public void Sticker_limits_come_from_data_not_code()
    {
        var sticker = Provider().Find("telegram.video-sticker");

        sticker.Should().NotBeNull();
        sticker!.Constraints.MaxDuration.Should().Be(TimeSpan.FromSeconds(3));
        sticker.Constraints.MaxFileSizeBytes.Should().Be(256_000);
        sticker.Constraints.RequireSquare.Should().BeTrue();
        sticker.Constraints.ForbidAudio.Should().BeTrue();
        sticker.Template.Width.Should().Be(512);
        sticker.Template.VideoCodec.Should().Be(VideoCodec.Vp9);
    }

    [Fact]
    public void Presets_are_ordered_for_display()
    {
        var presets = Provider().GetPresets("telegram");

        presets.Select(preset => preset.Order).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Unknown_preset_is_simply_absent()
    {
        Provider().Find("telegram.не-существует").Should().BeNull();
    }
}

/// <summary>Применение пресета к проекту и настройкам.</summary>
public class PresetApplierTests
{
    private static readonly IPresetProvider Presets =
        new JsonPresetProvider(new AppPaths(), NullLogger<JsonPresetProvider>.Instance);

    private static readonly PresetApplier Applier = new(new PresetValidator());

    private static PresetDefinition Sticker => Presets.Find("telegram.video-sticker")!;

    private static Project Project(double seconds = 10) => Core.Editing.Project.FromMedia(Info(seconds));

    [Fact]
    public void Sticker_preset_sets_format_codec_and_square_frame()
    {
        var result = Applier.Apply(Sticker, Project(2), ExportSettings.Default);

        result.Settings.Container.Should().Be(ContainerFormat.WebM);
        result.Settings.Video.Codec.Should().Be(VideoCodec.Vp9);
        result.Settings.Video.Resolution.Should().BeOfType<ResolutionSpec.Custom>();
        result.Settings.Video.Resolution.Resolve(result.Project.Sequence.Format.Size)
            .Should().Be(new Core.Media.FrameSize(512, 512));
    }

    [Fact]
    public void Sticker_preset_removes_audio_and_says_so()
    {
        var result = Applier.Apply(Sticker, Project(2), ExportSettings.Default);

        result.Settings.Audio.Enabled.Should().BeFalse();
        result.Changes.Should().Contain(change => change.Description.Contains("Звук", StringComparison.Ordinal));
    }

    [Fact]
    public void Long_video_is_trimmed_to_the_limit_by_default()
    {
        var result = Applier.Apply(Sticker, Project(10), ExportSettings.Default);

        result.Project.Sequence.Duration.Should().Be(TimeSpan.FromSeconds(3));
        result.Changes.Should().Contain(change => change.Description.Contains("Обрезано", StringComparison.Ordinal));
        result.Validation.IsSatisfied.Should().BeTrue();
    }

    [Fact]
    public void Long_video_can_be_sped_up_instead_of_trimmed()
    {
        var result = Applier.Apply(Sticker, Project(9), ExportSettings.Default, DurationFitMode.SpeedUp);

        result.Project.Sequence.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(50));
        result.Project.Sequence.Video.Clips[0].Speed.Should().BeApproximately(3d, 0.01);
        result.Changes.Should().Contain(change => change.Description.Contains("Ускорено", StringComparison.Ordinal));
    }

    [Fact]
    public void Short_video_is_left_alone()
    {
        var result = Applier.Apply(Sticker, Project(2), ExportSettings.Default);

        result.Project.Sequence.Duration.Should().Be(TimeSpan.FromSeconds(2));
        result.Changes.Should().NotContain(change => change.Description.Contains("Обрезано", StringComparison.Ordinal));
    }

    [Fact]
    public void Size_limit_becomes_a_target_size_rate_control()
    {
        var result = Applier.Apply(Sticker, Project(2), ExportSettings.Default);

        result.Settings.Video.RateControl.Should().BeOfType<RateControl.TargetSize>()
            .Which.Bytes.Should().Be(256_000);
    }

    [Fact]
    public void Preset_disables_stream_copy()
    {
        var settings = ExportSettings.Default with { PreferStreamCopy = true };

        var result = Applier.Apply(Sticker, Project(2), settings);

        result.Settings.PreferStreamCopy.Should().BeFalse("копирование потока не даст ни нужный кодек, ни размер");
    }

    [Fact]
    public void Applying_a_preset_never_changes_things_silently()
    {
        var result = Applier.Apply(Sticker, Project(10), ExportSettings.Default);

        result.Changes.Should().NotBeEmpty();
        result.Changes.Should().OnlyContain(change => change.Description.Length > 0);
    }

    [Fact]
    public void Video_message_preset_keeps_audio()
    {
        var preset = Presets.Find("telegram.video-message")!;

        var result = Applier.Apply(preset, Project(20), ExportSettings.Default);

        result.Settings.Audio.Enabled.Should().BeTrue();
        result.Settings.Audio.Channels.Should().Be(1);
        result.Settings.Container.Should().Be(ContainerFormat.Mp4);
    }
}

/// <summary>Проверка требований площадки.</summary>
public class PresetValidatorTests
{
    private static readonly PresetValidator Validator = new();

    private static readonly PresetConstraints StickerLimits = new()
    {
        MaxDuration = TimeSpan.FromSeconds(3),
        MaxFileSizeBytes = 256_000,
        MaxWidth = 512,
        MaxHeight = 512,
        RequireSquare = true,
        MaxFps = 30,
        ForbidAudio = true,
        AllowedContainers = [ContainerFormat.WebM],
        AllowedVideoCodecs = [VideoCodec.Vp9]
    };

    private static ExportSettings GoodSettings() => ExportSettings.Default with
    {
        Container = ContainerFormat.WebM,
        Video = VideoSettings.Default with
        {
            Codec = VideoCodec.Vp9,
            Resolution = new ResolutionSpec.Custom(512, 512, FitMode.Cover),
            FrameRate = new FrameRateSpec.Fixed(30),
            RateControl = new RateControl.TargetSize(256_000)
        },
        Audio = AudioSettings.Disabled
    };

    [Fact]
    public void Correct_settings_pass()
    {
        var result = Validator.Validate(StickerLimits, Project.FromMedia(Info(2)), GoodSettings());

        result.IsSatisfied.Should().BeTrue();
        result.Violations.Should().BeEmpty();
    }

    [Fact]
    public void Too_long_video_is_an_error_with_a_fix()
    {
        var result = Validator.Validate(StickerLimits, Project.FromMedia(Info(10)), GoodSettings());

        result.IsSatisfied.Should().BeFalse();
        result.Violations.Should().Contain(v => v.AutoFix == AutoFixKind.TrimDuration);
    }

    [Fact]
    public void Non_square_frame_is_rejected()
    {
        var settings = GoodSettings() with
        {
            Video = GoodSettings().Video with { Resolution = new ResolutionSpec.Custom(512, 288, FitMode.Contain) }
        };

        var result = Validator.Validate(StickerLimits, Project.FromMedia(Info(2)), settings);

        result.Violations.Should().Contain(v => v.AutoFix == AutoFixKind.FixResolution);
    }

    [Fact]
    public void Audio_left_on_is_rejected()
    {
        var settings = GoodSettings() with { Audio = AudioSettings.Default };

        var result = Validator.Validate(StickerLimits, Project.FromMedia(Info(2)), settings);

        result.Violations.Should().Contain(v => v.AutoFix == AutoFixKind.DisableAudio);
    }

    [Fact]
    public void Frame_rate_above_the_limit_is_rejected()
    {
        var settings = GoodSettings() with
        {
            Video = GoodSettings().Video with { FrameRate = new FrameRateSpec.Fixed(60) }
        };

        var result = Validator.Validate(StickerLimits, Project.FromMedia(Info(2)), settings);

        result.Violations.Should().Contain(v => v.AutoFix == AutoFixKind.FixFrameRate);
    }

    [Fact]
    public void Wrong_container_is_rejected()
    {
        var settings = GoodSettings() with { Container = ContainerFormat.Mp4 };

        var result = Validator.Validate(StickerLimits, Project.FromMedia(Info(2)), settings);

        result.IsSatisfied.Should().BeFalse();
    }

    [Fact]
    public void Unlimited_size_is_a_warning_not_an_error()
    {
        var settings = GoodSettings() with
        {
            Video = GoodSettings().Video with { RateControl = new RateControl.ConstantQuality(31) }
        };

        var result = Validator.Validate(StickerLimits, Project.FromMedia(Info(2)), settings);

        // Файла ещё нет, размер точно неизвестен — запрещать экспорт из-за этого нельзя.
        result.Violations.Should().Contain(v => v.Severity == ViolationSeverity.Warning);
        result.IsSatisfied.Should().BeTrue();
    }

    [Fact]
    public void Size_limit_larger_than_allowed_is_an_error()
    {
        var settings = GoodSettings() with
        {
            Video = GoodSettings().Video with { RateControl = new RateControl.TargetSize(1_000_000) }
        };

        var result = Validator.Validate(StickerLimits, Project.FromMedia(Info(2)), settings);

        result.IsSatisfied.Should().BeFalse();
        result.Violations.Should().Contain(v => v.AutoFix == AutoFixKind.LimitFileSize);
    }

    [Fact]
    public void Empty_constraints_never_complain()
    {
        var result = Validator.Validate(new PresetConstraints(), Project.FromMedia(Info(600)), ExportSettings.Default);

        result.HasAnything.Should().BeFalse();
    }
}
