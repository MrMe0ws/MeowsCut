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

    public bool HasVideoEncoder(string name) => VideoEncoders.Contains(name);

    public bool HasAudioEncoder(string name) => AudioEncoders.Contains(name);

    public bool HasHardwareAcceleration(string name) => HardwareAccelerators.Contains(name);
}
