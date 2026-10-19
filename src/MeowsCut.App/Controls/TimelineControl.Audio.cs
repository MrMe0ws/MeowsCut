using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MeowsCut.App.Timeline;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.Controls;

/// <summary>
/// Полосы звука: подложка дорожки, куски и волна столбиками.
/// </summary>
/// <remarks>
/// Волна рисуется столбиками в цвете дорожки — бирюзовая у музыки, фиолетовая
/// у озвучки, как значки слева. Цвет берётся у темы: в светлой он темнее,
/// иначе бледная волна на почти белой подложке теряется.
/// </remarks>
public sealed partial class TimelineControl
{
    /// <summary>Шаг столбиков волны и ширина самого столбика, в пикселях экрана.</summary>
    private const double WavePitch = 3d;

    private const double WaveBarWidth = 1.8d;

    /// <summary>Тишина — не пустота: у неё остаётся тонкая черта по середине.</summary>
    private const double WaveMinHalfHeight = 0.6d;

    private static readonly Typeface CaptionTypeface = new(
        new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private Brush AudioFill => (Brush)FindResource("Brush.AudioClip");

    private Brush MutedAudioFill => (Brush)FindResource("Brush.SurfaceRaised");

    /// <summary>Куски озвучки — фиолетовые, как микрофон у их дорожки слева.</summary>
    private Brush VoiceFill => (Brush)FindResource("Brush.VoiceClip");

    private Brush MusicWave => (Brush)FindResource("Brush.MusicMark");

    private Brush VoiceWave => (Brush)FindResource("Brush.VoiceMark");

    /// <summary>
    /// Полоса звука: подложка и её куски.
    /// </summary>
    /// <remarks>
    /// Волна берётся пиками на весь файл, из которых вырезается кусок
    /// по точкам входа и выхода. Пока она считается, кусок рисуется без неё —
    /// ждать на перерисовке нельзя.
    /// </remarks>
    private void DrawAudioLane(DrawingContext context, TimelineMetrics metrics, AudioTrack track, TimelineLane lane)
    {
        var laneRect = new Rect(0, lane.Top, ActualWidth, lane.Height);
        context.DrawRoundedRectangle(LaneFill, null, laneRect, 4, 4);

        var muted = track.IsMuted;
        var voice = TrackHeaderStrip.IsVoice(track);

        foreach (var clip in track.Clips)
        {
            var left = metrics.TimeToX(clip.TimelineStart);
            var right = metrics.TimeToX(clip.TimelineEnd);

            if (right < 0 || left > ActualWidth)
            {
                continue;
            }

            var rect = new Rect(left + ClipGap, lane.Top + 3, Math.Max(2d, right - left - (ClipGap * 2)), lane.Height - 6);
            var selected = _model?.SelectedAudioClip?.Id == clip.Id;

            // Рамки у куска нет: его край виден по заливке, а рамка остаётся выделению.
            var fill = muted ? MutedAudioFill : voice ? VoiceFill : AudioFill;
            context.DrawRoundedRectangle(fill, selected ? new Pen(Accent, 2) : null, rect, 6, 6);

            DrawWaveform(context, clip, rect, muted ? MutedBrush : voice ? VoiceWave : MusicWave, muted);
            DrawFades(context, metrics, rect, clip.FadeIn, clip.FadeOut);
            DrawAudioCaption(context, clip, rect);
            DrawBadges(context, rect, ClipBadges.For(clip));
        }
    }

    /// <summary>
    /// Волна столбиками, отражёнными от середины куска.
    /// </summary>
    /// <remarks>
    /// Рисуется только видимая часть: у часового трека на крупном масштабе
    /// столбиков были бы десятки тысяч. Сетка столбиков привязана к началу куска,
    /// поэтому при прокрутке волна едет вместе с ним, а не мерцает.
    /// </remarks>
    private void DrawWaveform(DrawingContext context, AudioClip clip, Rect rect, Brush brush, bool muted)
    {
        if (rect.Width < 4 || rect.Height < 10 || _model?.WaveformFor(clip) is not { } wave)
        {
            return;
        }

        var area = new Rect(rect.X + 2, rect.Y + 2, Math.Max(0, rect.Width - 4), Math.Max(0, rect.Height - 4));
        var middle = area.Y + (area.Height / 2);
        var half = area.Height / 2;

        var first = Math.Max(0, (int)Math.Floor((0 - area.X) / WavePitch));
        var last = (int)Math.Ceiling((Math.Min(ActualWidth, area.Right) - area.X) / WavePitch);

        var geometry = new StreamGeometry();

        using (var bars = geometry.Open())
        {
            for (var i = first; i < last; i++)
            {
                var x = area.X + (i * WavePitch);
                var width = Math.Min(WaveBarWidth, area.Right - x);

                if (width <= 0)
                {
                    break;
                }

                var peak = wave.Max((x - area.X) / area.Width, (x + WavePitch - area.X) / area.Width);
                var height = Math.Max(WaveMinHalfHeight, peak * half);

                AddBar(bars, new Rect(x, middle - height, width, height * 2));
            }
        }

        geometry.Freeze();

        context.PushOpacity(muted ? 0.35 : 0.8);
        context.PushClip(new RectangleGeometry(rect, 6, 6));
        context.DrawGeometry(brush, null, geometry);
        context.Pop();
        context.Pop();
    }

    private static void AddBar(StreamGeometryContext bars, Rect bar)
    {
        bars.BeginFigure(bar.TopLeft, isFilled: true, isClosed: true);
        bars.LineTo(bar.TopRight, isStroked: false, isSmoothJoin: false);
        bars.LineTo(bar.BottomRight, isStroked: false, isSmoothJoin: false);
        bars.LineTo(bar.BottomLeft, isStroked: false, isSmoothJoin: false);
    }

    /// <summary>Имя куска — сверху слева, как в карточке: волна под ним читается целиком.</summary>
    private void DrawAudioCaption(DrawingContext context, AudioClip clip, Rect rect)
    {
        if (rect.Width < 40 || rect.Height < 22)
        {
            return;
        }

        // Тональность и громкость ушли в значки, громкость дорожки — к её значку.
        var caption = clip.Title.Length > 0 ? clip.Title : Localization.Strings.SectionSound;

        var text = new FormattedText(
            caption,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            CaptionTypeface,
            10.5,
            TextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(10, rect.Width - 12),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };

        context.DrawText(text, new Point(rect.X + 7, rect.Y + 3));
    }
}
