using FluentAssertions;
using MeowsCut.App.Tests.Support;
using MeowsCut.App.Timeline;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Media;

namespace MeowsCut.App.Tests.Timeline;

/// <summary>
/// Значки на куске: что показывается, а что нет.
/// </summary>
/// <remarks>
/// Здесь стерегут две ошибки в разные стороны. Первая — значок не появился,
/// и правку на ленте не видно: ровно с этого всё и началось, затухание было
/// заметно только в свойствах выбранного куска. Вторая — значок появился там,
/// где ничего не делали: тогда лента пестрит, и ответить по ней, какой кусок
/// тронут, снова нельзя.
/// </remarks>
public class ClipBadgeTests
{
    private static Clip Video()
    {
        var source = MediaSource.FromMedia(Fake.Info(30));
        return Clip.FromSource(source, new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)));
    }

    private static Clip Photo()
    {
        var source = MediaSource.FromMedia(Fake.Info(30, path: @"C:\фото\кадр.jpg"));
        return Clip.FromSource(source);
    }

    private static AudioClip Sound()
    {
        var source = MediaSource.FromMedia(Fake.Info(30, path: @"C:\звук\музыка.m4a"));
        return AudioClip.FromSource(source, TimeSpan.Zero);
    }

    [Fact]
    public void An_untouched_clip_carries_no_badges()
    {
        ClipBadges.For(Video()).Should().BeEmpty();
    }

    [Fact]
    public void Speed_shows_up()
    {
        ClipBadges.For(Video().WithSpeed(2d)).Should().Equal(ClipBadge.Speed);
    }

    [Fact]
    public void Switched_off_sound_shows_up()
    {
        ClipBadges.For(Video().WithAudio(ClipAudio.Muted)).Should().Equal(ClipBadge.Muted);
    }

    [Fact]
    public void Changed_volume_shows_up_instead_of_the_muted_sign()
    {
        // Одно из двух: «выключен» и «тише обычного» — разные вещи, и показывать
        // их вместе значило бы поставить два значка про один и тот же звук.
        ClipBadges.For(Video().WithAudio(new ClipAudio(true, 0.4)))
            .Should().Equal(ClipBadge.Volume);
    }

    [Fact]
    public void A_photo_is_not_marked_as_silenced()
    {
        // Звука в фотографии нет вовсе, и значок «без звука» означал бы правку,
        // которой не делали.
        ClipBadges.For(Photo()).Should().BeEmpty();
    }

    [Fact]
    public void Rotation_and_framing_are_separate_signs()
    {
        var rotated = Video().WithTransform(ClipTransform.Identity.WithRotation(90));
        var zoomed = Video().WithTransform(ClipTransform.Identity.WithZoom(1.5));
        var both = Video().WithTransform(ClipTransform.Identity.WithRotation(180).WithZoom(1.5));

        ClipBadges.For(rotated).Should().Equal(ClipBadge.Rotation);
        ClipBadges.For(zoomed).Should().Equal(ClipBadge.Framing);
        ClipBadges.For(both).Should().Equal(ClipBadge.Rotation, ClipBadge.Framing);
    }

    [Fact]
    public void Shifted_frame_counts_as_framing()
    {
        var moved = Video().WithTransform(ClipTransform.Identity with { OffsetX = 0.2 });

        ClipBadges.For(moved).Should().Equal(ClipBadge.Framing);
    }

    [Fact]
    public void Fades_get_no_badge_of_their_own()
    {
        // Затухание рисуется прямо на кадрах и своей длиной: значок сказал бы
        // только «есть», а пять секунд от половины секунды не отличить.
        var faded = Video().WithFades(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        ClipBadges.For(faded).Should().BeEmpty();
    }

    [Fact]
    public void The_order_of_signs_does_not_depend_on_the_order_of_edits()
    {
        // Глаз привыкает к месту: «повёрнут ли этот кусок» видно, не перечитывая ряд.
        var first = Video()
            .WithTransform(ClipTransform.Identity.WithRotation(90))
            .WithAudio(ClipAudio.Muted)
            .WithSpeed(2d);

        var second = Video()
            .WithSpeed(2d)
            .WithTransform(ClipTransform.Identity.WithRotation(90))
            .WithAudio(ClipAudio.Muted);

        ClipBadges.For(first).Should().Equal(ClipBadge.Speed, ClipBadge.Muted, ClipBadge.Rotation);
        ClipBadges.For(second).Should().Equal(ClipBadges.For(first));
    }

    [Fact]
    public void An_untouched_piece_of_sound_carries_no_badges()
    {
        ClipBadges.For(Sound()).Should().BeEmpty();
    }

    [Fact]
    public void Sound_shows_speed_volume_and_pitch()
    {
        var edited = Sound() with { Gain = 0.5, PitchSemitones = 3 };

        ClipBadges.For(edited.WithSpeed(1.5))
            .Should().Equal(ClipBadge.Speed, ClipBadge.Volume, ClipBadge.Pitch);
    }
}
