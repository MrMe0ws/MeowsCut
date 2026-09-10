namespace MeowsCut.Core.Abstractions;

/// <summary>
/// Картинка звуковой волны для полосы звука на доске монтажа.
/// </summary>
/// <remarks>
/// Волна строится один раз на весь файл и кладётся в кэш на диск: проход ffmpeg
/// по часовому треку стоит секунд, а кусок этого трека на доске может меняться
/// десятки раз в минуту. Доска берёт из готовой картинки нужный отрезок.
///
/// Возвращается путь к файлу, а не картинка в памяти — по тем же причинам,
/// что и у <see cref="IThumbnailService"/>.
/// </remarks>
public interface IWaveformService
{
    /// <summary>
    /// Волна всего файла. Возвращает null, если построить её не удалось —
    /// это не повод рушить доску: без волны кусок звука всё равно виден.
    /// </summary>
    Task<string?> GetWaveformAsync(string sourcePath, CancellationToken cancellationToken);
}
