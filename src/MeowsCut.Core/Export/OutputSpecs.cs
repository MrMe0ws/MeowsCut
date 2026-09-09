using MeowsCut.Core.Media;

namespace MeowsCut.Core.Export;

/// <summary>Как вписывать кадр, если пропорции цели не совпадают с исходником.</summary>
public enum FitMode
{
    /// <summary>Вписать целиком, добавив поля.</summary>
    Contain = 0,

    /// <summary>Заполнить кадр, обрезав лишнее.</summary>
    Cover,

    /// <summary>Растянуть, игнорируя пропорции.</summary>
    Stretch
}

/// <summary>
/// Разрешение результата.
/// </summary>
public abstract record ResolutionSpec
{
    public sealed record Original : ResolutionSpec;

    /// <summary>Пресет по высоте: 2160, 1080, 720, 480, 360.</summary>
    public sealed record Preset(int TargetHeight) : ResolutionSpec;

    public sealed record Custom(int Width, int Height, FitMode Fit = FitMode.Contain) : ResolutionSpec;

    public static readonly ResolutionSpec Default = new Original();

    public static readonly int[] PresetHeights = [2160, 1080, 720, 480, 360];

    /// <summary>
    /// Итоговый размер кадра. Всегда чётный: yuv420p не принимает нечётные стороны,
    /// и ffmpeg падает на этом уже в конце длинного экспорта.
    /// </summary>
    public FrameSize Resolve(FrameSize sequenceSize)
    {
        if (sequenceSize.IsEmpty)
        {
            return sequenceSize;
        }

        return this switch
        {
            Original => sequenceSize.RoundedToEven(),
            Preset preset => sequenceSize.ScaledToHeight(preset.TargetHeight),
            Custom custom => new FrameSize(custom.Width, custom.Height).RoundedToEven(),
            _ => sequenceSize.RoundedToEven()
        };
    }

    /// <summary>Апскейл разрешён, но о нём стоит предупредить: качества он не добавит.</summary>
    public bool IsUpscale(FrameSize sequenceSize)
    {
        var target = Resolve(sequenceSize);
        return target.Width > sequenceSize.Width || target.Height > sequenceSize.Height;
    }
}

/// <summary>
/// Частота кадров результата.
/// </summary>
public abstract record FrameRateSpec
{
    public sealed record Original : FrameRateSpec;

    public sealed record Fixed(double Fps) : FrameRateSpec;

    public static readonly FrameRateSpec Default = new Original();

    public static readonly double[] Presets = [24, 25, 30, 50, 60];

    /// <summary>null — оставить как есть, не добавляя фильтр fps.</summary>
    public double? Resolve(Rational sequenceFrameRate) => this switch
    {
        Fixed fixedRate => fixedRate.Fps,
        _ => null
    };

    public double Effective(Rational sequenceFrameRate) =>
        Resolve(sequenceFrameRate) ?? (sequenceFrameRate.IsZero ? 30d : sequenceFrameRate.Value);

    public bool IsIncrease(Rational sequenceFrameRate) =>
        Resolve(sequenceFrameRate) is { } fps && !sequenceFrameRate.IsZero && fps > sequenceFrameRate.Value + 0.01;
}
