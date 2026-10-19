using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MeowsCut.App.Localization;
using MeowsCut.App.Timeline;
using MeowsCut.App.ViewModels;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.Controls;

/// <summary>
/// Колонка значков слева от доски: какая полоса что несёт.
/// </summary>
/// <remarks>
/// Подписи «Дорожка 1 · 5%» поверх полосы закрывали начало первого куска.
/// Теперь у каждой полосы значок в своей колонке: плёнка — видеоряд, «Т» —
/// надписи, нота — музыка, микрофон — озвучка; цвет значка совпадает с цветом
/// кусков на полосе. Имя и громкость дорожки — в подсказке под курсором.
/// Щелчок по значку звуковой дорожки выключает и включает её звук,
/// правая кнопка открывает меню дорожки.
///
/// Раскладка полос берётся из того же <see cref="TimelineLayout"/>, что и у доски,
/// поэтому значки стоят ровно напротив своих полос.
/// </remarks>
public sealed class TrackHeaderStrip : FrameworkElement
{
    private const double IconSize = 16d;

    private readonly TimelineLayout _layout = new();
    private TimelineViewModel? _model;
    private TimelineLane? _hovered;

    public TrackHeaderStrip()
    {
        SnapsToDevicePixels = true;
        DataContextChanged += OnDataContextChanged;
        ToolTipService.SetInitialShowDelay(this, 300);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_model is not null)
        {
            _model.VisualInvalidated -= OnInvalidated;
        }

        _model = e.NewValue as TimelineViewModel;

        if (_model is not null)
        {
            _model.VisualInvalidated += OnInvalidated;
        }

        InvalidateVisual();
    }

    private void OnInvalidated(object? sender, EventArgs e) => InvalidateVisual();

    private IReadOnlyList<TimelineLane> Lanes() => _model is null
        ? []
        : _layout.Build(ActualHeight, [.. _model.AudioTracks.Select(track => track.Id)], _model.Sequence.Titles.Count > 0);

    private Brush Res(string key) => (Brush)FindResource(key);

    protected override void OnRender(DrawingContext context)
    {
        context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (_model is null || !_model.HasProject)
        {
            return;
        }

        foreach (var lane in Lanes())
        {
            var track = lane.Kind == LaneKind.Audio ? _model.Sequence.FindTrack(lane.TrackId) : null;
            var hovered = _hovered is { } h && h.Kind == lane.Kind && h.TrackId == lane.TrackId;

            var cell = new Rect(4, lane.Top, ActualWidth - 8, lane.Height);
            context.DrawRoundedRectangle(hovered ? Res("Brush.Hover") : Res("Brush.Background"), null, cell, 6, 6);

            var (key, brushKey) = Glyph(lane.Kind, track);
            if (TryFindResource(key) is not Geometry geometry)
            {
                continue;
            }

            var pen = new Pen(Res(brushKey), 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var scale = IconSize / 24d;
            var x = cell.X + (cell.Width - IconSize) / 2;
            var y = cell.Y + (cell.Height - IconSize) / 2;

            context.PushOpacity(track?.IsMuted == true ? 0.55 : 1d);
            context.PushTransform(new MatrixTransform(scale, 0, 0, scale, x, y));
            context.DrawGeometry(null, pen, geometry);
            context.Pop();
            context.Pop();
        }
    }

    private static (string Icon, string Brush) Glyph(LaneKind kind, AudioTrack? track) => kind switch
    {
        LaneKind.Video => ("Icon.Film", "Brush.TextMuted"),
        LaneKind.Title => ("Icon.Text", "Brush.TitleMark"),
        _ when track?.IsMuted == true => ("Icon.VolumeOff", "Brush.TextMuted"),
        _ when IsVoice(track) => ("Icon.Mic", "Brush.VoiceMark"),
        _ => ("Icon.Note", "Brush.MusicMark")
    };

    /// <summary>Дорожка озвучки: записи с микрофона ложатся на дорожки с таким именем.</summary>
    public static bool IsVoice(AudioTrack? track) =>
        track is not null && track.Title.StartsWith(Strings.VoiceoverTrackName, StringComparison.CurrentCultureIgnoreCase);

    private TimelineLane? LaneAt(double y) => _layout.LaneAt(Lanes(), y);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var lane = LaneAt(e.GetPosition(this).Y);
        if (lane == _hovered)
        {
            return;
        }

        _hovered = lane;
        ToolTip = lane is { } value ? Describe(value) : null;
        Cursor = lane?.Kind == LaneKind.Audio ? Cursors.Hand : null;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = null;
        InvalidateVisual();
    }

    private string Describe(TimelineLane lane)
    {
        if (lane.Kind == LaneKind.Video)
        {
            return Strings.TrackVideo;
        }

        if (lane.Kind == LaneKind.Title)
        {
            return Strings.TrackTitles;
        }

        if (_model?.Sequence.FindTrack(lane.TrackId) is not { } track)
        {
            return string.Empty;
        }

        var state = track.IsMuted
            ? Strings.TrackMuted
            : string.Format(CultureInfo.CurrentCulture, "{0:0}%", track.Gain * 100);

        return $"{track.Title} · {state}\n{Strings.TrackHeaderHint}";
    }

    /// <summary>Щелчок по значку дорожки звука — выключить или вернуть её звук.</summary>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_model is not null && LaneAt(e.GetPosition(this).Y) is { Kind: LaneKind.Audio } lane)
        {
            _model.ToggleAudioTrackMutedCommand.Execute(lane.TrackId);
            ToolTip = Describe(lane);
            e.Handled = true;
        }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);

        if (_model is null || LaneAt(e.GetPosition(this).Y) is not { Kind: LaneKind.Audio } lane ||
            _model.Sequence.FindTrack(lane.TrackId) is not { } track)
        {
            return;
        }

        var menu = TimelineMenus.ForTrack(_model, track);
        menu.PlacementTarget = this;
        menu.IsOpen = true;
        e.Handled = true;
    }
}
