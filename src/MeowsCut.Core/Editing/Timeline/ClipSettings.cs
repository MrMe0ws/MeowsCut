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

    /// <summary>Без изменений: кадр вписан целиком, без смещения и поворота.</summary>
    public static readonly ClipTransform Identity = new(1d, 0d, 0d);

    /// <summary>
    /// Поворот кадра по часовой стрелке: 0, 90, 180 или 270 градусов.
    /// </summary>
    /// <remarks>
    /// Произвольного угла нет намеренно. Поворот здесь — это «снято боком,
    /// разверни», а не творческий приём: любой угол кроме прямого оставляет
    /// в кадре чёрные клинья и требует отдельного разговора о том, чем их закрыть.
    /// </remarks>
    public int Rotation { get; init; }

    public bool IsRotated => Rotation != 0;

    /// <summary>Меняет ли поворот местами ширину и высоту кадра.</summary>
    public bool SwapsDimensions => Rotation is 90 or 270;

    public bool IsIdentity =>
        Rotation == 0 &&
        Math.Abs(Zoom - 1d) < 0.0001 &&
        Math.Abs(OffsetX) < 0.0001 &&
        Math.Abs(OffsetY) < 0.0001;

    /// <summary>
    /// Поворот с приведением к четырём положениям: −90 и 270 — одно и то же,
    /// а 450 приходит от кнопки, нажатой пять раз подряд.
    /// </summary>
    public ClipTransform WithRotation(int degrees) =>
        this with { Rotation = Normalize(degrees) };

    /// <summary>Довернуть от текущего положения: как раз то, что делает кнопка «повернуть».</summary>
    public ClipTransform RotatedBy(int degrees) => WithRotation(Rotation + degrees);

    private static int Normalize(int degrees)
    {
        var value = degrees % 360;
        if (value < 0)
        {
            value += 360;
        }

        // Между делениями не останавливаемся: 45 градусов здесь — ошибка вызова,
        // а не «почти прямой угол».
        return value - value % 90;
    }

    /// <summary>Смещения заданы в долях кадра: 0.5 — сдвиг на половину ширины.</summary>
    public ClipTransform WithZoom(double zoom) =>
        this with { Zoom = Math.Clamp(zoom, MinZoom, MaxZoom) };

    public ClipTransform WithOffset(double offsetX, double offsetY) =>
        this with { OffsetX = offsetX, OffsetY = offsetY };
}
