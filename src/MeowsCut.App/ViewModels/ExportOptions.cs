using MeowsCut.Core.Export;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Пункт списка разрешений. Отдельный тип нужен, чтобы в списке была подпись
/// для человека, а наружу уходила модель ядра.
/// </summary>
/// <summary>
/// Формат кадра ролика. Пропорции, а не размер: длинную сторону сохраняет
/// сама последовательность, чтобы ролик из 4K не падал молча до 1080.
/// </summary>
public sealed record SequenceFormatOption(string Title, double? Aspect)
{
    public static readonly SequenceFormatOption Original = new("Как в исходнике", null);

    public static readonly IReadOnlyList<SequenceFormatOption> All =
    [
        Original,
        new("16:9 · горизонтальный", 16d / 9d),
        new("9:16 · вертикальный", 9d / 16d),
        new("1:1 · квадрат", 1d),
        new("4:5 · лента", 4d / 5d)
    ];

    /// <summary>
    /// Какой пункт списка отвечает нынешнему формату.
    /// </summary>
    /// <remarks>
    /// Сравнение приблизительное: 1080×1920 и 1082×1920 — один и тот же
    /// вертикальный формат, и подсвечен в списке должен быть он, а не «как
    /// в исходнике» из-за округления на два пикселя.
    /// </remarks>
    public static SequenceFormatOption For(Core.Editing.Timeline.SequenceFormat format)
    {
        if (!format.IsCustom)
        {
            return Original;
        }

        foreach (var option in All)
        {
            if (option.Aspect is { } aspect && Math.Abs(aspect - format.AspectRatio) < 0.01)
            {
                return option;
            }
        }

        return Original;
    }

    public override string ToString() => Title;
}

public sealed record ResolutionOption(string Title, int? TargetHeight, bool IsCustom = false)
{
    public static readonly ResolutionOption Original = new("Как в исходнике", null);

    public static readonly ResolutionOption Custom = new("Свой размер…", null, IsCustom: true);

    public static readonly IReadOnlyList<ResolutionOption> All =
    [
        Original,
        new("4K · 2160p", 2160),
        new("1080p", 1080),
        new("720p", 720),
        new("480p", 480),
        new("360p", 360),
        Custom
    ];

    public ResolutionSpec ToSpec(int customWidth, int customHeight, FitMode fit) => this switch
    {
        { IsCustom: true } => new ResolutionSpec.Custom(customWidth, customHeight, fit),
        { TargetHeight: { } height } => new ResolutionSpec.Preset(height),
        _ => new ResolutionSpec.Original()
    };

    public override string ToString() => Title;
}

/// <summary>Пункт списка частоты кадров.</summary>
public sealed record FrameRateOption(string Title, double? Fps, bool IsCustom = false)
{
    public static readonly FrameRateOption Original = new("Как в исходнике", null);

    public static readonly FrameRateOption Custom = new("Своя…", null, IsCustom: true);

    public static readonly IReadOnlyList<FrameRateOption> All =
    [
        Original,
        new("24", 24),
        new("25", 25),
        new("30", 30),
        new("50", 50),
        new("60", 60),
        Custom
    ];

    public FrameRateSpec ToSpec(double customFps) => this switch
    {
        { IsCustom: true } => new FrameRateSpec.Fixed(customFps),
        { Fps: { } fps } => new FrameRateSpec.Fixed(fps),
        _ => new FrameRateSpec.Original()
    };

    public override string ToString() => Title;
}

/// <summary>Способ задания качества: то, что в интерфейсе называется своими именами.</summary>
public enum QualityMode
{
    /// <summary>Подобрать самому.</summary>
    Auto = 0,

    /// <summary>Постоянное качество (CRF): размер заранее неизвестен.</summary>
    ConstantQuality,

    /// <summary>Фиксированный битрейт.</summary>
    Bitrate,

    /// <summary>Уложиться в размер.</summary>
    TargetSize
}

public sealed record QualityModeOption(QualityMode Mode, string Title)
{
    public static readonly IReadOnlyList<QualityModeOption> All =
    [
        new(QualityMode.Auto, "Авто"),
        new(QualityMode.ConstantQuality, "Постоянное качество (CRF)"),
        new(QualityMode.Bitrate, "Битрейт"),
        new(QualityMode.TargetSize, "Уложиться в размер")
    ];

    public override string ToString() => Title;
}

public sealed record EncodingSpeedOption(EncodingSpeed Speed, string Title)
{
    public static readonly IReadOnlyList<EncodingSpeedOption> All =
    [
        new(EncodingSpeed.VeryFast, "Очень быстро"),
        new(EncodingSpeed.Fast, "Быстро"),
        new(EncodingSpeed.Balanced, "Баланс"),
        new(EncodingSpeed.Slow, "Медленно, меньше файл")
    ];

    public override string ToString() => Title;
}

public sealed record SubtitleModeOption(SubtitleMode Mode, string Title)
{
    public static readonly IReadOnlyList<SubtitleModeOption> All =
    [
        new(SubtitleMode.Burn, Localization.Strings.SubtitleModeBurn),
        new(SubtitleMode.Embed, Localization.Strings.SubtitleModeEmbed)
    ];

    public override string ToString() => Title;
}

public sealed record FitModeOption(FitMode Mode, string Title)
{
    public static readonly IReadOnlyList<FitModeOption> All =
    [
        new(FitMode.Contain, "Вписать (с полями)"),
        new(FitMode.Cover, "Заполнить (обрезать)"),
        new(FitMode.Stretch, "Растянуть")
    ];

    public override string ToString() => Title;
}
