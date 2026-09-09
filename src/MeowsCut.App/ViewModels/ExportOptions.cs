using MeowsCut.Core.Export;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Пункт списка разрешений. Отдельный тип нужен, чтобы в списке была подпись
/// для человека, а наружу уходила модель ядра.
/// </summary>
public sealed record ResolutionOption(string Title, int? TargetHeight)
{
    public static readonly ResolutionOption Original = new("Как в исходнике", null);

    public static readonly IReadOnlyList<ResolutionOption> All =
    [
        Original,
        new("4K · 2160p", 2160),
        new("1080p", 1080),
        new("720p", 720),
        new("480p", 480),
        new("360p", 360)
    ];

    public ResolutionSpec ToSpec() =>
        TargetHeight is { } height ? new ResolutionSpec.Preset(height) : new ResolutionSpec.Original();

    public override string ToString() => Title;
}

/// <summary>Пункт списка частоты кадров.</summary>
public sealed record FrameRateOption(string Title, double? Fps)
{
    public static readonly FrameRateOption Original = new("Как в исходнике", null);

    public static readonly IReadOnlyList<FrameRateOption> All =
    [
        Original,
        new("24", 24),
        new("25", 25),
        new("30", 30),
        new("50", 50),
        new("60", 60)
    ];

    public FrameRateSpec ToSpec() =>
        Fps is { } fps ? new FrameRateSpec.Fixed(fps) : new FrameRateSpec.Original();

    public override string ToString() => Title;
}
