using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using MeowsCut.App.Localization;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Раздел «О программе»: версия, из чего собрано и где лежит.
/// </summary>
/// <remarks>
/// Версию спрашивают, когда что-то пошло не так или вышла новая, — поэтому рядом
/// версия FFmpeg, страница выпусков и папка с данными, где лежат логи и черновики.
/// </remarks>
public sealed partial class SettingsViewModel
{
    private static readonly Uri ProjectPage = new("https://github.com/MrMe0ws/MeowsCut");

    private static readonly Uri ReleasesPage = new("https://github.com/MrMe0ws/MeowsCut/releases/latest");

    /// <summary>Версия сборки без хвоста с хешем коммита: «2.0.0», а не «2.0.0+316d45c…».</summary>
    public string AppVersion
    {
        get
        {
            var assembly = typeof(SettingsViewModel).Assembly;
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                          ?? assembly.GetName().Version?.ToString(3)
                          ?? string.Empty;

            var plus = version.IndexOf('+', StringComparison.Ordinal);
            return string.Format(Strings.AboutVersion, plus > 0 ? version[..plus] : version);
        }
    }

    /// <summary>На чём работает: среда .NET и найденный FFmpeg.</summary>
    public string AboutComponents =>
        string.Format(
            Strings.AboutComponents,
            Environment.Version.ToString(3),
            _toolsetProvider.Current?.Version ?? Strings.AboutFfmpegMissing);

    public string AboutCopyright =>
        typeof(SettingsViewModel).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright is { Length: > 0 } copyright
            ? $"{Strings.AppTitle} {copyright}"
            : Strings.AppTitle;

    [RelayCommand]
    private void OpenProjectPage() => _shell.OpenWebPage(ProjectPage);

    [RelayCommand]
    private void OpenReleasesPage() => _shell.OpenWebPage(ReleasesPage);

    [RelayCommand]
    private void OpenAppFolder() => _shell.RevealInExplorer(_paths.ApplicationDirectory.TrimEnd('\\', '/') + "\\");

    [RelayCommand]
    private void OpenDataFolder() => _shell.RevealInExplorer(_paths.LocalRoot.TrimEnd('\\', '/') + "\\");
}
