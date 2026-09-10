using MeowsCut.Core.Export;

namespace MeowsCut.Core.Presets;

/// <summary>Группа пресетов: Telegram, общие и что появится дальше.</summary>
public sealed record PresetGroup(string Id, string Title, string? Description, int Order);

/// <summary>
/// Что пресет выставляет. Незаданные поля оставляют текущее значение —
/// пресет задаёт то, что важно для площадки, и не трогает остальное.
/// </summary>
public sealed record PresetTemplate
{
    public ContainerFormat? Container { get; init; }

    public VideoCodec? VideoCodec { get; init; }

    public int? CrfOverride { get; init; }

    public int? BitrateKbps { get; init; }

    public long? TargetSizeBytes { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public FitMode? Fit { get; init; }

    public double? Fps { get; init; }

    public string? PixelFormat { get; init; }

    public bool? AudioEnabled { get; init; }

    public AudioCodec? AudioCodec { get; init; }

    public int? AudioBitrateKbps { get; init; }

    public int? AudioSampleRateHz { get; init; }

    public int? AudioChannels { get; init; }
}

/// <summary>
/// Требования площадки. Отделены от шаблона намеренно: шаблон говорит, что выставить,
/// ограничения — что проверить. Одно меняется без другого, и валидатор работает
/// даже когда пользователь потом правил настройки руками.
/// </summary>
public sealed record PresetConstraints
{
    public TimeSpan? MaxDuration { get; init; }

    public long? MaxFileSizeBytes { get; init; }

    public int? MaxWidth { get; init; }

    public int? MaxHeight { get; init; }

    public bool RequireSquare { get; init; }

    public double? MaxFps { get; init; }

    public bool ForbidAudio { get; init; }

    public IReadOnlyList<ContainerFormat>? AllowedContainers { get; init; }

    public IReadOnlyList<VideoCodec>? AllowedVideoCodecs { get; init; }
}

/// <summary>
/// Готовый пресет: подпись для человека, шаблон настроек и ограничения площадки.
/// </summary>
public sealed record PresetDefinition(
    string Id,
    string GroupId,
    string Title,
    string Description,
    int Order,
    PresetTemplate Template,
    PresetConstraints Constraints,
    IReadOnlyList<string> Notes);
