using System.IO;
using MeowsCut.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Раскладка окна: окно запоминает размеры панелей, но в файл настроек ходит
/// через модель — представление сервисов не знает.
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>Раскладка с прошлого запуска. null — заводская.</summary>
    public PanelLayout? SavedLayout => _settingsStore.Current.Layout;

    /// <summary>Сохраняет раскладку, если она поменялась.</summary>
    public async Task SaveLayoutAsync(PanelLayout layout)
    {
        if (layout == _settingsStore.Current.Layout)
        {
            return;
        }

        try
        {
            await _settingsStore
                .SaveAsync(_settingsStore.Current with { Layout = layout }, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Не запомнили раскладку — не повод мешать работе: в худшем случае
            // следующий запуск откроется с заводскими размерами.
            _logger.LogWarning(ex, "Не удалось сохранить раскладку окна");
        }
    }
}
