using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Media;
using MeowsCut.Ffmpeg.Audio;
using MeowsCut.Ffmpeg.Execution;
using MeowsCut.Ffmpeg.Toolset;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeowsCut.Ffmpeg.Tests.Integration;

/// <summary>
/// Шумоподавление дубля на настоящем ffmpeg: шум тише, голос цел, время то же.
/// </summary>
/// <remarks>
/// Запись собирается из розового шума, «голоса» (тон с 1 до 1,5 с) и щелчка ровно
/// на второй секунде. По щелчку видно, не сдвинул ли фильтр звук: afftdn сам
/// задерживает его на ~25 мс, и без поправки озвучка отставала бы от картинки.
/// </remarks>
[Trait("Category", "RequiresFfmpeg")]
public sealed partial class NoiseReductionIntegrationTests : IDisposable
{
    private readonly string _workDirectory = Path.Combine(
        Path.GetTempPath(),
        "meowscut-tests",
        Guid.NewGuid().ToString("N"));

    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    public NoiseReductionIntegrationTests() => Directory.CreateDirectory(_workDirectory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Временные файлы — не повод падать в тесте.
        }
    }

    [FfmpegFact]
    public async Task Noise_gets_quieter_and_the_voice_stays()
    {
        var (input, output) = await ReduceAsync();

        var noiseBefore = await RmsAsync(input, 1.6, 1.95);
        var noiseAfter = await RmsAsync(output, 1.6, 1.95);
        var voiceBefore = await RmsAsync(input, 1.1, 1.4);
        var voiceAfter = await RmsAsync(output, 1.1, 1.4);

        (noiseBefore - noiseAfter).Should().BeGreaterThan(10, "шум микрофона должен стать заметно тише");
        Math.Abs(voiceBefore - voiceAfter).Should().BeLessThan(2, "голос трогать нельзя");
    }

    [FfmpegFact]
    public async Task Voice_does_not_move_in_time()
    {
        var (input, output) = await ReduceAsync();

        var (peakBefore, lengthBefore) = Peak(input);
        var (peakAfter, lengthAfter) = Peak(output);

        (peakAfter - peakBefore).Should().BeInRange(-0.006, 0.006, "щелчок обязан остаться на своём месте");
        lengthAfter.Should().BeApproximately(lengthBefore, 0.002, "длина дубля не меняется");
    }

    private async Task<(string Input, string Output)> ReduceAsync()
    {
        var input = Path.Combine(_workDirectory, "дубль.wav");
        var output = Path.Combine(_workDirectory, "дубль (без шума).wav");

        await RunFfmpegAsync(
        [
            "-f", "lavfi", "-i", "aevalsrc='if(between(t,1,1.5),0.3*sin(2*PI*300*t),0)+if(between(t,2,2.002),0.9,0)':s=48000:d=4",
            "-f", "lavfi", "-i", "anoisesrc=color=pink:amplitude=0.02:duration=4:sample_rate=48000",
            "-filter_complex", "[0][1]amix=inputs=2:normalize=0,aformat=sample_fmts=s16:sample_rates=48000:channel_layouts=mono",
            input
        ]);

        var reducer = new FfmpegNoiseReducer(CreateToolsetProvider(), _runner, NullLogger<FfmpegNoiseReducer>.Instance);
        var reduced = await reducer.ReduceAsync(input, output, 20, CancellationToken.None);

        reduced.Should().BeTrue();
        File.Exists(output).Should().BeTrue();
        File.Exists(output + ".part").Should().BeFalse("промежуточный файл переименован");

        return (input, output);
    }

    private async Task<double> RmsAsync(string path, double from, double to)
    {
        var lines = new List<string>();
        string[] arguments =
        [
            "-hide_banner", "-nostdin", "-i", path,
            "-af", string.Create(CultureInfo.InvariantCulture, $"atrim={from}:{to},astats"), "-f", "null", "-"
        ];

        await _runner.RunAsync(new ProcessRequest(FfmpegTestEnvironment.FfmpegPath!, arguments), null, lines.Add, CancellationToken.None);

        var match = lines.Select(line => RmsLine().Match(line)).Last(m => m.Success);
        return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>Время самого громкого отсчёта и длина записи — по 16-битному моно WAV.</summary>
    private static (double PeakSeconds, double LengthSeconds) Peak(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var data = FindChunk(bytes, "data");
        var samples = (bytes.Length - data) / 2;

        var best = 0;
        var bestValue = 0;
        for (var i = 0; i < samples; i++)
        {
            var value = Math.Abs((int)BitConverter.ToInt16(bytes, data + i * 2));
            if (value > bestValue)
            {
                bestValue = value;
                best = i;
            }
        }

        return (best / 48000d, samples / 48000d);
    }

    private static int FindChunk(byte[] bytes, string id)
    {
        for (var i = 12; i < bytes.Length - 8;)
        {
            var name = System.Text.Encoding.ASCII.GetString(bytes, i, 4);
            var size = BitConverter.ToInt32(bytes, i + 4);
            if (name == id)
            {
                return i + 8;
            }

            i += 8 + size + (size % 2);
        }

        throw new InvalidDataException("В WAV нет блока " + id);
    }

    private async Task RunFfmpegAsync(string[] arguments)
    {
        string[] full = ["-y", "-hide_banner", "-loglevel", "error", .. arguments];

        var result = await _runner.RunAsync(
            new ProcessRequest(FfmpegTestEnvironment.FfmpegPath!, full),
            null,
            null,
            CancellationToken.None);

        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.StdErrTail));
    }

    private static IMediaToolsetProvider CreateToolsetProvider()
    {
        var provider = new MediaToolsetProvider();
        provider.Set(new MediaToolset(
            FfmpegTestEnvironment.FfmpegPath!,
            FfmpegTestEnvironment.FfprobePath!,
            "test",
            ToolsetSource.LocalAppData,
            MediaCapabilities.Empty));

        return provider;
    }

    [GeneratedRegex(@"RMS level dB: (-?[\d.]+)")]
    private static partial Regex RmsLine();
}
