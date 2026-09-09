using MeowsCut.Core.Media;

namespace MeowsCut.Core.Abstractions;

/// <summary>
/// Чтение характеристик медиафайла. Единственный способ для UI узнать что-либо о файле.
/// </summary>
public interface IMediaProbe
{
    /// <summary>
    /// Читает характеристики файла. Бросает <see cref="Diagnostics.MediaProbeException"/>,
    /// если файл не удалось разобрать, и <see cref="Diagnostics.UnsupportedMediaException"/>,
    /// если в нём нет пригодного для монтажа видео.
    /// </summary>
    Task<MediaInfo> ProbeAsync(string filePath, CancellationToken cancellationToken);
}
