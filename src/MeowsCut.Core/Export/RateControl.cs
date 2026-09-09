namespace MeowsCut.Core.Export;

/// <summary>
/// Способ управления битрейтом. Разные варианты — разные ключи ffmpeg, поэтому
/// это не число с флажком, а явный набор случаев.
/// </summary>
public abstract record RateControl
{
    /// <summary>Подобрать автоматически по разрешению, частоте кадров и кодеку.</summary>
    public sealed record Auto : RateControl;

    /// <summary>Постоянный битрейт: предсказуемый размер, менее эффективное качество.</summary>
    public sealed record ConstantBitrate(int Kbps) : RateControl;

    /// <summary>Постоянное качество (CRF/CQ): размер заранее неизвестен.</summary>
    public sealed record ConstantQuality(int Crf) : RateControl;

    /// <summary>Уложиться в заданный размер — для стикеров и лимитов площадок.</summary>
    public sealed record TargetSize(long Bytes) : RateControl;

    public static readonly RateControl Default = new Auto();

    /// <summary>Готовые значения для списка в интерфейсе.</summary>
    public static readonly int[] BitratePresetsKbps = [500, 1000, 2000, 4000, 6000, 8000];
}

/// <summary>
/// Правила выбора битрейта и качества. Собраны в одном месте, чтобы значения
/// не расползались по интерфейсу и по коду сборки аргументов.
/// </summary>
public static class RateControlPolicy
{
    /// <summary>CRF поддерживают все наши кодеки, но диапазоны у них разные.</summary>
    public static (int Min, int Max, int Default) CrfRange(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => (0, 51, 23),
        VideoCodec.H265 => (0, 51, 28),
        VideoCodec.Vp9 => (0, 63, 31),
        VideoCodec.Av1 => (0, 63, 35),
        _ => (0, 51, 23)
    };

    public static bool SupportsConstantQuality(VideoCodec codec) => codec != VideoCodec.Copy;

    /// <summary>
    /// Во что разворачивается «Авто»: постоянное качество с типовым CRF.
    /// Это даёт хороший результат без вопросов к пользователю, а предсказуемый
    /// размер получают те, кто осознанно выбрал битрейт.
    /// </summary>
    public static RateControl Resolve(RateControl rateControl, VideoCodec codec)
    {
        if (rateControl is not RateControl.Auto)
        {
            return rateControl;
        }

        return new RateControl.ConstantQuality(CrfRange(codec).Default);
    }

    /// <summary>
    /// Оценка битрейта для показа в сводке и расчёта размера. Для режима постоянного
    /// качества это именно оценка — реальный битрейт зависит от содержимого кадра.
    /// </summary>
    public static long? EstimateVideoBitrateBps(
        RateControl rateControl,
        VideoCodec codec,
        Media.FrameSize size,
        double fps)
    {
        switch (rateControl)
        {
            case RateControl.ConstantBitrate constant:
                return constant.Kbps * 1000L;

            case RateControl.TargetSize:
                return null;

            case RateControl.Auto:
            case RateControl.ConstantQuality:
            {
                if (size.IsEmpty || fps <= 0)
                {
                    return null;
                }

                // Эмпирическая оценка: биты на пиксель в секунду для типового контента.
                var bitsPerPixel = codec switch
                {
                    VideoCodec.H264 => 0.10,
                    VideoCodec.H265 => 0.06,
                    VideoCodec.Vp9 => 0.06,
                    VideoCodec.Av1 => 0.05,
                    _ => 0.10
                };

                return (long)(size.Width * (long)size.Height * fps * bitsPerPixel);
            }

            default:
                return null;
        }
    }
}
