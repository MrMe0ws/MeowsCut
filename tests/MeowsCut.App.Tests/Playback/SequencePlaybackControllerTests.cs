using FluentAssertions;
using MeowsCut.App.Playback;
using MeowsCut.App.Tests.Support;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.Tests.Playback;

public class SequencePlaybackControllerTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static (SequencePlaybackController Playback, FakeMediaPlayer Player, Project Project) Create(
        double seconds = 20)
    {
        var project = Fake.Project(seconds);
        var player = new FakeMediaPlayer();
        var playback = new SequencePlaybackController(player);
        playback.Attach(project);

        return (playback, player, project);
    }

    [Fact]
    public void Attaching_opens_the_source_and_stands_at_the_beginning()
    {
        var (playback, player, project) = Create();

        player.Source.Should().Be(project.Sources[0].FilePath);
        player.Position.Should().Be(TimeSpan.Zero);
        playback.Position.Should().Be(TimeSpan.Zero);
        playback.IsPlaying.Should().BeFalse();
    }

    [Fact]
    public void Seeking_maps_timeline_time_to_source_time()
    {
        var (playback, player, _) = Create();

        playback.Seek(S(7));

        player.Position.Should().Be(S(7));
        playback.Position.Should().Be(S(7));
    }

    [Fact]
    public void Seeking_accounts_for_trimmed_in_point()
    {
        var project = Fake.Project(20);
        var trimmed = project.Sequence.WithTrack(
            project.Sequence.Video.Replace(project.Sequence.Video.Clips[0].TrimStart(S(5))));

        var player = new FakeMediaPlayer();
        var playback = new SequencePlaybackController(player);
        playback.Attach(project.WithSequence(trimmed));

        playback.Seek(S(2));

        // Две секунды таймлайна — это седьмая секунда исходника.
        player.Position.Should().Be(S(7));
    }

    [Fact]
    public void Seeking_applies_clip_speed_and_volume()
    {
        var project = Fake.Project(20);
        var sequence = new SetClipSpeedCommand(project.Sequence.Video.Clips[0].Id, 2d).Apply(project.Sequence);
        sequence = new SetClipAudioCommand(sequence.Video.Clips[0].Id, new ClipAudio(true, 0.5)).Apply(sequence);

        var player = new FakeMediaPlayer();
        var playback = new SequencePlaybackController(player);
        playback.Attach(project.WithSequence(sequence));

        playback.Seek(S(1));

        player.SpeedRatio.Should().Be(2d);
        player.Volume.Should().Be(0.5);
        player.Position.Should().Be(S(2), "при двукратной скорости секунда таймлайна — две секунды исходника");
    }

    [Fact]
    public void Muted_clip_plays_silently()
    {
        var project = Fake.Project(20);
        var sequence = new SetClipAudioCommand(project.Sequence.Video.Clips[0].Id, ClipAudio.Muted)
            .Apply(project.Sequence);

        var player = new FakeMediaPlayer();
        var playback = new SequencePlaybackController(player);
        playback.Attach(project.WithSequence(sequence));

        playback.Seek(S(1));

        player.Volume.Should().Be(0d);
    }

    [Fact]
    public void Playing_moves_the_timeline_position()
    {
        var (playback, player, _) = Create();
        var positions = new List<TimeSpan>();
        playback.PositionChanged += (_, position) => positions.Add(position);

        playback.Play();
        player.Advance(S(1));
        playback.Tick();

        playback.Position.Should().Be(S(1));
        positions.Should().Contain(S(1));
    }

    [Fact]
    public void Playing_a_sped_up_clip_moves_the_playhead_slower_than_the_source()
    {
        var project = Fake.Project(20);
        var sequence = new SetClipSpeedCommand(project.Sequence.Video.Clips[0].Id, 2d).Apply(project.Sequence);

        var player = new FakeMediaPlayer();
        var playback = new SequencePlaybackController(player);
        playback.Attach(project.WithSequence(sequence));

        playback.Play();
        player.Advance(S(4));
        playback.Tick();

        playback.Position.Should().Be(S(2), "клип идёт вдвое быстрее, значит таймлайн проходит вдвое меньше");
    }

    [Fact]
    public void Playback_moves_to_the_next_clip_at_the_boundary()
    {
        var project = Fake.Project(20);
        var sequence = new SplitClipCommand(S(5)).Apply(project.Sequence);
        var trimmedSecond = sequence.Video.Clips[1].TrimStart(S(3));    // второй клип начинается с 8 с исходника
        sequence = sequence.WithTrack(sequence.Video.Replace(trimmedSecond));

        var player = new FakeMediaPlayer();
        var playback = new SequencePlaybackController(player);
        playback.Attach(project.WithSequence(sequence));

        playback.Play();
        player.Position = S(5);      // дошли до конца первого клипа
        playback.Tick();

        player.Position.Should().Be(S(8), "второй клип начинается с восьмой секунды исходника");
        playback.IsPlaying.Should().BeTrue("на стыке воспроизведение не прерывается");
    }

    [Fact]
    public void Playback_stops_at_the_end_of_the_sequence()
    {
        var (playback, player, _) = Create();

        playback.Play();
        player.Position = S(20);
        playback.Tick();

        playback.IsPlaying.Should().BeFalse();
        playback.Position.Should().Be(S(20));
    }

    [Fact]
    public void Play_from_the_end_starts_over()
    {
        var (playback, player, _) = Create();

        playback.Seek(S(20));
        playback.Play();

        playback.Position.Should().Be(TimeSpan.Zero);
        player.Position.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Pausing_stops_position_updates()
    {
        var (playback, player, _) = Create();

        playback.Play();
        playback.Pause();

        player.Advance(S(3));
        playback.Tick();

        playback.Position.Should().Be(TimeSpan.Zero);
        player.IsPlaying.Should().BeFalse();
    }

    [Fact]
    public void Seeking_inside_one_source_does_not_reopen_the_file()
    {
        var (playback, player, _) = Create();

        playback.Seek(S(3));
        playback.Seek(S(9));
        playback.Seek(S(15));

        player.OpenCount.Should().Be(1, "перезагрузка файла на каждую перемотку — это заметные подвисания");
    }

    [Fact]
    public void Position_is_applied_after_the_file_reports_it_is_open()
    {
        var project = Fake.Project(20);
        var player = new FakeMediaPlayer { OpenImmediately = false };
        var playback = new SequencePlaybackController(player);

        playback.Attach(project);
        playback.Seek(S(6));

        player.Position.Should().Be(TimeSpan.Zero, "до открытия позиция бессмысленна");

        player.CompleteOpen();

        player.Position.Should().Be(S(6));
    }

    [Fact]
    public void A_file_the_system_cannot_open_switches_to_frames()
    {
        var (playback, player, _) = Create();

        player.FailToOpen();

        playback.IsPlayerUnavailable.Should().BeTrue();
        playback.IsPlaying.Should().BeFalse();

        playback.Play();
        playback.IsPlaying.Should().BeFalse("играть нечем, но приложение не должно делать вид, что играет");
    }

    [Fact]
    public void Editing_during_pause_refreshes_the_shown_frame()
    {
        var project = Fake.Project(20);
        var player = new FakeMediaPlayer();
        var playback = new SequencePlaybackController(player);
        playback.Attach(project);

        playback.Seek(S(10));

        // Обрезали начало клипа: под тем же временем таймлайна теперь другой кадр.
        var trimmed = project.Sequence.WithTrack(
            project.Sequence.Video.Replace(project.Sequence.Video.Clips[0].TrimStart(S(5))));

        playback.UpdateSequence(trimmed);

        player.Position.Should().Be(S(15));
    }

    [Fact]
    public void Shortened_sequence_pulls_the_position_back()
    {
        var project = Fake.Project(20);
        var player = new FakeMediaPlayer();
        var playback = new SequencePlaybackController(player);
        playback.Attach(project);

        playback.Seek(S(18));

        var shortened = project.Sequence.WithTrack(
            project.Sequence.Video.Replace(project.Sequence.Video.Clips[0].TrimEnd(S(-10))));

        playback.UpdateSequence(shortened);

        playback.Position.Should().Be(S(10));
    }

    [Fact]
    public void Detaching_releases_the_file()
    {
        var (playback, player, _) = Create();

        playback.Detach();

        player.Source.Should().BeNull();
        playback.IsPlaying.Should().BeFalse();
    }
}
