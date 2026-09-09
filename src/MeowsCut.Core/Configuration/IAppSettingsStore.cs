namespace MeowsCut.Core.Configuration;

/// <summary>
/// Хранилище настроек с кэшем в памяти. Реализация обязана переживать битый файл:
/// настройки — не повод не запуститься.
/// </summary>
public interface IAppSettingsStore
{
    AppSettings Current { get; }

    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);

    event EventHandler<AppSettings>? Changed;
}
