namespace MeowsCut.Core.Abstractions;

/// <summary>
/// Кадры-миниатюры для таймлайна и предпросмотра.
/// </summary>
/// <remarks>
/// Возвращает путь к файлу в кэше, а не картинку в памяти: кадров на таймлайне
/// бывают сотни, и держать их все в памяти незачем — этим занимается система.
/// </remarks>
public interface IThumbnailService
{
    /// <summary>
    /// Кадр исходного файла в указанной позиции. Возвращает null, если кадр
    /// извлечь не удалось — это не повод рушить интерфейс.
    /// </summary>
    Task<string?> GetFrameAsync(string sourcePath, TimeSpan position, int width, CancellationToken cancellationToken);

    /// <summary>Убирает старые кадры, если кэш перерос ограничение.</summary>
    void TrimCache(long limitBytes);
}
