using System.Globalization;

namespace MeowsCut.Ffmpeg.Arguments;

/// <summary>
/// Сборка аргументов ffmpeg в виде списка строк.
/// </summary>
/// <remarks>
/// Порядок аргументов у ffmpeg значим: глобальные, затем входные, затем фильтры
/// и выходные. Билдер держит секции отдельно и склеивает их правильно независимо
/// от порядка вызовов — иначе легко получить «ключ применился не к тому потоку».
/// Строка команды не собирается никогда: наружу уходит список, который уходит
/// в ProcessStartInfo.ArgumentList.
/// </remarks>
public sealed class FfmpegArgumentBuilder
{
    private readonly List<string> _global = [];
    private readonly List<string> _inputs = [];
    private readonly List<string> _filters = [];
    private readonly List<string> _output = [];

    private string? _outputPath;

    public static FfmpegArgumentBuilder Create() => new();

    public FfmpegArgumentBuilder HideBanner()
    {
        _global.Add("-hide_banner");
        return this;
    }

    public FfmpegArgumentBuilder OverwriteOutput()
    {
        _global.Add("-y");
        return this;
    }

    public FfmpegArgumentBuilder NoStdin()
    {
        _global.Add("-nostdin");
        return this;
    }

    public FfmpegArgumentBuilder LogLevel(string level)
    {
        _global.Add("-loglevel");
        _global.Add(level);
        return this;
    }

    /// <summary>Прогресс отдельным потоком в stdout — его парсит FfmpegProgressParser.</summary>
    public FfmpegArgumentBuilder ProgressToStdout()
    {
        _global.Add("-progress");
        _global.Add("pipe:1");
        _global.Add("-nostats");
        return this;
    }

    /// <summary>Быстрый поиск перед -i: применяется к следующему входу.</summary>
    public FfmpegArgumentBuilder InputSeek(TimeSpan position)
    {
        _inputs.Add("-ss");
        _inputs.Add(FormatTime(position));
        return this;
    }

    public FfmpegArgumentBuilder InputTo(TimeSpan position)
    {
        _inputs.Add("-to");
        _inputs.Add(FormatTime(position));
        return this;
    }

    public FfmpegArgumentBuilder Input(string path)
    {
        _inputs.Add("-i");
        _inputs.Add(path);
        return this;
    }

    /// <summary>
    /// Опция, относящаяся к следующему входу.
    /// </summary>
    /// <remarks>
    /// Секция важна: те же ключи в выходной части означают совсем другое.
    /// Например, «-f concat» перед -i выбирает демультиплексор, а после —
    /// формат результата, и ffmpeg пытается прочитать список как видео.
    /// </remarks>
    public FfmpegArgumentBuilder InputOption(string key, string value)
    {
        _inputs.Add(key);
        _inputs.Add(value);
        return this;
    }

    public FfmpegArgumentBuilder InputFormat(string demuxer) => InputOption("-f", demuxer);

    public FfmpegArgumentBuilder FilterComplex(string graph)
    {
        if (string.IsNullOrWhiteSpace(graph))
        {
            return this;
        }

        _filters.Add("-filter_complex");
        _filters.Add(graph);
        return this;
    }

    public FfmpegArgumentBuilder Map(string specifier)
    {
        _output.Add("-map");
        _output.Add(specifier);
        return this;
    }

    public FfmpegArgumentBuilder VideoCodec(string codec) => Option("-c:v", codec);

    public FfmpegArgumentBuilder AudioCodec(string codec) => Option("-c:a", codec);

    public FfmpegArgumentBuilder CopyAllStreams() => Option("-c", "copy");

    public FfmpegArgumentBuilder NoAudio()
    {
        _output.Add("-an");
        return this;
    }

    public FfmpegArgumentBuilder NoSubtitles()
    {
        _output.Add("-sn");
        return this;
    }

    public FfmpegArgumentBuilder Crf(int value) => Option("-crf", value.ToString(CultureInfo.InvariantCulture));

    /// <summary>VP9 и AV1 используют -crf вместе с нулевым -b:v.</summary>
    public FfmpegArgumentBuilder ZeroVideoBitrate() => Option("-b:v", "0");

    public FfmpegArgumentBuilder VideoBitrateKbps(int kbps) =>
        Option("-b:v", kbps.ToString(CultureInfo.InvariantCulture) + "k");

    public FfmpegArgumentBuilder MaxRateKbps(int kbps) =>
        Option("-maxrate", kbps.ToString(CultureInfo.InvariantCulture) + "k");

    public FfmpegArgumentBuilder BufferSizeKbps(int kbps) =>
        Option("-bufsize", kbps.ToString(CultureInfo.InvariantCulture) + "k");

    public FfmpegArgumentBuilder AudioBitrateKbps(int kbps) =>
        Option("-b:a", kbps.ToString(CultureInfo.InvariantCulture) + "k");

    public FfmpegArgumentBuilder AudioSampleRate(int hertz) =>
        Option("-ar", hertz.ToString(CultureInfo.InvariantCulture));

    public FfmpegArgumentBuilder AudioChannels(int channels) =>
        Option("-ac", channels.ToString(CultureInfo.InvariantCulture));

    public FfmpegArgumentBuilder PixelFormat(string format) => Option("-pix_fmt", format);

    public FfmpegArgumentBuilder Preset(string preset) => Option("-preset", preset);

    public FfmpegArgumentBuilder MovFlags(string flags) => Option("-movflags", flags);

    public FfmpegArgumentBuilder Tag(string stream, string tag) => Option($"-tag:{stream}", tag);

    public FfmpegArgumentBuilder Format(string muxer) => Option("-f", muxer);

    public FfmpegArgumentBuilder Duration(TimeSpan duration) => Option("-t", FormatTime(duration));

    public FfmpegArgumentBuilder Pass(int pass, string logPrefix)
    {
        Option("-pass", pass.ToString(CultureInfo.InvariantCulture));
        Option("-passlogfile", logPrefix);
        return this;
    }

    public FfmpegArgumentBuilder Option(string key, string value)
    {
        _output.Add(key);
        _output.Add(value);
        return this;
    }

    public FfmpegArgumentBuilder Flag(string key)
    {
        _output.Add(key);
        return this;
    }

    public FfmpegArgumentBuilder Output(string path)
    {
        _outputPath = path;
        return this;
    }

    public IReadOnlyList<string> Build()
    {
        var arguments = new List<string>(_global.Count + _inputs.Count + _filters.Count + _output.Count + 1);
        arguments.AddRange(_global);
        arguments.AddRange(_inputs);
        arguments.AddRange(_filters);
        arguments.AddRange(_output);

        if (!string.IsNullOrEmpty(_outputPath))
        {
            arguments.Add(_outputPath);
        }

        return arguments;
    }

    /// <summary>ffmpeg ждёт время с точкой-разделителем независимо от локали системы.</summary>
    public static string FormatTime(TimeSpan value) =>
        value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
}
