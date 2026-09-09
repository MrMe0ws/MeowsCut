using MeowsCut.Core.Media;

namespace MeowsCut.Core.Editing;

/// <summary>
/// Файл, добавленный в проект. Сам файл только читается: приложение никогда
/// не изменяет исходники.
/// </summary>
public sealed record MediaSource(SourceId Id, MediaInfo Info)
{
    public static MediaSource FromMedia(MediaInfo info) => new(SourceId.New(), info);

    public string DisplayName => Info.FileName;

    public string FilePath => Info.FilePath;

    public TimeSpan Duration => Info.Duration;

    public bool HasAudio => Info.HasAudio;
}
