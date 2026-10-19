namespace MeowsCut.Core.Abstractions;

/// <summary>
/// Шумоподавление записанного голоса: шипение микрофона, гул комнаты, вентилятор.
/// </summary>
/// <remarks>
/// Работает с файлом, а не с потоком записи: дубль сначала сохраняется как есть,
/// и чистый вариант пишется рядом. Сырой файл не трогается — если подавление
/// съело что-то лишнее, исходник цел.
/// </remarks>
public interface INoiseReducer
{
    /// <summary>
    /// Пишет в <paramref name="outputPath"/> запись без постоянного шума.
    /// Время звука не сдвигается: озвучка остаётся под той же картинкой.
    /// </summary>
    /// <param name="strengthDb">Насколько приглушить шум, в децибелах.</param>
    /// <returns>false, если убирать нечего — запись и так чистая, файл не создан.</returns>
    Task<bool> ReduceAsync(string inputPath, string outputPath, int strengthDb, CancellationToken cancellationToken);
}
