using MeowsCut.Core.Media;

namespace MeowsCut.Core.Presets;

/// <summary>
/// Ориентация кадра монтажа. По ней выбирается пресет, который стоит в экспорте
/// до того, как пользователь выбрал свой.
/// </summary>
public enum FrameOrientation
{
    Landscape,
    Portrait,
    Square
}

/// <summary>Ориентация по размеру кадра.</summary>
public static class FrameOrientations
{
    public static FrameOrientation Of(FrameSize size) =>
        size.IsSquare ? FrameOrientation.Square
        : size.IsPortrait ? FrameOrientation.Portrait
        : FrameOrientation.Landscape;
}
