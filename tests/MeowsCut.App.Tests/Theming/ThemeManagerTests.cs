using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using MeowsCut.App.Theming;
using MeowsCut.App.Tests.Views;
using MeowsCut.Core.Configuration;

namespace MeowsCut.App.Tests.Theming;

[Collection("WPF")]
public class ThemeManagerTests
{
    [Fact]
    public void Light_theme_puts_light_brushes_into_resources() => WpfRunner.Run(() =>
    {
        var resources = Resources();
        using var manager = new ThemeManager(resources);

        manager.Apply(AppTheme.Light);

        var background = (SolidColorBrush)resources["Brush.Background"];
        var text = (SolidColorBrush)resources["Brush.Text"];

        Brightness(background.Color).Should().BeGreaterThan(0.8, "фон светлой темы должен быть светлым");
        Brightness(text.Color).Should().BeLessThan(0.2, "текст на светлом фоне должен быть тёмным");
        manager.Effective.Should().Be(AppTheme.Light);
    });

    [Fact]
    public void Dark_theme_comes_back() => WpfRunner.Run(() =>
    {
        var resources = Resources();
        using var manager = new ThemeManager(resources);

        manager.Apply(AppTheme.Light);
        manager.Apply(AppTheme.Dark);

        Brightness(((SolidColorBrush)resources["Brush.Background"]).Color).Should().BeLessThan(0.2);
        manager.Effective.Should().Be(AppTheme.Dark);
    });

    [Fact]
    public void System_theme_resolves_to_a_real_one() => WpfRunner.Run(() =>
    {
        using var manager = new ThemeManager(Resources());

        manager.Apply(AppTheme.System);

        manager.Effective.Should().BeOneOf(AppTheme.Dark, AppTheme.Light);
    });

    /// <summary>
    /// Свой словарь, а не ресурсы приложения: тест идёт в собственном потоке,
    /// а ресурсы принадлежат тому, где Application создали первым.
    /// </summary>
    private static ResourceDictionary Resources()
    {
        var resources = new ResourceDictionary();

        foreach (var source in new[] { "Palette.Dark.xaml", "Theme.xaml" })
        {
            resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/MeowsCut;component/Resources/{source}")
            });
        }

        return resources;
    }

    private static double Brightness(Color color) =>
        ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255;
}
