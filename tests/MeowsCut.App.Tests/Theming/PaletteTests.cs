using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using MeowsCut.App.Tests.Views;

namespace MeowsCut.App.Tests.Theming;

/// <summary>
/// Палитры тем: состав и читаемость.
/// </summary>
/// <remarks>
/// Та же коллекция, что у остальных тестов разметки: Application в домене
/// может быть только один, и параллельный запуск роняет тот тест, что опоздал.
/// </remarks>
[Collection("WPF")]
public class PaletteTests
{
    [Fact]
    public void Both_palettes_define_the_same_names() => WpfRunner.Run(() =>
    {
        // Пропущенный ключ оставил бы на экране цвет от прошлой темы —
        // одну белую панель посреди тёмного окна заметят не сразу.
        var dark = Keys(Load("Palette.Dark.xaml"));
        var light = Keys(Load("Palette.Light.xaml"));

        light.Should().BeEquivalentTo(dark);
    });

    [Theory]
    [InlineData("Palette.Dark.xaml")]
    [InlineData("Palette.Light.xaml")]
    public void Text_stays_readable_on_its_background(string palette) => WpfRunner.Run(() =>
    {
        var colors = Load(palette);

        var surface = Color(colors, "Color.Surface");
        var background = Color(colors, "Color.Background");

        // 7:1 — уровень AAA для основного текста. Приглушённый текст читается
        // не хуже: подписи полей в этом редакторе несут смысл, а не украшают.
        Contrast(Color(colors, "Color.Text"), surface).Should().BeGreaterThan(7);
        Contrast(Color(colors, "Color.Text"), background).Should().BeGreaterThan(7);
        Contrast(Color(colors, "Color.TextMuted"), surface).Should().BeGreaterThan(7);

        // Надпись на акцентной кнопке — 4.5:1, уровень AA для обычного текста.
        Contrast(Color(colors, "Color.OnAccent"), Color(colors, "Color.Accent")).Should().BeGreaterThan(4.5);
        Contrast(Color(colors, "Color.OnAccent"), Color(colors, "Color.AccentHover")).Should().BeGreaterThan(4.5);
    });

    [Theory]
    [InlineData("Palette.Dark.xaml")]
    [InlineData("Palette.Light.xaml")]
    public void Panels_are_told_apart_from_the_window(string palette) => WpfRunner.Run(() =>
    {
        // Доска монтажа рисует клип поверх дорожки: слившись, они превращаются
        // в одно пятно, и границы кусков перестают читаться.
        var colors = Load(palette);

        Contrast(Color(colors, "Color.SurfaceRaised"), Color(colors, "Color.Background"))
            .Should().BeGreaterThan(1.08);
    });

    private static ResourceDictionary Load(string name) => new()
    {
        Source = new Uri($"pack://application:,,,/MeowsCut;component/Resources/{name}")
    };

    private static IEnumerable<string> Keys(ResourceDictionary palette) =>
        palette.Keys.OfType<string>().Where(key => key.StartsWith("Color.", StringComparison.Ordinal));

    private static Color Color(ResourceDictionary palette, string key)
    {
        palette.Contains(key).Should().BeTrue($"в палитре обязан быть {key}");
        return (Color)palette[key]!;
    }

    private static double Contrast(Color first, Color second)
    {
        var a = Luminance(first);
        var b = Luminance(second);

        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var part = value / 255d;
            return part <= 0.03928 ? part / 12.92 : Math.Pow((part + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }
}
