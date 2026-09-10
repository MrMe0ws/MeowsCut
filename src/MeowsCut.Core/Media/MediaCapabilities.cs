namespace MeowsCut.Core.Media;

/// <summary>
/// Что умеет найденная сборка ffmpeg. Заполняется на старте по выводу -encoders/-hwaccels
/// и определяет, какие варианты вообще показывать в интерфейсе: лучше не предлагать AV1,
/// чем предложить и упасть в конце длинного экспорта.
/// </summary>
public sealed record MediaCapabilities(
    IReadOnlySet<string> VideoEncoders,
    IReadOnlySet<string> AudioEncoders,
    IReadOnlySet<string> HardwareAccelerators)
{
    public static readonly MediaCapabilities Empty = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Доступные фильтры. Нужны не для показа в интерфейсе, а для выбора реализации:
    /// сдвиг тональности с rubberband звучит чище, но этого фильтра в сборке может не быть.
    /// </summary>
    public IReadOnlySet<string> Filters { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool HasVideoEncoder(string name) => VideoEncoders.Contains(name);

    public bool HasAudioEncoder(string name) => AudioEncoders.Contains(name);

    public bool HasHardwareAcceleration(string name) => HardwareAccelerators.Contains(name);

    /// <summary>
    /// Аппаратные энкодеры, которые действительно запустились на этой машине.
    /// </summary>
    /// <remarks>
    /// Список <see cref="VideoEncoders"/> говорит лишь о том, с чем ffmpeg собран.
    /// h264_nvenc там есть всегда, а без драйвера NVIDIA он падает на «Cannot load
    /// nvcuda.dll» — и падает в конце длинного экспорта. Поэтому железо проверяется
    /// пробным кодированием одного кадра, а не наличием имени в списке.
    /// </remarks>
    public IReadOnlySet<string> WorkingHardwareEncoders { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool HasFilter(string name) => Filters.Contains(name);

    public bool HasWorkingHardwareEncoder(string name) => WorkingHardwareEncoders.Contains(name);
}
