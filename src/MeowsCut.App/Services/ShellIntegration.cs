using System.Diagnostics;
using System.IO;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace MeowsCut.App.Services;

/// <summary>
/// Открытие файла и папки в проводнике.
/// </summary>
public sealed class ShellIntegration(ILogger<ShellIntegration> logger) : IShellIntegration
{
    public void OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void RevealInExplorer(string path)
    {
        if (File.Exists(path))
        {
            // Аргументы проводника: /select требует путь без кавычек в ArgumentList.
            Start(new ProcessStartInfo("explorer.exe")
            {
                ArgumentList = { "/select,", path },
                UseShellExecute = true
            });

            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (directory is not null && Directory.Exists(directory))
        {
            Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
    }

    private void Start(ProcessStartInfo startInfo)
    {
        try
        {
            using var process = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "Не удалось открыть {Target}", startInfo.FileName);
        }
    }
}

/// <summary>
/// Имя файла результата по шаблону из настроек.
/// </summary>
public static class OutputNameBuilder
{
    public static string Build(string sourcePath, AppSettings settings, string extension)
    {
        var directory = settings.DefaultOutputDirectory is { Length: > 0 } configured
            ? configured
            : Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();

        var name = settings.OutputNameTemplate
            .Replace("{name}", Path.GetFileNameWithoutExtension(sourcePath), StringComparison.Ordinal)
            .Replace("{ext}", extension, StringComparison.Ordinal);

        if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            name += extension;
        }

        return Path.Combine(directory, name);
    }
}
