using System.Buffers.Binary;
using NAudio.Wave;

namespace MeowsCut.Recording;

/// <summary>Формат, в котором микрофон отдаёт звук.</summary>
internal readonly record struct CaptureFormat(int Channels, int BitsPerSample, bool IsFloat)
{
    public int BytesPerSample => BitsPerSample / 8;

    public int BlockAlign => Channels * BytesPerSample;

    public bool IsSupported =>
        Channels > 0 &&
        (IsFloat ? BitsPerSample is 32 or 64 : BitsPerSample is 16 or 24 or 32);

    /// <summary>KSDATAFORMAT_SUBTYPE_IEEE_FLOAT — так WASAPI помечает отсчёты с плавающей точкой.</summary>
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    public static CaptureFormat From(WaveFormat format)
    {
        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                      (format is WaveFormatExtensible extensible && extensible.SubFormat == IeeeFloatSubFormat);

        return new CaptureFormat(format.Channels, format.BitsPerSample, isFloat);
    }
}

/// <summary>
/// Перевод звука микрофона в 16-битное моно.
/// </summary>
/// <remarks>
/// WASAPI в общем режиме отдаёт звук в формате системного микшера — обычно это
/// 32-битные числа с плавающей точкой и столько каналов, сколько у микрофонного
/// массива. Для голоса это вчетверо больше места без пользы, а моно ещё и избавляет
/// от голоса «в одном ухе», если массив пишет каналы по-разному.
/// </remarks>
internal static class SampleDownmix
{
    /// <summary>
    /// Сводит каналы и пишет 16-битные отсчёты в <paramref name="output"/>.
    /// </summary>
    /// <param name="peak">Наибольший модуль отсчёта до сведения, от 0 до 1 — для индикатора уровня.</param>
    /// <returns>Сколько байт записано.</returns>
    public static int ToMono16(CaptureFormat format, ReadOnlySpan<byte> input, Span<byte> output, out float peak)
    {
        var frames = input.Length / format.BlockAlign;
        var bytesPerSample = format.BytesPerSample;
        peak = 0f;

        for (var frame = 0; frame < frames; frame++)
        {
            var block = input.Slice(frame * format.BlockAlign, format.BlockAlign);
            var sum = 0f;

            for (var channel = 0; channel < format.Channels; channel++)
            {
                var sample = Read(format, block.Slice(channel * bytesPerSample, bytesPerSample));
                var magnitude = MathF.Abs(sample);

                if (magnitude > peak)
                {
                    peak = magnitude;
                }

                sum += sample;
            }

            var mono = Math.Clamp(sum / format.Channels, -1f, 1f);
            BinaryPrimitives.WriteInt16LittleEndian(output.Slice(frame * 2, 2), (short)MathF.Round(mono * short.MaxValue));
        }

        peak = Math.Min(peak, 1f);
        return frames * 2;
    }

    private static float Read(CaptureFormat format, ReadOnlySpan<byte> sample) => (format.IsFloat, format.BitsPerSample) switch
    {
        (true, 32) => BinaryPrimitives.ReadSingleLittleEndian(sample),
        (true, 64) => (float)BinaryPrimitives.ReadDoubleLittleEndian(sample),
        (false, 16) => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768f,
        (false, 24) => (sample[0] | (sample[1] << 8) | ((sbyte)sample[2] << 16)) / 8388608f,
        (false, 32) => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648f,
        _ => 0f
    };
}
