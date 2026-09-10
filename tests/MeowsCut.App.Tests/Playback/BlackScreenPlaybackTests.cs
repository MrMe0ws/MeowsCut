using FluentAssertions;
using MeowsCut.App.Playback;
using MeowsCut.App.Tests.Support;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Media;

namespace MeowsCut.App.Tests.Playback;

/// <summary>
/// Воспроизведение там, где картинки нет: зазор между клипами и хвост под музыку.
/// </summary>
/// <remarks>
/// Обе дыры пользователь нашёл руками: в зазоре плеер перепрыгивал на следующий клип,
/// а в конце видеоряда вставал, хотя музыка ещё шла. Часы здесь подставные —
/// иначе проверить ход времени можно было бы только реальным ожиданием.
/// </remarks>
public class BlackScreenPlaybackTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    /// <summary>Подставные часы: время идёт ровно тогда, когда его двигает тест.</summary>
    private sealed class TestClock
    {
        public TimeSpan Now { get; private set; }

        public void Advance(TimeSpan delta) => Now += delta;
    }

    /// <summary>Два клипа по пять секунд с зазором в три секунды между ними.</summary>
    private static Project WithGap()
    {
        var project = Fake.Project(20);

        var sequence = project.Sequence.SplitAt(S(5));
        sequence = sequence.WithTrack(
            sequence.Video.Replace(sequence.Video.Clips[1].WithLeadingGap(S(3))));

        return project.WithSequence(sequence);
    }

    /// <summary>Пять секунд видео и двенадцать секунд музыки поверх.</summary>
    private static Project WithLongMusic()
    {
        var project = Fake.Project(5);
        var music = MediaSource.FromMedia(Fake.Info(12, path: @"C:\звук\музыка.m4a"));

        var withMusic = project with { Sources = [.. project.Sources, music] };

        var track = AudioTrack.Empty("Музыка") with
        {
            Clips = [AudioClip.FromSource(music, TimeSpan.Zero)]
        };

        return withMusic.WithSequence(withMusic.Sequence.WithTracks([track]));
    }

    private static (SequencePlaybackController Playback, FakeMediaPlayer Player, TestClock Clock) Create(
        Project project)
    {
        var clock = new TestClock();
        var player = new FakeMediaPlayer();
        var playback = new SequencePlaybackController(player, () => clock.Now);

        playback.Attach(project);

        return (playback, player, clock);
    }

    [Fact]
    public void A_gap_is_played_through_and_not_jumped_over()
    {
        var (playback, player, clock) = Create(WithGap());

        playback.Seek(S(4.9));
        playback.Play();

        // Первый клип доиграл: раньше здесь происходил прыжок к началу второго.
        player.Advance(S(0.2));
        playback.Tick();

        playback.Position.Should().BeCloseTo(S(5), TimeSpan.FromMilliseconds(120),
            "в зазоре показывают чёрный экран, а не следующий клип");
        playback.IsPlaying.Should().BeTrue();

        // Время в зазоре идёт по часам.
        clock.Advance(S(1));
        playback.Tick();

        playback.Position.Should().BeCloseTo(S(6), TimeSpan.FromMilliseconds(120));
        playback.IsPlaying.Should().BeTrue();
    }

    [Fact]
    public void The_next_clip_starts_when_the_gap_ends()
    {
        var (playback, player, clock) = Create(WithGap());

        playback.Seek(S(6));
        playback.Play();

        clock.Advance(S(2.1));
        playback.Tick();

        playback.Position.Should().BeGreaterThanOrEqualTo(S(8));
        playback.IsPlaying.Should().BeTrue();
        player.IsPlaying.Should().BeTrue("после зазора картинка обязана вернуться");
    }

    [Fact]
    public void Playback_continues_past_the_video_while_the_music_goes_on()
    {
        var (playback, player, clock) = Create(WithLongMusic());

        playback.Seek(S(4.9));
        playback.Play();

        player.Advance(S(0.2));
        playback.Tick();

        playback.Position.Should().BeCloseTo(S(5), TimeSpan.FromMilliseconds(120));
        playback.IsPlaying.Should().BeTrue("музыка ещё идёт — останавливаться незачем");

        clock.Advance(S(3));
        playback.Tick();

        playback.Position.Should().BeCloseTo(S(8), TimeSpan.FromMilliseconds(120));
        playback.IsPlaying.Should().BeTrue();
    }

    [Fact]
    public void At_the_very_end_of_the_music_playback_stops()
    {
        var (playback, player, clock) = Create(WithLongMusic());

        playback.Seek(S(4.9));
        playback.Play();

        player.Advance(S(0.2));
        playback.Tick();

        clock.Advance(S(20));
        playback.Tick();

        playback.Position.Should().Be(S(12));
        playback.IsPlaying.Should().BeFalse();
    }

    [Fact]
    public void Without_a_tail_the_last_frame_stays_on_screen()
    {
        var project = Fake.Project(6);
        var (playback, player, _) = Create(project);

        playback.Seek(S(6));

        // Ролик кончился ровно на последнем кадре: пустой экран здесь выглядел бы
        // сбоем перемотки, а не задумкой.
        player.Source.Should().Be(project.Sources[0].FilePath);
    }
}
