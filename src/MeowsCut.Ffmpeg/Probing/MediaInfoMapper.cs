using System.Globalization;
using MeowsCut.Core.Media;
using MeowsCut.Ffmpeg.Probing.Json;

namespace MeowsCut.Ffmpeg.Probing;

/// <summary>
/// Превращает ответ ffprobe в доменную модель. Главное правило: отсутствующее или
/// бессмысленное значение (bit_rate = "N/A", avg_frame_rate = "0/0") превращается в null,
/// а не в исключение — таких файлов в природе много.
/// </summary>
internal static class MediaInfoMapper
{
    /// <summary>Пиксельные форматы с альфа-каналом — нужны для стикеров.</summary>
    private static readonly string[] AlphaPixelFormats =
        ["yuva", "rgba", "bgra", "argb", "abgr", "ya8", "ya16", "gbrap"];

    public static MediaInfo Map(FfprobeResponse response, string filePath, long fileSizeBytes)
    {
        var format = response.Format;
        var streams = response.Streams ?? [];

        var container = new ContainerInfo(
            format?.FormatName ?? "unknown",
            format?.FormatLongName ?? string.Empty,
            ParseLong(format?.BitRate));

        var duration = ParseDuration(format?.Duration);

        var videoStreams = streams
            .Where(IsUsableVideoStream)
            .Select(MapVideo)
            .ToArray();

        var audioStreams = streams
            .Where(s => string.Equals(s.CodecType, "audio", StringComparison.OrdinalIgnoreCase))
            .Select(MapAudio)
            .ToArray();

        var subtitleStreams = streams
            .Where(s => string.Equals(s.CodecType, "subtitle", StringComparison.OrdinalIgnoreCase))
            .Select(MapSubtitle)
            .ToArray();

        // У некоторых контейнеров (MKV с потоковой записью) длительности в format нет,
        // но она есть у потока — берём максимальную известную.
        if (duration <= TimeSpan.Zero)
        {
            var streamDurations = videoStreams.Select(v => v.Duration)
                .Concat(audioStreams.Select(a => a.Duration))
                .Where(d => d > TimeSpan.Zero)
                .ToArray();

            if (streamDurations.Length > 0)
            {
                duration = streamDurations.Max();
            }
        }

        return new MediaInfo(
            filePath,
            fileSizeBytes,
            duration,
            container,
            videoStreams,
            audioStreams,
            subtitleStreams);
    }

    /// <summary>
    /// Обложка альбома в mp3 тоже приходит как видеопоток — такие отсеиваем,
    /// иначе аудиофайл выглядит как видео с одним кадром.
    /// </summary>
    private static bool IsUsableVideoStream(FfprobeStream stream)
    {
        if (!string.Equals(stream.CodecType, "video", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (stream.Disposition is not null &&
            stream.Disposition.TryGetValue("attached_pic", out var attached) &&
            attached == 1)
        {
            return false;
        }

        return true;
    }

    private static VideoStreamInfo MapVideo(FfprobeStream stream)
    {
        var pixelFormat = stream.PixelFormat ?? string.Empty;

        return new VideoStreamInfo(
            stream.Index,
            stream.CodecName ?? "unknown",
            stream.CodecLongName,
            stream.Profile,
            pixelFormat,
            new FrameSize(stream.Width ?? 0, stream.Height ?? 0),
            Rational.Parse(stream.RFrameRate),
            Rational.Parse(stream.AvgFrameRate),
            Rational.Parse(stream.SampleAspectRatio),
            ParseLong(stream.BitRate),
            ReadRotation(stream),
            HasAlpha(pixelFormat),
            ParseDuration(stream.Duration));
    }

    private static AudioStreamInfo MapAudio(FfprobeStream stream) =>
        new(
            stream.Index,
            stream.CodecName ?? "unknown",
            stream.CodecLongName,
            stream.Channels ?? 0,
            stream.ChannelLayout,
            (int)(ParseLong(stream.SampleRate) ?? 0),
            ParseLong(stream.BitRate),
            ParseDuration(stream.Duration),
            ReadTag(stream, "language"));

    private static SubtitleStreamInfo MapSubtitle(FfprobeStream stream) =>
        new(
            stream.Index,
            stream.CodecName ?? "unknown",
            ReadTag(stream, "language"),
            ReadTag(stream, "title"));

    /// <summary>
    /// Поворот приходит либо в side_data (современный вариант), либо тегом rotate (старый).
    /// Без него вертикальные видео с телефона показываются на боку.
    /// </summary>
    private static int ReadRotation(FfprobeStream stream)
    {
        var sideData = stream.SideDataList?
            .FirstOrDefault(x => x.Rotation.HasValue);

        if (sideData?.Rotation is { } rotation)
        {
            return NormalizeRotation((int)Math.Round(rotation));
        }

        var tag = ReadTag(stream, "rotate");
        if (tag is not null && int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tagValue))
        {
            return NormalizeRotation(tagValue);
        }

        return 0;
    }

    private static int NormalizeRotation(int degrees)
    {
        var normalized = degrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static bool HasAlpha(string pixelFormat) =>
        !string.IsNullOrEmpty(pixelFormat) &&
        AlphaPixelFormats.Any(prefix => pixelFormat.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static string? ReadTag(FfprobeStream stream, string key)
    {
        if (stream.Tags is null)
        {
            return null;
        }

        foreach (var pair in stream.Tags)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static long? ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static TimeSpan ParseDuration(string? value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
            seconds > 0 &&
            !double.IsInfinity(seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.Zero;
    }
}
