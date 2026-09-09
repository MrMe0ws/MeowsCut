using MeowsCut.App.Formatting;
using MeowsCut.App.Localization;
using MeowsCut.Core.Media;

namespace MeowsCut.App.ViewModels;

/// <summary>Одна строка «поле — значение» в панели информации.</summary>
public sealed record MediaField(string Label, string Value);

/// <summary>
/// Готовое к показу представление MediaInfo. Форматирование живёт здесь, а не в XAML:
/// так его можно проверить тестом и не плодить конвертеры.
/// </summary>
public sealed class MediaSummaryViewModel
{
    private MediaSummaryViewModel(
        MediaInfo source,
        IReadOnlyList<MediaField> fileFields,
        IReadOnlyList<MediaField> videoFields,
        IReadOnlyList<MediaField> audioFields,
        IReadOnlyList<string> warnings)
    {
        Source = source;
        FileFields = fileFields;
        VideoFields = videoFields;
        AudioFields = audioFields;
        Warnings = warnings;
    }

    public MediaInfo Source { get; }

    public string FileName => Source.FileName;

    public string FilePath => Source.FilePath;

    public IReadOnlyList<MediaField> FileFields { get; }

    public IReadOnlyList<MediaField> VideoFields { get; }

    public IReadOnlyList<MediaField> AudioFields { get; }

    public IReadOnlyList<string> Warnings { get; }

    public bool HasWarnings => Warnings.Count > 0;

    public bool HasAudio => AudioFields.Count > 0;

    /// <summary>Короткая строка для заголовка окна: 1920×1080 · 30 fps · H.264.</summary>
    public string HeaderLine { get; private set; } = string.Empty;

    public static MediaSummaryViewModel Create(MediaInfo info)
    {
        var video = info.PrimaryVideo;
        var audio = info.PrimaryAudio;

        var fileFields = new List<MediaField>
        {
            new(Strings.FieldDuration, DisplayFormat.Duration(info.Duration)),
            new(Strings.FieldSize, DisplayFormat.FileSize(info.FileSizeBytes)),
            new(Strings.FieldContainer, info.Container.FormatName),
            new(Strings.FieldOverallBitrate, DisplayFormat.Bitrate(info.OverallBitrateBps))
        };

        var videoFields = new List<MediaField>();
        var warnings = new List<string>();

        if (video is not null)
        {
            videoFields.Add(new MediaField(Strings.FieldResolution, video.DisplaySize.ToString()));
            videoFields.Add(new MediaField(Strings.FieldFrameRate, DisplayFormat.FrameRate(video.FrameRate)));
            videoFields.Add(new MediaField(Strings.FieldCodec, DisplayFormat.Codec(video.CodecName, video.Profile)));
            videoFields.Add(new MediaField(Strings.FieldBitrate, DisplayFormat.Bitrate(video.BitrateBps)));
            videoFields.Add(new MediaField(Strings.FieldPixelFormat, video.PixelFormat));

            if (video.IsVariableFrameRate)
            {
                warnings.Add(Strings.WarningVariableFrameRate);
            }

            if (video.RotationDegrees != 0)
            {
                warnings.Add(string.Format(Strings.WarningRotated, video.RotationDegrees));
            }
        }

        var audioFields = new List<MediaField>();
        if (audio is not null)
        {
            audioFields.Add(new MediaField(Strings.FieldCodec, DisplayFormat.Codec(audio.CodecName, null)));
            audioFields.Add(new MediaField(Strings.FieldBitrate, DisplayFormat.Bitrate(audio.BitrateBps)));
            audioFields.Add(new MediaField(Strings.FieldChannels, DisplayFormat.Channels(audio.Channels, audio.ChannelLayout)));
            audioFields.Add(new MediaField(Strings.FieldSampleRate, DisplayFormat.SampleRate(audio.SampleRateHz)));
        }

        var summary = new MediaSummaryViewModel(info, fileFields, videoFields, audioFields, warnings);

        summary.HeaderLine = video is null
            ? info.FileName
            : $"{info.FileName}  ·  {video.DisplaySize}  ·  {DisplayFormat.FrameRate(video.FrameRate)}  ·  {video.CodecName}";

        return summary;
    }
}
