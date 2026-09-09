using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace MeowsCut.Core.Configuration;

/// <summary>
/// Настройки в JSON рядом с профилем пользователя. Запись атомарная: сначала во временный
/// файл, потом замена — иначе выключение питания посреди сохранения оставит пустой файл.
/// </summary>
public sealed class JsonAppSettingsStore(AppPaths paths, ILogger<JsonAppSettingsStore> logger) : IAppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppSettings Current { get; private set; } = AppSettings.Default;

    public event EventHandler<AppSettings>? Changed;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(paths.SettingsFile))
            {
                Current = AppSettings.Default;
                return Current;
            }

            await using var stream = File.OpenRead(paths.SettingsFile);
            var loaded = await JsonSerializer
                .DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            Current = loaded ?? AppSettings.Default;
            logger.LogInformation("Настройки загружены из {Path}", paths.SettingsFile);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Не удалось прочитать настройки, используются значения по умолчанию");
            BackupCorruptedFile();
            Current = AppSettings.Default;
        }
        finally
        {
            _gate.Release();
        }

        return Current;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(paths.SettingsFile)!);

            var tempFile = paths.SettingsFile + ".tmp";
            await using (var stream = File.Create(tempFile))
            {
                await JsonSerializer
                    .SerializeAsync(stream, settings, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (File.Exists(paths.SettingsFile))
            {
                File.Replace(tempFile, paths.SettingsFile, null);
            }
            else
            {
                File.Move(tempFile, paths.SettingsFile);
            }

            Current = settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Не удалось сохранить настройки в {Path}", paths.SettingsFile);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, Current);
    }

    private void BackupCorruptedFile()
    {
        try
        {
            var backup = paths.SettingsFile + ".corrupted";
            File.Copy(paths.SettingsFile, backup, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Не удалось сохранить копию повреждённых настроек");
        }
    }
}
