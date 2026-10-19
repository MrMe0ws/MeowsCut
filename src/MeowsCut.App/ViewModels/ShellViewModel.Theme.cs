using System.IO;
using CommunityToolkit.Mvvm.Input;
using MeowsCut.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Тема одним нажатием: кнопка в строке меню и пункты меню «Вид».
/// </summary>
/// <remarks>
/// Раньше тему меняли только в настройках, три клика вглубь — а переключают её
/// по свету в комнате, по ходу работы. Выбор сразу записывается в настройки,
/// чтобы следующий запуск открылся в той же теме.
/// </remarks>
public sealed partial class ShellViewModel
{
    /// <summary>На экране тёмная тема — кнопка показывает солнце, то есть «сделать светлой».</summary>
    public bool IsDarkTheme => _themes.Effective != AppTheme.Light;

    [RelayCommand]
    private Task ToggleTheme() => SetTheme(IsDarkTheme ? AppTheme.Light : AppTheme.Dark);

    [RelayCommand]
    private async Task SetTheme(AppTheme theme)
    {
        _themes.Apply(theme);
        OnPropertyChanged(nameof(IsDarkTheme));

        if (_settingsStore.Current.Theme == theme)
        {
            return;
        }

        try
        {
            await _settingsStore
                .SaveAsync(_settingsStore.Current with { Theme = theme }, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Тема уже на экране; не запомнилась — значит, следующий запуск
            // откроется в прежней. Мешать работе из-за этого незачем.
            _logger.LogWarning(ex, "Не удалось сохранить тему");
        }
    }
}
