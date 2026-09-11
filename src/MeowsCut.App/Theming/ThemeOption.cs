using MeowsCut.App.Localization;
using MeowsCut.Core.Configuration;

namespace MeowsCut.App.Theming;

/// <summary>Пункт списка тем: значение настройки и его подпись для человека.</summary>
public sealed record ThemeOption(AppTheme Value, string Title)
{
    public static IReadOnlyList<ThemeOption> All { get; } =
    [
        new(AppTheme.Dark, Strings.ThemeDark),
        new(AppTheme.Light, Strings.ThemeLight),
        new(AppTheme.System, Strings.ThemeSystem)
    ];

    public static ThemeOption For(AppTheme theme) =>
        All.FirstOrDefault(option => option.Value == theme) ?? All[0];
}
