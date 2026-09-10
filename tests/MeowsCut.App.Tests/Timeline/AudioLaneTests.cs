using FluentAssertions;
using MeowsCut.App.Timeline;
using MeowsCut.App.ViewModels;
using MeowsCut.App.Tests.Support;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Media;

namespace MeowsCut.App.Tests.Timeline;

/// <summary>
/// Полосы доски: раскладка по высоте и попадание мышью.
/// </summary>
/// <remarks>
/// Здесь стерегут ровно одну ошибку, которую глазами не найти: щелчок попадает
/// не в ту полосу, что нарисована. Поэтому раскладка одна на отрисовку и на
/// тест попаданий, и эти тесты проверяют именно её.
/// </remarks>
public class TimelineLayoutTests
{
    [Fact]
    public void Without_sound_the_whole_board_belongs_to_video()
    {
        var lanes = new TimelineLayout().Build(240, []);

        lanes.Should().HaveCount(1);
        lanes[0].Kind.Should().Be(LaneKind.Video);
        lanes[0].Bottom.Should().BeApproximately(240 - TimelineLayout.TrackPadding, 0.01);
    }

    [Fact]
    public void Audio_lanes_go_under_the_video_and_do_not_overlap()
    {
        var tracks = new[] { AudioTrackId.New(), AudioTrackId.New() };

        var lanes = new TimelineLayout().Build(300, tracks);

        lanes.Should().HaveCount(3);
        lanes[1].Top.Should().BeGreaterThanOrEqualTo(lanes[0].Bottom);
        lanes[2].Top.Should().BeGreaterThanOrEqualTo(lanes[1].Bottom);
        lanes[1].TrackId.Should().Be(tracks[0]);
    }

    [Fact]
    public void The_video_lane_never_collapses_completely()
    {
        // Дорожек больше, чем помещается: видеоряд обязан остаться видимым,
        // иначе доска превращается в набор полос без картинки.
        var tracks = Enumerable.Range(0, 10).Select(_ => AudioTrackId.New()).ToArray();

        var lanes = new TimelineLayout().Build(200, tracks);

        lanes[0].Height.Should().BeGreaterThanOrEqualTo(TimelineLayout.MinVideoLaneHeight);
    }
}

/// <summary>Правка звука на доске: выделение, перенос, обрезка, разрез.</summary>
public class AudioEditingTests
{
    private const double PixelsPerSecond = 50;

    private static double X(double seconds) => seconds * PixelsPerSecond;

    /// <summary>Y внутри первой полосы звука при высоте доски 300.</summary>
    private static double AudioY(TimelineViewModel timeline)
    {
        var lanes = new TimelineLayout().Build(
            timeline.ViewportHeight,
            [.. timeline.AudioTracks.Select(track => track.Id)]);

        var lane = lanes[1];
        return lane.Top + (lane.Height / 2);
    }

    private static TimelineViewModel Attached(double soundStart = 2)
    {
        var timeline = new TimelineViewModel();
        var project = Fake.Project(20);

        timeline.Attach(project);
        timeline.Metrics.ViewportWidth = 1000;
        timeline.Metrics.PixelsPerSecond = PixelsPerSecond;
        timeline.Metrics.Scroll = TimeSpan.Zero;
        timeline.ViewportHeight = 300;
        timeline.SnapEnabled = false;

        var music = MediaSource.FromMedia(Fake.Info(10));
        var withMusic = project with { Sources = [.. project.Sources, music] };

        timeline.AppendAudioSource(withMusic, music);

        // Звук ложится от плейхеда, то есть с нуля; двигаем его в нужную точку.
        if (soundStart > 0)
        {
            var y = AudioY(timeline);
            timeline.PointerDown(X(0), y);
            timeline.PointerMove(X(soundStart), y);
            timeline.PointerUp();
        }

        return timeline;
    }

    [Fact]
    public void Adding_sound_creates_a_track_and_selects_the_clip()
    {
        var timeline = Attached(soundStart: 0);

        timeline.AudioTracks.Should().HaveCount(1);
        timeline.HasAudioSelection.Should().BeTrue();
    }

    [Fact]
    public void Clicking_a_sound_selects_it_and_drops_the_video_selection()
    {
        var timeline = Attached(soundStart: 2);

        // Сначала выбираем видеоклип...
        timeline.PointerDown(X(5), 60);
        timeline.PointerUp();
        timeline.SelectedClip.Should().NotBeNull();

        // ...потом кусок звука: два выделения не складываются.
        timeline.PointerDown(X(3), AudioY(timeline));
        timeline.PointerUp();

        timeline.HasAudioSelection.Should().BeTrue();
        timeline.SelectedClip.Should().BeNull("иначе непонятно, к чему относится «громкость»");
    }

    [Fact]
    public void Dragging_a_sound_moves_it_in_time()
    {
        var timeline = Attached(soundStart: 2);
        var y = AudioY(timeline);

        timeline.PointerDown(X(3), y);
        timeline.PointerMove(X(6), y);
        timeline.PointerUp();

        timeline.AudioTracks[0].Clips[0].TimelineStart.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Dragging_a_sound_is_a_single_undo_step()
    {
        var timeline = Attached(soundStart: 2);
        var y = AudioY(timeline);

        timeline.PointerDown(X(3), y);
        for (var i = 1; i <= 20; i++)
        {
            timeline.PointerMove(X(3) + (i * 5), y);
        }

        timeline.PointerUp();
        timeline.UndoCommand.Execute(null);

        timeline.AudioTracks[0].Clips[0].TimelineStart.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void The_razor_cuts_sound_too()
    {
        var timeline = Attached(soundStart: 0);
        timeline.ActiveTool = TimelineTool.Razor;

        timeline.PointerDown(X(4), AudioY(timeline));
        timeline.PointerUp();

        timeline.AudioTracks[0].Clips.Should().HaveCount(2);
        timeline.Clips.Should().HaveCount(1, "разрез по звуку не трогает видеоряд");
    }

    [Fact]
    public void Deleting_removes_the_selected_sound_not_the_video()
    {
        var timeline = Attached(soundStart: 2);

        timeline.PointerDown(X(3), AudioY(timeline));
        timeline.PointerUp();
        timeline.DeleteSelectedCommand.Execute(null);

        timeline.AudioTracks[0].Clips.Should().BeEmpty();
        timeline.Clips.Should().HaveCount(1);
    }

    [Fact]
    public void Detaching_audio_puts_it_on_a_track()
    {
        var timeline = new TimelineViewModel();
        timeline.Attach(Fake.Project(20));
        timeline.Metrics.ViewportWidth = 1000;
        timeline.Metrics.PixelsPerSecond = PixelsPerSecond;
        timeline.ViewportHeight = 300;

        timeline.PointerDown(X(5), 60);
        timeline.PointerUp();

        timeline.DetachAudioCommand.Execute(null);

        timeline.AudioTracks.Should().HaveCount(1);
        timeline.Clips[0].Clip.Audio.Enabled.Should().BeFalse();
    }
}
