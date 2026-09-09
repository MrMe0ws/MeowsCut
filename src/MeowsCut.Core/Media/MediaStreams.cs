namespace MeowsCut.Core.Media;

/// <summary>
/// Информация о контейнере файла (секция format у ffprobe).
/// </summary>
public sealed record ContainerInfo(
    string FormatName,
    string FormatLongName,
    long? BitrateBps);

/// <summary>
/// Видеопоток. Поля, которых может не быть у части файлов, объявлены nullable намеренно:
/// у многих контейнеров нет битрейта потока, у VFR-видео нет осмысленного avg_frame_rate.
/// </summary>
public sealed record VideoStreamInfo(
    int Index,
    string CodecName,
    string? CodecLongName,
    string? Profile,
    string PixelFormat,
    FrameSize Size,
    Rational FrameRate,
    Rational AverageFrameRate,
    Rational SampleAspectRatio,
    long? BitrateBps,
    int RotationDegrees,
    bool HasAlpha,
    TimeSpan Duration)
{
    /// <summary>
    /// Расхождение номинальной и средней частоты кадров — признак VFR.
    /// Для нарезки без перекодирования это повод предупредить о рассинхроне.
    /// </summary>
    public bool IsVariableFrameRate =>
        !FrameRate.IsZero && !AverageFrameRate.IsZero &&
        Math.Abs(FrameRate.Value - AverageFrameRate.Value) > 0.5;

    /// <summary>
    /// Размер кадра с учётом поворота из метаданных: снятое вертикально видео
    /// хранится горизонтально плюс тег rotate.
    /// </summary>
    public FrameSize DisplaySize =>
        RotationDegrees is 90 or 270 or -90 or -270
            ? new FrameSize(Size.Height, Size.Width)
            : Size;
}

/// <summary>
/// Аудиопоток.
/// </summary>
public sealed record AudioStreamInfo(
    int Index,
    string CodecName,
    string? CodecLongName,
    int Channels,
    string? ChannelLayout,
    int SampleRateHz,
    long? BitrateBps,
    TimeSpan Duration,
    string? Language);

/// <summary>
/// Поток субтитров. В v1 не обрабатывается, но показывается в информации о файле.
/// </summary>
public sealed record SubtitleStreamInfo(
    int Index,
    string CodecName,
    string? Language,
    string? Title);
