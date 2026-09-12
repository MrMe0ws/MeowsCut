using FluentAssertions;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.History;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Media;

namespace MeowsCut.Core.Tests.Editing;

/// <summary>
/// Появление из чёрного и уход в чёрное.
/// </summary>
/// <remarks>
/// Заказанная длина хранится как есть, а наружу отдаётся подрезанная: клип
/// укорачивают уже после того, как выставили затухание, и без подрезки
/// fade=out начинался бы за концом куска — то есть не показывался вовсе.
/// </remarks>
public class ClipFadeTests
{
    private static Clip Clip10s()
    {
        var source = MediaSource.FromMedia(Fake.Info(60));
        return Clip.FromSource(source, new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Clip_starts_without_fades()
    {
        var clip = Clip10s();

        clip.HasFades.Should().BeFalse();
        clip.EffectiveFadeIn.Should().Be(TimeSpan.Zero);
        clip.EffectiveFadeOut.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Fades_are_kept_as_asked_while_they_fit()
    {
        var clip = Clip10s().WithFades(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        clip.EffectiveFadeIn.Should().Be(TimeSpan.FromSeconds(1));
        clip.EffectiveFadeOut.Should().Be(TimeSpan.FromSeconds(2));
        clip.HasFades.Should().BeTrue();
    }

    [Fact]
    public void Fade_longer_than_the_clip_shrinks_with_it()
    {
        // Выставили три секунды затухания, потом обрезали клип до двух.
        var clip = Clip10s().WithFades(fadeOut: TimeSpan.FromSeconds(3));
        var short_ = clip.TrimEnd(TimeSpan.FromSeconds(-8));

        short_.TimelineDuration.Should().Be(TimeSpan.FromSeconds(2));
        short_.EffectiveFadeOut.Should().Be(TimeSpan.FromSeconds(2));

        // Само заданное значение не потеряно: вернув край, пользователь
        // получает обратно те три секунды, которые просил.
        short_.FadeOut.Should().Be(TimeSpan.FromSeconds(3));
        short_.TrimEnd(TimeSpan.FromSeconds(8)).EffectiveFadeOut.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Two_fades_longer_than_the_clip_meet_in_the_middle()
    {
        var clip = Clip10s().WithFades(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(8));

        clip.EffectiveFadeIn.Should().Be(TimeSpan.FromSeconds(5));
        clip.EffectiveFadeOut.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Fade_is_capped_and_never_negative()
    {
        var clip = Clip10s().WithFades(TimeSpan.FromSeconds(-4), TimeSpan.FromHours(1));

        clip.FadeIn.Should().Be(TimeSpan.Zero);
        clip.FadeOut.Should().Be(Clip.MaxFade);
    }

    [Fact]
    public void Command_changes_one_edge_and_leaves_the_other()
    {
        var source = MediaSource.FromMedia(Fake.Info(60));
        var sequence = Sequence.FromSource(source);
        var history = new EditHistory(sequence);
        var id = sequence.Video.Clips[0].Id;

        history.Execute(new SetClipFadesCommand(id, fadeIn: TimeSpan.FromSeconds(1)));
        history.Execute(new SetClipFadesCommand(id, fadeOut: TimeSpan.FromSeconds(2)));

        var clip = history.Current.Video.Clips[0];
        clip.FadeIn.Should().Be(TimeSpan.FromSeconds(1));
        clip.FadeOut.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Undo_returns_the_clip_without_fades()
    {
        var source = MediaSource.FromMedia(Fake.Info(60));
        var sequence = Sequence.FromSource(source);
        var history = new EditHistory(sequence);

        history.Execute(new SetClipFadesCommand(sequence.Video.Clips[0].Id, fadeIn: TimeSpan.FromSeconds(1)));
        history.Undo();

        history.Current.Video.Clips[0].HasFades.Should().BeFalse();
    }

    [Fact]
    public void Dragging_one_edge_stays_a_single_history_entry()
    {
        var source = MediaSource.FromMedia(Fake.Info(60));
        var sequence = Sequence.FromSource(source);
        var history = new EditHistory(sequence);
        var id = sequence.Video.Clips[0].Id;

        // Тащили ползунок: три шага подряд по одному и тому же краю.
        history.Execute(new SetClipFadesCommand(id, fadeIn: TimeSpan.FromSeconds(0.5)));
        history.Execute(new SetClipFadesCommand(id, fadeIn: TimeSpan.FromSeconds(1)));
        history.Execute(new SetClipFadesCommand(id, fadeIn: TimeSpan.FromSeconds(1.5)));

        history.Undo();

        history.Current.Video.Clips[0].FadeIn.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Sequence_reports_picture_edits_for_fades()
    {
        var source = MediaSource.FromMedia(Fake.Info(60));
        var sequence = Sequence.FromSource(source);

        sequence.HasPictureEdits.Should().BeFalse();

        var faded = sequence.WithTrack(
            sequence.Video.Replace(sequence.Video.Clips[0].WithFades(fadeIn: TimeSpan.FromSeconds(1))));

        faded.HasPictureEdits.Should().BeTrue();
        faded.HasRotation.Should().BeFalse();
    }
}
