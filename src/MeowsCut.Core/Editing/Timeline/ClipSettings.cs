namespace MeowsCut.Core.Editing.Timeline;

/// <summary>
/// Звук клипа. В v1 аудио связано с видео: режется вместе, скорость общая,
/// отдельно управляются только громкость и включение.
/// </summary>
public sealed record ClipAudio(bool Enabled, double Volume)
{
    public const double MinVolume = 0d;
    public const double MaxVolume = 4d;

    public static readonly ClipAudio Default = new(true, 1d);

    public static readonly ClipAudio Muted = new(false, 1d);

    public bool IsDefault => Enabled && Math.Abs(Volume - 1d) < 0.0001;

    public ClipAudio WithVolume(double volume) =>
        this with { Volume = Math.Clamp(volume, MinVolume, MaxVolume) };
}

/// <summary>
/// Как кадр клипа ложится в кадр последовательности: масштаб и смещение.
/// Нужно уже в v1 для стикеров Telegram — там произвольное видео приводится
/// к квадрату 512×512, и пользователь выбирает, какую часть кадра оставить.
/// </summary>
public sealed record ClipTransform(double Zoom, double OffsetX, double OffsetY)
{
    public const double MinZoom = 0.1;
    public const double MaxZoom = 10d;

    /// <summary>Без изменений: кадр вписан целиком, без смещения.</summary>
    public static readonly ClipTransform Identity = new(1d, 0d, 0d);

    public bool IsIdentity =>
        Math.Abs(Zoom - 1d) < 0.0001 &&
        Math.Abs(OffsetX) < 0.0001 &&
        Math.Abs(OffsetY) < 0.0001;

    /// <summary>Смещения заданы в долях кадра: 0.5 — сдвиг на половину ширины.</summary>
    public ClipTransform WithZoom(double zoom) =>
        this with { Zoom = Math.Clamp(zoom, MinZoom, MaxZoom) };

    public ClipTransform WithOffset(double offsetX, double offsetY) =>
        this with { OffsetX = offsetX, OffsetY = offsetY };
}
