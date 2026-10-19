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
    public const double MaxZoom = 50d;

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

    /// <summary>
    /// Как этот кусок ложится в кадр ролика, если не так, как весь проект.
    /// null — как в проекте.
    /// </summary>
    /// <remarks>
    /// Формат и «заполнить кадр» выбираются на весь ролик, но кадры в нём бывают
    /// разные: вертикальное видео с телефона в широком ролике хочется растянуть
    /// на всю ширину, а соседнюю фотографию — оставить целиком, с полями.
    /// </remarks>
    public Export.FitMode? Fit { get; init; }

    /// <summary>
    /// За сколько секунд от начала куска кадр доходит до заданных масштаба и сдвига.
    /// 0 — сразу, без наезда.
    /// </summary>
    public double MotionSeconds { get; init; }

    /// <summary>Отъезд: от заданного кадра к 100%, а не наоборот.</summary>
    public bool MotionOut { get; init; }

    /// <summary>Кадр не стоит на месте: есть куда ехать и есть за сколько.</summary>
    public bool HasMotion => MotionSeconds > 0 && !IsUnscaled;

    /// <summary>Масштаб 100% и без сдвига — поворот и подгонка не в счёт.</summary>
    public bool IsUnscaled =>
        Math.Abs(Zoom - 1d) < 0.0001 &&
        Math.Abs(OffsetX) < 0.0001 &&
        Math.Abs(OffsetY) < 0.0001;

    public bool IsIdentity => Fit is null && Rotation == 0 && IsUnscaled;

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

    /// <summary>
    /// Кадрирование в момент <paramref name="offset"/> от начала куска: с учётом
    /// наезда и со сдвигом, ужатым до картинки.
    /// </summary>
    public ClipTransform At(TimeSpan offset, TimeSpan clipDuration)
    {
        var still = this with { MotionSeconds = 0d, MotionOut = false };
        if (!HasMotion)
        {
            return still.Clamped();
        }

        var eased = ClipMotion.Ease(ClipMotion.Progress(offset, MotionSeconds, clipDuration));
        var share = MotionOut ? 1d - eased : eased;

        return (still with
        {
            Zoom = Math.Pow(Zoom, share),
            OffsetX = OffsetX * share,
            OffsetY = OffsetY * share
        }).Clamped();
    }

    /// <summary>Сдвиг, ужатый так, чтобы кадр не выходил за картинку: так его режет и экспорт.</summary>
    public ClipTransform Clamped()
    {
        var limit = ClipMotion.OffsetLimit(Zoom);
        return this with
        {
            OffsetX = Math.Clamp(OffsetX, -limit, limit),
            OffsetY = Math.Clamp(OffsetY, -limit, limit)
        };
    }

    /// <summary>Смещения заданы в долях кадра: 0.5 — сдвиг на половину ширины.</summary>
    public ClipTransform WithZoom(double zoom) =>
        this with { Zoom = Math.Clamp(zoom, MinZoom, MaxZoom) };

    public ClipTransform WithOffset(double offsetX, double offsetY) =>
        this with { OffsetX = offsetX, OffsetY = offsetY };
}
