namespace MeowsCut.Core.Export;

/// <summary>
/// Редкие параметры кодирования. Вынесены отдельно, чтобы базовый экран экспорта
/// не тащил десяток полей, а расширенный не требовал своей модели.
/// </summary>
public sealed record AdvancedVideoSettings
{
    public static readonly AdvancedVideoSettings Default = new();

    public string? PixelFormat { get; init; }

    public string? Profile { get; init; }

    public string? Level { get; init; }

    /// <summary>Интервал ключевых кадров (-g). null — на усмотрение энкодера.</summary>
    public int? KeyframeIntervalFrames { get; init; }

    public bool TwoPass { get; init; }

    public string? Tune { get; init; }

    /// <summary>Ключи, которые пользователь дописал сам: «я знаю, что делаю».</summary>
    public IReadOnlyDictionary<string, string>? RawOptions { get; init; }
}

public sealed record VideoSettings
{
    public static readonly VideoSettings Default = new();

    public VideoCodec Codec { get; init; } = VideoCodec.H264;

    public RateControl RateControl { get; init; } = RateControl.Default;

    public EncodingSpeed Speed { get; init; } = EncodingSpeed.Balanced;

    /// <summary>
    /// Кодировать видеокартой. Выключено по умолчанию: быстрее, но при равном
    /// размере файла качество заметно хуже — это осознанный выбор пользователя.
    /// </summary>
    public HardwareAcceleration Hardware { get; init; } = HardwareAcceleration.None;

    public ResolutionSpec Resolution { get; init; } = ResolutionSpec.Default;

    public FrameRateSpec FrameRate { get; init; } = FrameRateSpec.Default;

    public AdvancedVideoSettings Advanced { get; init; } = AdvancedVideoSettings.Default;
}

public sealed record AudioSettings
{
    public static readonly AudioSettings Default = new();

    public static readonly AudioSettings Disabled = new() { Enabled = false };

    public bool Enabled { get; init; } = true;

    public AudioCodec Codec { get; init; } = AudioCodec.Aac;

    public int BitrateKbps { get; init; } = 192;

    /// <summary>null — как в источнике.</summary>
    public int? SampleRateHz { get; init; }

    public int? Channels { get; init; }

    /// <summary>Общая громкость поверх громкости отдельных клипов.</summary>
    public double MasterVolume { get; init; } = 1d;

    public static readonly int[] BitratePresetsKbps = [64, 96, 128, 192, 256, 320];

    public static readonly int[] SampleRatePresetsHz = [22_050, 32_000, 44_100, 48_000];
}

/// <summary>
/// Полный набор параметров вывода.
/// </summary>
public sealed record ExportSettings
{
    public static readonly ExportSettings Default = new();

    public ContainerFormat Container { get; init; } = ContainerFormat.Mp4;

    public VideoSettings Video { get; init; } = VideoSettings.Default;

    public AudioSettings Audio { get; init; } = AudioSettings.Default;

    /// <summary>Субтитры к результату: вшить в кадр или положить дорожкой.</summary>
    public SubtitleSettings Subtitles { get; init; } = SubtitleSettings.None;

    public string OutputPath { get; init; } = string.Empty;

    public OverwritePolicy Overwrite { get; init; } = OverwritePolicy.AutoRename;

    /// <summary>
    /// Разрешить копирование потоков, когда правки этого не запрещают. Это разница
    /// между секундами и минутами, но границы обрезки прилипают к ключевым кадрам.
    /// </summary>
    public bool PreferStreamCopy { get; init; }

    /// <summary>
    /// Приводит настройки к согласованному виду: кодеки, недопустимые в выбранном
    /// контейнере, заменяются на подходящие, расширение файла подгоняется под контейнер.
    /// </summary>
    public ExportSettings Normalized()
    {
        var video = Video with { Codec = CompatibilityMatrix.Coerce(Container, Video.Codec) };
        var audio = Audio with { Codec = CompatibilityMatrix.Coerce(Container, Audio.Codec) };

        var path = OutputPath;
        if (!string.IsNullOrEmpty(path) &&
            !string.Equals(Path.GetExtension(path), Container.FileExtension(), StringComparison.OrdinalIgnoreCase))
        {
            path = Path.ChangeExtension(path, Container.FileExtension());
        }

        return this with { Video = video, Audio = audio, OutputPath = path };
    }
}
