namespace MeowsCut.Core.Export;

/// <summary>Как субтитры попадают в файл.</summary>
public enum SubtitleMode
{
    /// <summary>
    /// Вшить в картинку. Видны везде, включая соцсети и телефоны, но выключить
    /// их уже нельзя и текст пересжимается вместе с кадром.
    /// </summary>
    Burn = 0,

    /// <summary>
    /// Отдельной дорожкой. Зритель включает и выключает их сам, качество текста
    /// не страдает — но нужен плеер, который эту дорожку покажет.
    /// </summary>
    Embed
}

/// <summary>
/// Субтитры к результату.
/// </summary>
/// <remarks>
/// Файл берётся готовый — SRT или ASS. Своего редактора субтитров в приложении нет
/// и не планируется: их пишут в специальных программах, а сюда приносят готовыми.
/// </remarks>
public sealed record SubtitleSettings
{
    public static readonly SubtitleSettings None = new();

    /// <summary>Путь к файлу субтитров. null — субтитров нет.</summary>
    public string? FilePath { get; init; }

    public SubtitleMode Mode { get; init; } = SubtitleMode.Burn;

    /// <summary>
    /// Оформление для SRT: у него нет своих стилей, и без этого ffmpeg рисует
    /// текст шрифтом по умолчанию. У ASS оформление своё, и сюда лезть не нужно.
    /// </summary>
    public string? ForceStyle { get; init; }

    public bool Enabled => !string.IsNullOrWhiteSpace(FilePath);

    public bool IsBurnedIn => Enabled && Mode == SubtitleMode.Burn;

    public bool IsEmbedded => Enabled && Mode == SubtitleMode.Embed;
}

public static class SubtitleFormats
{
    /// <summary>
    /// Кодек дорожки субтитров для контейнера. null — контейнер их не носит,
    /// и остаётся только вшивать.
    /// </summary>
    public static string? SubtitleCodec(ContainerFormat container) => container switch
    {
        ContainerFormat.Mp4 or ContainerFormat.Mov => "mov_text",
        ContainerFormat.Mkv => "srt",
        ContainerFormat.WebM => "webvtt",
        _ => null
    };

    public static bool SupportsEmbedded(ContainerFormat container) => SubtitleCodec(container) is not null;

    /// <summary>Оформление по умолчанию для SRT: белый текст с чёрной обводкой читается на любом кадре.</summary>
    public const string DefaultStyle = "FontName=Arial,FontSize=24,PrimaryColour=&H00FFFFFF,OutlineColour=&H80000000,BorderStyle=1,Outline=2,Shadow=0";
}
