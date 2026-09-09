using MeowsCut.Core.Media;

namespace MeowsCut.Core.Abstractions;

/// <summary>
/// Откуда взялись бинарники ffmpeg. Показывается в настройках, чтобы было понятно,
/// какая именно сборка используется, когда их в системе несколько.
/// </summary>
public enum ToolsetSource
{
    NotFound = 0,
    UserSettings,
    ApplicationFolder,
    LocalAppData,
    EnvironmentVariable,
    SystemPath,
    WellKnownLocation
}

/// <summary>
/// Найденный и проверенный набор инструментов ffmpeg.
/// </summary>
public sealed record MediaToolset(
    string FfmpegPath,
    string FfprobePath,
    string Version,
    ToolsetSource Source,
    MediaCapabilities Capabilities)
{
    public string Directory => Path.GetDirectoryName(FfmpegPath) ?? string.Empty;
}

/// <summary>
/// Результат поиска: либо готовый набор, либо причина, по которой его нет.
/// </summary>
public sealed record ToolsetLocationResult(
    bool Found,
    MediaToolset? Toolset,
    string? FailureReason)
{
    public static ToolsetLocationResult Success(MediaToolset toolset) => new(true, toolset, null);

    public static ToolsetLocationResult Failure(string reason) => new(false, null, reason);
}

/// <summary>
/// Поиск ffmpeg/ffprobe. Пути никогда не хардкодятся в остальном коде — только здесь.
/// </summary>
public interface IMediaToolsetLocator
{
    Task<ToolsetLocationResult> LocateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Проверяет конкретную папку, указанную пользователем в настройках.
    /// </summary>
    Task<ToolsetLocationResult> ValidateDirectoryAsync(string directory, CancellationToken cancellationToken);
}

/// <summary>
/// Доступ к текущему набору инструментов для сервисов, которым он нужен во время работы.
/// </summary>
public interface IMediaToolsetProvider
{
    MediaToolset? Current { get; }

    bool IsReady => Current is not null;

    MediaToolset Require();

    void Set(MediaToolset toolset);

    event EventHandler? Changed;
}
