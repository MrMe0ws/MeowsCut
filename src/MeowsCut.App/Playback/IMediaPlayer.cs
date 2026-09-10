namespace MeowsCut.App.Playback;

/// <summary>
/// Проигрыватель одного файла.
/// </summary>
/// <remarks>
/// Отдельный интерфейс нужен по двум причинам: логику воспроизведения
/// последовательности можно проверить тестами без окна, и сам проигрыватель
/// потом заменяется (кандидат — встроенный libmpv с кадроточной перемоткой),
/// не задевая ничего вокруг.
/// </remarks>
public interface IMediaPlayer
{
    /// <summary>Путь открытого файла или null, если ничего не открыто.</summary>
    string? Source { get; }

    /// <summary>Позиция внутри исходного файла.</summary>
    TimeSpan Position { get; set; }

    /// <summary>Множитель скорости воспроизведения.</summary>
    double SpeedRatio { get; set; }

    /// <summary>Громкость от 0 до 1.</summary>
    double Volume { get; set; }

    bool IsPlaying { get; }

    void Open(string path);

    void Play();

    void Pause();

    void Close();

    /// <summary>Файл готов: до этого момента позицию задавать бесполезно.</summary>
    event EventHandler? Opened;

    /// <summary>Файл не открылся — интерфейс должен перейти на покадровый предпросмотр.</summary>
    event EventHandler<Exception>? Failed;
}
