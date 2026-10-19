namespace MeowsCut.Core.Configuration;

public enum AppTheme
{
    Dark = 0,
    Light,
    System
}

/// <summary>
/// Пользовательские настройки. Иммутабельны: изменение — это создание новой записи
/// через <c>with</c> и сохранение через <see cref="IAppSettingsStore"/>.
/// </summary>
public sealed record AppSettings
{
    public static readonly AppSettings Default = new();

    /// <summary>Явно указанная папка с ffmpeg. Переопределяет автопоиск.</summary>
    public string? FfmpegDirectory { get; init; }

    /// <summary>Папка для результатов. null — рядом с исходным файлом.</summary>
    public string? DefaultOutputDirectory { get; init; }

    public string OutputNameTemplate { get; init; } = "{name}_meows{ext}";

    public AppTheme Theme { get; init; } = AppTheme.Dark;

    public string Language { get; init; } = "ru";

    public bool VerboseLogging { get; init; }

    public int MaxParallelJobs { get; init; } = 1;

    public long ThumbnailCacheLimitBytes { get; init; } = 500L * 1024 * 1024;

    public IReadOnlyList<string> RecentFiles { get; init; } = [];

    /// <summary>Микрофон для озвучки. null — системный по умолчанию.</summary>
    public string? RecordingDeviceId { get; init; }

    /// <summary>
    /// Глушить звук ролика, пока идёт запись. Без наушников динамики попадают
    /// в микрофон, и в озвучке слышно эхо исходника.
    /// </summary>
    public bool MuteWhileRecording { get; init; } = true;

    /// <summary>
    /// Поправка синхронности озвучки в миллисекундах. Положительная сдвигает
    /// голос раньше — на случай, если микрофон этой машины отдаёт звук с задержкой.
    /// </summary>
    public int RecordingOffsetMs { get; init; }

    /// <summary>Убирать шум микрофона из дубля сразу после записи.</summary>
    public bool ReduceRecordingNoise { get; init; } = true;

    /// <summary>
    /// Насколько приглушать шум, в децибелах. Больше — тише фон, но голос
    /// начинает звучать как из-под воды.
    /// </summary>
    public int RecordingNoiseReductionDb { get; init; } = 20;

    /// <summary>Раскладка окна с прошлого запуска. null — заводская.</summary>
    public PanelLayout? Layout { get; init; }

    public AppSettings WithRecentFile(string path, int limit = 10)
    {
        var list = new List<string> { path };
        list.AddRange(RecentFiles.Where(x => !string.Equals(x, path, StringComparison.OrdinalIgnoreCase)));

        return this with { RecentFiles = list.Take(limit).ToArray() };
    }
}
