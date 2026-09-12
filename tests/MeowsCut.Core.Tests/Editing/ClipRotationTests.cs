using FluentAssertions;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Core.Tests.Editing;

/// <summary>
/// Поворот кадра на прямой угол — то, чем разворачивают снятое боком.
/// </summary>
public class ClipRotationTests
{
    [Theory]
    [InlineData(90, 90)]
    [InlineData(180, 180)]
    [InlineData(270, 270)]
    [InlineData(360, 0)]
    [InlineData(450, 90)]
    [InlineData(-90, 270)]
    [InlineData(-180, 180)]
    public void Angle_is_brought_to_four_positions(int asked, int expected) =>
        ClipTransform.Identity.WithRotation(asked).Rotation.Should().Be(expected);

    [Fact]
    public void Quarter_turns_add_up_and_wrap_around()
    {
        var transform = ClipTransform.Identity
            .RotatedBy(90)
            .RotatedBy(90)
            .RotatedBy(90)
            .RotatedBy(90);

        transform.Rotation.Should().Be(0);
        transform.IsRotated.Should().BeFalse();
    }

    [Fact]
    public void Quarter_turn_swaps_width_and_height()
    {
        ClipTransform.Identity.WithRotation(90).SwapsDimensions.Should().BeTrue();
        ClipTransform.Identity.WithRotation(270).SwapsDimensions.Should().BeTrue();
        ClipTransform.Identity.WithRotation(180).SwapsDimensions.Should().BeFalse();
    }

    [Fact]
    public void Rotated_frame_is_no_longer_untouched()
    {
        // От этого зависит выбор стратегии экспорта: у нетронутого кадра
        // планировщику разрешено копировать поток вместо перекодирования.
        ClipTransform.Identity.IsIdentity.Should().BeTrue();
        ClipTransform.Identity.WithRotation(90).IsIdentity.Should().BeFalse();
    }

    [Fact]
    public void Rotation_survives_zoom_and_offset()
    {
        var transform = ClipTransform.Identity
            .WithRotation(90)
            .WithZoom(1.5)
            .WithOffset(0.1, -0.2);

        transform.Rotation.Should().Be(90);
    }

    [Fact]
    public void Sequence_knows_that_a_clip_is_rotated()
    {
        var source = MediaSource.FromMedia(Fake.Info(30));
        var sequence = Sequence.FromSource(source);

        sequence.HasRotation.Should().BeFalse();

        var rotated = sequence.WithTrack(
            sequence.Video.Replace(
                sequence.Video.Clips[0].WithTransform(ClipTransform.Identity.WithRotation(90))));

        rotated.HasRotation.Should().BeTrue();
        rotated.HasPictureEdits.Should().BeTrue();
    }
}
