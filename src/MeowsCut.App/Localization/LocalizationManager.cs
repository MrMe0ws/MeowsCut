using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace MeowsCut.App.Localization;

/// <summary>
/// Доступ к строкам интерфейса по ключу. Нужен для XAML: текста в разметке быть не должно,
/// а через один вход проще будет добавить английский и переключение языка на лету.
/// </summary>
public static class LocalizationManager
{
    private static readonly ResourceManager Resources =
        new("MeowsCut.App.Localization.Strings", typeof(LocalizationManager).Assembly);

    public static CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;

    public static void UseCulture(string cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            return;
        }

        Culture = CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.CurrentUICulture = Culture;
    }

    public static string Get(string key) =>
        Resources.GetString(key, Culture) ?? key;
}

/// <summary>
/// Разметка вида Text="{loc:Tr EmptyStateTitle}".
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => LocalizationManager.Get(Key);
}
