using FluentAssertions;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Media;

namespace MeowsCut.Core.Tests.Editing;

/// <summary>
/// Вырезание пауз: арифметика, ради которой это вообще отделено от ffmpeg.
/// </summary>
/// <remarks>
/// Клип уже обрезан с обеих сторон, может идти на другой скорости, а одна пауза
/// файла попадает сразу в несколько кусков ленты. Проверять такое запусками
/// ffmpeg было бы и медленно, и бесполезно: ошибка в пересчёте времени выглядит
/// как «вырезало не там», а не как падение.
/// </remarks>
public class SilenceTrimmerTests
{
    private static readonly SilenceOptions NoPadding =
        SilenceOptions.Default with { Padding = TimeSpan.Zero, MinDuration = TimeSpan.FromMilliseconds(500) };

    private static Sequence Single(double from = 0, double to = 30)
    {
        var source = MediaSource.FromMedia(Fake.Info(60));
        var clip = Clip.FromSource(source, new TimeRange(Sec(from), Sec(to)));

        return new Sequence(new VideoTrack([clip]), SequenceFormat.FromMedia(source.Info));
    }

    private static TimeSpan Sec(double value) => TimeSpan.FromSeconds(value);

    private static Dictionary<SourceId, IReadOnlyList<TimeRange>> Silence(
        Sequence sequence,
        params (double From, double To)[] ranges) =>
        new()
        {
            [sequence.Video.Clips[0].SourceId] =
                ranges.Select(range => new TimeRange(Sec(range.From), Sec(range.To))).ToArray()
        };

    [Fact]
    public void Without_silence_the_montage_stays_as_it_was()
    {
        var sequence = Single();

        var result = SilenceTrimmer.Trim(sequence, Silence(sequence), NoPadding);

        result.IsEmpty.Should().BeTrue();
        result.Sequence.Should().BeSameAs(sequence);
    }

    [Fact]
    public void A_pause_in_the_middle_splits_the_clip_in_two()
    {
        var sequence = Single();

        var result = SilenceTrimmer.Trim(sequence, Silence(sequence, (10, 14)), NoPadding);

        result.RemovedCount.Should().Be(1);
        result.RemovedDuration.Should().Be(Sec(4));

        var clips = result.Sequence.Video.Clips;
        clips.Should().HaveCount(2);
        clips[0].SourceRange.Should().Be(new TimeRange(Sec(0), Sec(10)));
        clips[1].SourceRange.Should().Be(new TimeRange(Sec(14), Sec(30)));

        // Ролик стал короче ровно на вырезанное, а не на что-то ещё.
        result.Sequence.Duration.Should().Be(Sec(26));
    }

    [Fact]
    public void Pieces_stand_end_to_end_without_a_gap()
    {
        // Иначе на месте каждой вырезанной паузы появлялся бы чёрный кадр —
        // ровно то, от чего избавлялись.
        var sequence = Single();

        var result = SilenceTrimmer.Trim(sequence, Silence(sequence, (5, 8), (15, 19)), NoPadding);

        result.Sequence.Video.Clips.Skip(1).Should().OnlyContain(clip => clip.LeadingGap == TimeSpan.Zero);
        result.Sequence.Video.HasGaps.Should().BeFalse();
    }

    [Fact]
    public void Padding_leaves_air_around_the_pause()
    {
        var sequence = Single();
        var options = NoPadding with { Padding = TimeSpan.FromMilliseconds(200) };

        var result = SilenceTrimmer.Trim(sequence, Silence(sequence, (10, 14)), options);

        var clips = result.Sequence.Video.Clips;
        clips[0].SourceRange.End.Should().Be(Sec(10.2));
        clips[1].SourceRange.Start.Should().Be(Sec(13.8));
    }

    [Fact]
    public void Pause_shorter_than_asked_is_left_alone()
    {
        var sequence = Single();

        var result = SilenceTrimmer.Trim(sequence, Silence(sequence, (10, 10.3)), NoPadding);

        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Silence_outside_the_clip_does_not_count()
    {
        // Клип взят с 10-й по 20-ю секунду файла, а пауза лежит на 30-й:
        // в ленте её попросту нет.
        var sequence = Single(from: 10, to: 20);

        var result = SilenceTrimmer.Trim(sequence, Silence(sequence, (30, 35)), NoPadding);

        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Silence_hanging_over_the_edge_is_cut_by_the_edge()
    {
        var sequence = Single(from: 10, to: 20);

        var result = SilenceTrimmer.Trim(sequence, Silence(sequence, (8, 13)), NoPadding);

        result.RemovedCount.Should().Be(1);
        result.RemovedDuration.Should().Be(Sec(3), "вырезать можно только то, что было на ленте");
        result.Sequence.Video.Clips.Should().ContainSingle()
            .Which.SourceRange.Should().Be(new TimeRange(Sec(13), Sec(20)));
    }

    [Fact]
    public void Sped_up_clip_loses_less_timeline_time()
    {
        // Четыре секунды исходника на двойной скорости — это две секунды ленты.
        var sequence = Single();
        var sped = sequence.WithTrack(sequence.Video.Replace(sequence.Video.Clips[0].WithSpeed(2d)));

        var result = SilenceTrimmer.Trim(sped, Silence(sequence, (10, 14)), NoPadding);

        result.RemovedDuration.Should().Be(Sec(2));
    }

    [Fact]
    public void Clip_without_sound_is_not_touched()
    {
        // Тишина в выключенном куске — не пауза в речи, а его свойство.
        var sequence = Single();
        var muted = sequence.WithTrack(
            sequence.Video.Replace(sequence.Video.Clips[0].WithAudio(ClipAudio.Muted)));

        var result = SilenceTrimmer.Trim(muted, Silence(sequence, (10, 14)), NoPadding);

        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Whole_clip_of_silence_leaves_the_montage_alone()
    {
        // Резать нечего: вырезав всё, мы оставили бы на доске пустое место.
        var sequence = Single(from: 0, to: 10);

        var result = SilenceTrimmer.Trim(sequence, Silence(sequence, (0, 10)), NoPadding);

        result.Sequence.Video.Clips.Should().ContainSingle();
    }
}
