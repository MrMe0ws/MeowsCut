using System.Buffers.Binary;
using FluentAssertions;
using MeowsCut.Recording;

namespace MeowsCut.App.Tests.Recording;

/// <summary>Звук микрофона в 16-битное моно: форматы, которые реально отдаёт WASAPI.</summary>
public class SampleDownmixTests
{
    private static short[] Run(CaptureFormat format, byte[] input, out float peak)
    {
        var output = new byte[input.Length / format.BlockAlign * 2];
        var written = SampleDownmix.ToMono16(format, input, output, out peak);

        var samples = new short[written / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(i * 2));
        }

        return samples;
    }

    private static byte[] Floats(params float[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), values[i]);
        }

        return bytes;
    }

    [Fact]
    public void Stereo_float_is_averaged_into_mono()
    {
        var format = new CaptureFormat(Channels: 2, BitsPerSample: 32, IsFloat: true);

        var samples = Run(format, Floats(0.5f, 0.5f, 1f, 0f, -1f, -1f), out var peak);

        samples.Should().Equal(16384, 16384, -32767);
        peak.Should().Be(1f);
    }

    [Fact]
    public void Microphone_array_with_four_channels_is_supported()
    {
        var format = new CaptureFormat(4, 32, true);

        var samples = Run(format, Floats(0.2f, 0.2f, 0.2f, 0.2f), out var peak);

        samples.Should().ContainSingle().Which.Should().BeCloseTo(6553, 1);
        peak.Should().BeApproximately(0.2f, 0.0001f);
    }

    [Fact]
    public void Pcm16_passes_through_unchanged_in_mono()
    {
        var format = new CaptureFormat(1, 16, false);
        var input = new byte[4];
        BinaryPrimitives.WriteInt16LittleEndian(input, 1234);
        BinaryPrimitives.WriteInt16LittleEndian(input.AsSpan(2), -20000);

        var samples = Run(format, input, out _);

        samples[0].Should().BeCloseTo(1234, 1);
        samples[1].Should().BeCloseTo(-20000, 1);
    }

    [Fact]
    public void Negative_pcm24_keeps_its_sign()
    {
        var format = new CaptureFormat(1, 24, false);
        // −0,5 в 24 битах: 0xC00000
        var input = new byte[] { 0x00, 0x00, 0xC0 };

        var samples = Run(format, input, out var peak);

        samples[0].Should().BeCloseTo(-16384, 1);
        peak.Should().BeApproximately(0.5f, 0.0001f);
    }

    [Fact]
    public void Silence_has_zero_peak()
    {
        var format = new CaptureFormat(2, 32, true);

        Run(format, Floats(0f, 0f, 0f, 0f), out var peak);

        peak.Should().Be(0f, "по нулевому пику узнаём микрофон, который Windows не даёт программе");
    }

    [Theory]
    [InlineData(1, 8, false)]
    [InlineData(2, 16, true)]
    [InlineData(0, 16, false)]
    public void Unusual_formats_are_reported_as_unsupported(int channels, int bits, bool isFloat)
    {
        new CaptureFormat(channels, bits, isFloat).IsSupported.Should().BeFalse();
    }
}
