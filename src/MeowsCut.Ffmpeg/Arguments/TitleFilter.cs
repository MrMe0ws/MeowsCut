using System.Globalization;
using System.Text;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Ffmpeg.Arguments;

/// <summary>
/// Надпись поверх кадра — фильтром drawtext.
/// </summary>
/// <remarks>
/// Текст берётся из файла, а не пишется в строку фильтра. Строку ffmpeg
/// разбирает в несколько уровней, и апостроф в слове «дом'ой» закрывает
/// кавычку значения: остаток фильтра уезжает внутрь текста, и весь граф
/// рассыпается с сообщением «No option name near…», по которому причину
/// не угадать. Проверено на живом ffmpeg: ни одна форма экранирования
/// апострофа внутри кавычек не переживает соседства с двоеточием.
/// Файл снимает вопрос целиком — в нём любые символы обычные.
///
/// <c>expansion=none</c> по той же причине: иначе знак процента и <c>%{…}</c>
/// в тексте drawtext считает своими подстановками и падает на «Stray %».
/// </remarks>
public static class TitleFilter
{
    /// <summary>
    /// Шрифты, которые есть в любой Windows.
    /// </summary>
    /// <remarks>
    /// Segoe UI первым: это системный шрифт интерфейса, и надпись им выглядит
    /// как часть Windows, а не как заголовок из девяностых. Arial — запасной
    /// вариант для урезанных сборок.
    /// </remarks>
    private static readonly string[] FontCandidates =
    [
        "segoeui.ttf",
        "arial.ttf",
        "tahoma.ttf",
        "verdana.ttf"
    ];

    /// <summary>Цвет плашки под текстом: чёрный, чуть прозрачный.</summary>
    private const string BackdropColor = "black@0.55";

    /// <summary>Имя фильтра. По нему проверяется, умеет ли сборка ffmpeg надписи.</summary>
    public const string FilterName = "drawtext";

    /// <summary>
    /// Строит цепочку фильтров для надписей, текст которых уже лежит в файлах.
    /// </summary>
    /// <param name="titles">Надписи; пустые и без файла пропускаются.</param>
    /// <param name="textFiles">Где лежит текст каждой надписи.</param>
    /// <param name="fontFile">Путь к файлу шрифта или null — тогда берётся системный.</param>
    public static IEnumerable<string> Build(
        IReadOnlyList<TitleClip> titles,
        IReadOnlyDictionary<TitleId, string> textFiles,
        string? fontFile = null)
    {
        var font = fontFile ?? FindFont();

        foreach (var title in titles)
        {
            if (title.IsEmpty || !textFiles.TryGetValue(title.Id, out var textFile))
            {
                continue;
            }

            yield return Build(title, textFile, font);
        }
    }

    private static string Build(TitleClip title, string textFile, string? fontFile)
    {
        var builder = new StringBuilder(FilterName).Append('=');

        builder
            .Append("textfile=").Append(Path(textFile))
            .Append(":expansion=none");

        if (fontFile is not null)
        {
            builder.Append(":fontfile=").Append(Path(fontFile));
        }

        // Размер задаётся долей высоты кадра, а не пикселями: одна и та же
        // надпись в 1080p и в вертикальном 4K обязана выглядеть одинаково.
        builder
            .Append(":fontsize=h*").Append(Number(title.Scale))
            .Append(":fontcolor=").Append(Quote(ColorOf(title.Color)));

        if (title.Backdrop)
        {
            builder
                .Append(":box=1")
                .Append(":boxcolor=").Append(Quote(BackdropColor))
                .Append(":boxborderw=h*").Append(Number(title.Scale * 0.25));
        }

        var margin = Number(title.Margin);

        builder
            .Append(":x=").Append(Quote(HorizontalPosition(title.Anchor, margin)))
            .Append(":y=").Append(Quote(VerticalPosition(title.Anchor, margin)));

        // Надпись видна только в свой отрезок: без enable она стояла бы весь ролик.
        builder
            .Append(":enable=")
            .Append(Quote($"between(t,{Seconds(title.TimelineStart)},{Seconds(title.TimelineEnd)})"));

        return builder.ToString();
    }

    private static string HorizontalPosition(TitleAnchor anchor, string margin) => anchor switch
    {
        TitleAnchor.TopLeft or TitleAnchor.MiddleLeft or TitleAnchor.BottomLeft => $"h*{margin}",
        TitleAnchor.TopRight or TitleAnchor.MiddleRight or TitleAnchor.BottomRight => $"w-tw-h*{margin}",
        _ => "(w-tw)/2"
    };

    private static string VerticalPosition(TitleAnchor anchor, string margin) => anchor switch
    {
        TitleAnchor.TopLeft or TitleAnchor.TopCenter or TitleAnchor.TopRight => $"h*{margin}",
        TitleAnchor.BottomLeft or TitleAnchor.BottomCenter or TitleAnchor.BottomRight => $"h-th-h*{margin}",
        _ => "(h-th)/2"
    };

    /// <summary>
    /// Цвет для ffmpeg: #RRGGBB превращается в 0xRRGGBB.
    /// </summary>
    /// <remarks>
    /// Решётку ffmpeg не понимает вовсе, а имена цветов вроде «white» приходят
    /// из пресетов — их пропускаем как есть.
    /// </remarks>
    private static string ColorOf(string color)
    {
        var value = color.Trim();

        if (value.StartsWith('#') && value.Length is 7 or 9)
        {
            return "0x" + value[1..];
        }

        return string.IsNullOrEmpty(value) ? "white" : value;
    }

    /// <summary>
    /// Путь в строке фильтра: прямые косые и экранированное двоеточие.
    /// </summary>
    /// <remarks>
    /// Обратная косая сама служит экраном, и «C:\Windows» превратилось бы
    /// в «C:Windows». Двоеточие после буквы диска разбирается как разделитель
    /// параметров даже внутри кавычек — проверено на живом ffmpeg.
    /// </remarks>
    private static string Path(string path) =>
        Quote(path.Replace('\\', '/').Replace(":", "\\:", StringComparison.Ordinal));

    /// <summary>Значение в кавычках: они защищают от запятой, которая делит фильтры.</summary>
    private static string Quote(string value) => "'" + value + "'";

    private static string Number(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Seconds(TimeSpan value) =>
        value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// Первый существующий шрифт из списка.
    /// </summary>
    /// <remarks>
    /// Без fontfile drawtext на Windows обычно не находит шрифт вовсе
    /// и отказывается работать: fontconfig в эти сборки не кладут.
    /// </remarks>
    public static string? FindFont()
    {
        var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

        if (string.IsNullOrEmpty(fonts))
        {
            return null;
        }

        foreach (var candidate in FontCandidates)
        {
            var path = System.IO.Path.Combine(fonts, candidate);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
