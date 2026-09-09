using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace MeowsCut.Ffmpeg.Temp;

/// <summary>
/// Рабочая папка одной задачи. Удаляется при любом исходе, включая отмену и падение.
/// </summary>
internal sealed class TempWorkspace(string directory, ILogger logger) : ITempWorkspace
{
    public string Directory => directory;

    public string GetFilePath(string fileName) => Path.Combine(directory, fileName);

    public ValueTask DisposeAsync()
    {
        try
        {
            if (System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Не смертельно: папку заберёт уборщик сирот при следующем запуске.
            logger.LogWarning(ex, "Не удалось удалить временную папку {Directory}", directory);
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Создаёт рабочие папки задач и убирает те, что остались после аварийного завершения.
/// </summary>
public sealed class TempWorkspaceFactory(AppPaths paths, ILogger<TempWorkspaceFactory> logger)
    : ITempWorkspaceFactory
{
    public ITempWorkspace Create()
    {
        var directory = Path.Combine(paths.TempDirectory, "job-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        return new TempWorkspace(directory, logger);
    }

    public void CleanOrphans(TimeSpan olderThan)
    {
        try
        {
            if (!Directory.Exists(paths.TempDirectory))
            {
                return;
            }

            var threshold = DateTime.UtcNow - olderThan;

            foreach (var directory in Directory.EnumerateDirectories(paths.TempDirectory, "job-*"))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(directory) > threshold)
                    {
                        continue;
                    }

                    Directory.Delete(directory, recursive: true);
                    logger.LogInformation("Удалена осиротевшая временная папка {Directory}", directory);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogDebug(ex, "Папка {Directory} занята, пропускаем", directory);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Не удалось убрать временные папки");
        }
    }
}
