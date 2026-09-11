using System.Windows;
using System.Windows.Media;
using MeowsCut.Core.Configuration;
using Microsoft.Win32;

namespace MeowsCut.App.Theming;

/// <summary>
/// Применяет палитру к приложению и следит за темой Windows.
/// </summary>
/// <remarks>
/// Кисти в ресурсах заменяются новыми, а разметка ссылается на них через
/// DynamicResource — иначе смена темы до уже открытых окон не доехала бы.
/// Перекрасить существующие кисти нельзя: WPF замораживает те, что пришли
/// из разметки, и присваивание цвета такой кисти падает.
/// </remarks>
public sealed class ThemeManager : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightTheme = "AppsUseLightTheme";

    /// <summary>Ключ, по которому среди словарей узнаётся палитра.</summary>
    private const string PaletteMarker = "Color.Background";

    private readonly ResourceDictionary _resources;
    private AppTheme _requested = AppTheme.Dark;
    private bool _watchingSystem;

    public ThemeManager(ResourceDictionary resources) => _resources = resources;

    /// <summary>Тема, которая сейчас на экране: «системная» уже разрешена в светлую или тёмную.</summary>
    public AppTheme Effective { get; private set; } = AppTheme.Dark;

    public void Apply(AppTheme theme)
    {
        _requested = theme;
        WatchSystem(theme == AppTheme.System);
        Repaint(theme == AppTheme.System ? ReadSystemTheme() : theme);
    }

    private void Repaint(AppTheme theme)
    {
        var palette = new ResourceDictionary
        {
            Source = new Uri(
                $"pack://application:,,,/MeowsCut;component/Resources/Palette.{(theme == AppTheme.Light ? "Light" : "Dark")}.xaml")
        };

        // Старую палитру убираем, новую ставим на её место: словарь узнаём по цвету,
        // который есть только в палитрах. Остальные словари — стили и значки — на месте.
        var merged = _resources.MergedDictionaries;
        var previous = merged.FirstOrDefault(dictionary => dictionary.Contains(PaletteMarker));

        if (previous is not null)
        {
            merged[merged.IndexOf(previous)] = palette;
        }
        else
        {
            merged.Insert(0, palette);
        }

        Effective = theme;

        // Доска монтажа рисует себя сама и кисти берёт в момент отрисовки:
        // без этого она осталась бы в цветах прошлой темы до первого движения мышью.
        // Окна принадлежат своему потоку — из чужого до них не дотянуться.
        if (Application.Current is { } application && application.CheckAccess())
        {
            foreach (var window in application.Windows.OfType<Window>())
            {
                Redraw(window);
            }
        }
    }

    private static void Redraw(DependencyObject node)
    {
        if (node is UIElement element)
        {
            element.InvalidateVisual();
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            Redraw(VisualTreeHelper.GetChild(node, i));
        }
    }

    /// <summary>
    /// Тема Windows из реестра. Ключа может не быть вовсе — на сборках, где
    /// персонализация недоступна, Windows считает тему тёмной.
    /// </summary>
    private static AppTheme ReadSystemTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);

        return key?.GetValue(AppsUseLightTheme) is int value && value != 0
            ? AppTheme.Light
            : AppTheme.Dark;
    }

    private void WatchSystem(bool enabled)
    {
        if (enabled == _watchingSystem)
        {
            return;
        }

        if (enabled)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
        else
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }

        _watchingSystem = enabled;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle))
        {
            return;
        }

        // Событие приходит не из потока интерфейса, а кисти принадлежат ему.
        _ = Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_requested == AppTheme.System)
            {
                Repaint(ReadSystemTheme());
            }
        });
    }

    public void Dispose() => WatchSystem(false);
}
