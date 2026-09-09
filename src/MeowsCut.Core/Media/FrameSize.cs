namespace MeowsCut.Core.Media;

/// <summary>
/// Размер кадра в пикселях.
/// </summary>
public readonly record struct FrameSize(int Width, int Height)
{
    public static readonly FrameSize Empty = new(0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public double AspectRatio => Height == 0 ? 0d : (double)Width / Height;

    public bool IsSquare => Width == Height;

    public bool IsPortrait => Height > Width;

    /// <summary>
    /// Вписывает кадр в заданные габариты с сохранением пропорций. Не увеличивает.
    /// </summary>
    public FrameSize ScaledToFit(int maxWidth, int maxHeight)
    {
        if (IsEmpty || maxWidth <= 0 || maxHeight <= 0)
        {
            return Empty;
        }

        var scale = Math.Min((double)maxWidth / Width, (double)maxHeight / Height);
        if (scale >= 1d)
        {
            return this;
        }

        return new FrameSize((int)Math.Round(Width * scale), (int)Math.Round(Height * scale));
    }

    /// <summary>
    /// Масштабирует по целевой высоте с сохранением пропорций.
    /// </summary>
    public FrameSize ScaledToHeight(int targetHeight)
    {
        if (IsEmpty || targetHeight <= 0)
        {
            return Empty;
        }

        var width = (int)Math.Round(targetHeight * AspectRatio);
        return new FrameSize(width, targetHeight).RoundedToEven();
    }

    /// <summary>
    /// Округляет обе стороны до чётных значений: требование yuv420p для H.264/H.265.
    /// </summary>
    public FrameSize RoundedToEven()
    {
        if (IsEmpty)
        {
            return Empty;
        }

        return new FrameSize(Width + (Width & 1), Height + (Height & 1));
    }

    public override string ToString() => IsEmpty ? "—" : $"{Width}×{Height}";
}
