namespace MeowsCut.Core.Export;

/// <summary>
/// Что с чем сочетается. Одна таблица на всё приложение: по ней интерфейс гасит
/// невозможные варианты, валидатор блокирует экспорт, а пресеты подбирают замену
/// вместо тихой поломки файла.
/// </summary>
public static class CompatibilityMatrix
{
    private static readonly Dictionary<ContainerFormat, VideoCodec[]> VideoByContainer = new()
    {
        [ContainerFormat.Mp4] = [VideoCodec.H264, VideoCodec.H265, VideoCodec.Av1],
        [ContainerFormat.WebM] = [VideoCodec.Vp9, VideoCodec.Av1],
        [ContainerFormat.Mov] = [VideoCodec.H264, VideoCodec.H265],
        [ContainerFormat.Mkv] = [VideoCodec.H264, VideoCodec.H265, VideoCodec.Vp9, VideoCodec.Av1],
        [ContainerFormat.Avi] = [VideoCodec.H264]
    };

    private static readonly Dictionary<ContainerFormat, AudioCodec[]> AudioByContainer = new()
    {
        [ContainerFormat.Mp4] = [AudioCodec.Aac, AudioCodec.Mp3],
        [ContainerFormat.WebM] = [AudioCodec.Opus, AudioCodec.Vorbis],
        [ContainerFormat.Mov] = [AudioCodec.Aac],
        [ContainerFormat.Mkv] = [AudioCodec.Aac, AudioCodec.Opus, AudioCodec.Mp3, AudioCodec.Vorbis, AudioCodec.Flac],
        [ContainerFormat.Avi] = [AudioCodec.Mp3],
        [ContainerFormat.Mp3] = [AudioCodec.Mp3],
        [ContainerFormat.M4a] = [AudioCodec.Aac],
        [ContainerFormat.Wav] = [AudioCodec.Pcm]
    };

    public static IReadOnlyList<VideoCodec> VideoCodecsFor(ContainerFormat container) =>
        VideoByContainer.TryGetValue(container, out var codecs) ? codecs : [];

    public static IReadOnlyList<AudioCodec> AudioCodecsFor(ContainerFormat container) =>
        AudioByContainer.TryGetValue(container, out var codecs) ? codecs : [];

    public static bool Supports(ContainerFormat container, VideoCodec codec) =>
        codec == VideoCodec.Copy || VideoCodecsFor(container).Contains(codec);

    public static bool Supports(ContainerFormat container, AudioCodec codec) =>
        codec == AudioCodec.Copy || AudioCodecsFor(container).Contains(codec);

    /// <summary>Кодек по умолчанию для контейнера — то, что открывается везде.</summary>
    public static VideoCodec DefaultVideoCodec(ContainerFormat container) => container switch
    {
        ContainerFormat.WebM => VideoCodec.Vp9,
        _ => VideoCodec.H264
    };

    public static AudioCodec DefaultAudioCodec(ContainerFormat container) => container switch
    {
        ContainerFormat.WebM => AudioCodec.Opus,
        ContainerFormat.Avi or ContainerFormat.Mp3 => AudioCodec.Mp3,
        ContainerFormat.Wav => AudioCodec.Pcm,
        _ => AudioCodec.Aac
    };

    /// <summary>
    /// Подбирает ближайший допустимый кодек при смене контейнера: H.264 в WebM
    /// не положить, но и молча ломать экспорт нельзя.
    /// </summary>
    public static VideoCodec Coerce(ContainerFormat container, VideoCodec codec) =>
        Supports(container, codec) ? codec : DefaultVideoCodec(container);

    public static AudioCodec Coerce(ContainerFormat container, AudioCodec codec) =>
        Supports(container, codec) ? codec : DefaultAudioCodec(container);
}
