using FluentAssertions;
using MeowsCut.Ffmpeg.Execution;
using MeowsCut.Ffmpeg.Toolset;

namespace MeowsCut.Ffmpeg.Tests.Toolset;

public class EncoderProbeTests
{
    private const string EncodersOutput =
        """
        Encoders:
         V..... = Video
         A..... = Audio
         ------
         V....D libx264              libx264 H.264 / AVC / MPEG-4 AVC
         V....D libx265              libx265 H.265 / HEVC
         V....D libvpx-vp9           libvpx VP9
         V....D libsvtav1            SVT-AV1 encoder
         V..... h264_nvenc           NVIDIA NVENC H.264 encoder
         A....D aac                  AAC (Advanced Audio Coding)
         A....D libopus              libopus Opus
         A....D libmp3lame           libmp3lame MP3
        """;

    private const string HwAccelsOutput =
        """
        Hardware acceleration methods:
        cuda
        d3d11va
        qsv
        """;

    [Fact]
    public async Task Splits_video_and_audio_encoders()
    {
        var runner = new StubProcessRunner
        {
            Responses =
            {
                ["-encoders"] = EncodersOutput,
                ["-hwaccels"] = HwAccelsOutput
            }
        };

        var capabilities = await new EncoderProbe(runner).ProbeAsync(@"C:\ffmpeg\ffmpeg.exe", CancellationToken.None);

        capabilities.VideoEncoders.Should().Contain(["libx264", "libx265", "libvpx-vp9", "libsvtav1", "h264_nvenc"]);
        capabilities.AudioEncoders.Should().Contain(["aac", "libopus", "libmp3lame"]);
        capabilities.VideoEncoders.Should().NotContain("aac");
        capabilities.AudioEncoders.Should().NotContain("libx264");
    }

    [Fact]
    public async Task Reads_hardware_accelerators_without_header_line()
    {
        var runner = new StubProcessRunner
        {
            Responses =
            {
                ["-encoders"] = EncodersOutput,
                ["-hwaccels"] = HwAccelsOutput
            }
        };

        var capabilities = await new EncoderProbe(runner).ProbeAsync(@"C:\ffmpeg\ffmpeg.exe", CancellationToken.None);

        capabilities.HardwareAccelerators.Should().BeEquivalentTo(["cuda", "d3d11va", "qsv"]);
    }

    [Fact]
    public async Task Returns_empty_capabilities_when_ffmpeg_fails()
    {
        var runner = new StubProcessRunner { ExitCode = 1 };

        var capabilities = await new EncoderProbe(runner).ProbeAsync(@"C:\ffmpeg\ffmpeg.exe", CancellationToken.None);

        capabilities.VideoEncoders.Should().BeEmpty();
    }

    /// <summary>
    /// Подставной запуск процессов: тесты не должны зависеть от установленного ffmpeg.
    /// </summary>
    private sealed class StubProcessRunner : IProcessRunner
    {
        public Dictionary<string, string> Responses { get; } = [];

        public int ExitCode { get; init; }

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            Action<string>? onStandardOutputLine,
            Action<string>? onStandardErrorLine,
            CancellationToken cancellationToken)
        {
            foreach (var line in FindResponse(request).Split('\n'))
            {
                onStandardOutputLine?.Invoke(line.TrimEnd('\r'));
            }

            return Task.FromResult(new ProcessResult(ExitCode, [], TimeSpan.Zero));
        }

        public Task<(ProcessResult Result, string StandardOutput)> RunCapturingOutputAsync(
            ProcessRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult((new ProcessResult(ExitCode, [], TimeSpan.Zero), FindResponse(request)));

        private string FindResponse(ProcessRequest request)
        {
            foreach (var argument in request.Arguments)
            {
                if (Responses.TryGetValue(argument, out var response))
                {
                    return response;
                }
            }

            return string.Empty;
        }
    }
}
