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

            CleanPreviewFragments(threshold);
            CleanTitleTexts(threshold);

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

    /// <summary>
    /// Фрагменты предпросмотра живут дольше своей задачи — пользователь их смотрит,
    /// поэтому удаляются они не сразу, а при следующем запуске.
    /// </summary>
    /// <summary>
    /// Тексты надписей, которые больше никому не нужны.
    /// </summary>
    /// <remarks>
    /// Файлы именуются отпечатком текста и переживают экспорт намеренно:
    /// пересборка плана находит их на месте. Значит, убирать их некому,
    /// кроме этой уборки по возрасту.
    /// </remarks>
    private void CleanTitleTexts(DateTime threshold)
    {
        var directory = Path.Combine(paths.TempDirectory, TitleTextStore.FolderName);

        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.txt"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) <= threshold)
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Текст надписи {File} занят, пропускаем", file);
            }
        }
    }

    private void CleanPreviewFragments(DateTime threshold)
    {
        if (!Directory.Exists(paths.PreviewDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(paths.PreviewDirectory))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) <= threshold)
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Фрагмент {File} занят, пропускаем", file);
            }
        }
    }
}
