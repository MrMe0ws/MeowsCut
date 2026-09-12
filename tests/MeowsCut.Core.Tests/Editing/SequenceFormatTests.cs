using FluentAssertions;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.History;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Export;
using MeowsCut.Core.Media;

namespace MeowsCut.Core.Tests.Editing;

/// <summary>
/// Кадр ролика: вертикальный формат и то, как в него ложатся горизонтальные куски.
/// </summary>
public class SequenceFormatTests
{
    private static SequenceFormat FullHd => new(new FrameSize(1920, 1080), new Rational(30, 1));

    [Fact]
    public void Vertical_format_turns_the_frame_on_its_side()
    {
        var vertical = FullHd.WithAspect(9d / 16d);

        vertical.Size.Should().Be(new FrameSize(1080, 1920));
        vertical.IsCustom.Should().BeTrue();
    }

    [Fact]
    public void Long_side_survives_the_turn()
    {
        // Ролик из 4K обязан остаться 4K по длинной стороне, а не упасть до 1080.
        var vertical = new SequenceFormat(new FrameSize(3840, 2160), new Rational(30, 1))
            .WithAspect(9d / 16d);

        vertical.Size.Should().Be(new FrameSize(2160, 3840));
    }

    [Fact]
    public void Square_format_keeps_both_sides_equal()
    {
        FullHd.WithAspect(1d).Size.Should().Be(new FrameSize(1920, 1920));
    }

    [Fact]
    public void Frame_sides_stay_even()
    {
        // Нечётная сторона не кодируется в yuv420p: ffmpeg просто отказывается.
        var format = new SequenceFormat(new FrameSize(1001, 999), new Rational(30, 1)).WithAspect(9d / 16d);

        (format.Size.Width % 2).Should().Be(0);
        (format.Size.Height % 2).Should().Be(0);
    }

    [Fact]
    public void Format_from_a_file_is_not_custom()
    {
        var source = MediaSource.FromMedia(Fake.Info(30));

        Sequence.FromSource(source).Format.IsCustom.Should().BeFalse();
    }

    [Fact]
    public void Custom_format_makes_every_clip_go_through_the_common_frame()
    {
        var source = MediaSource.FromMedia(Fake.Info(30));
        var sequence = Sequence.FromSource(source);

        sequence.NeedsUniformFrame.Should().BeFalse("куски одного файла и так одного размера");

        var vertical = sequence with { Format = sequence.Format.WithAspect(9d / 16d) };

        vertical.NeedsUniformFrame.Should().BeTrue();
    }

    [Fact]
    public void Command_changes_the_format_and_undo_returns_it()
    {
        var source = MediaSource.FromMedia(Fake.Info(30));
        var history = new EditHistory(Sequence.FromSource(source));

        var vertical = history.Current.Format.WithAspect(9d / 16d) with { Fit = FitMode.Cover };
        history.Execute(new SetSequenceFormatCommand(vertical));

        history.Current.Format.Size.Should().Be(new FrameSize(1080, 1920));
        history.Current.Format.Fit.Should().Be(FitMode.Cover);

        history.Undo();

        history.Current.Format.Size.Should().Be(new FrameSize(1920, 1080));
        history.Current.Format.IsCustom.Should().BeFalse();
    }

    [Fact]
    public void Changing_the_format_is_not_merged_with_the_previous_edit()
    {
        // Это осознанное решение, а не тягание ползунка: каждая смена формата
        // обязана отменяться сама по себе.
        var command = new SetSequenceFormatCommand(FullHd.WithAspect(1d));

        command.TryMergeWith(new SetSequenceFormatCommand(FullHd), out _).Should().BeFalse();
    }
}
