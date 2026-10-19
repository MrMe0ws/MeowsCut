using System.Windows;
using System.Windows.Input;
using MeowsCut.App.Timeline;
using MeowsCut.App.ViewModels;

namespace MeowsCut.App.Controls;

/// <summary>
/// Правая кнопка по доске: меню параметров куска.
/// </summary>
/// <remarks>
/// Щелчок сначала выделяет то, что под курсором, — иначе выбранный в меню
/// параметр правил бы прошлое выделение. Пустое место звуковой дорожки
/// открывает меню самой дорожки.
/// </remarks>
public sealed partial class TimelineControl
{
    /// <summary>В меню выбрали параметр: открыть чип рядом с куском.</summary>
    public event EventHandler<ParameterRequestEventArgs>? ParameterRequested;

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);

        ContextMenu = null;

        if (_model is null)
        {
            return;
        }

        var position = e.GetPosition(this);
        var target = _model.PrepareContext(position.X, position.Y);

        if (target.Kind == ContextTargetKind.None)
        {
            return;
        }

        Focus();
        e.Handled = true;

        if (target.Kind == ContextTargetKind.Track)
        {
            if (_model.Sequence.FindTrack(target.TrackId) is { } track)
            {
                ContextMenu = TimelineMenus.ForTrack(_model, track);
            }
        }
        else
        {
            var anchor = AnchorOf(target);
            ContextMenu = TimelineMenus.ForTarget(
                _model,
                target,
                parameter => ParameterRequested?.Invoke(this, new ParameterRequestEventArgs(parameter, target.Kind, anchor)));
        }

        if (ContextMenu is { } menu)
        {
            menu.PlacementTarget = this;
            menu.IsOpen = true;
        }

        InvalidateVisual();
    }

    /// <summary>Видимая часть куска: по времени — его края, по высоте — его полоса.</summary>
    private Rect AnchorOf(ContextTarget target)
    {
        var metrics = _model!.Metrics;
        var left = Math.Clamp(metrics.TimeToX(target.Start), 0, ActualWidth);
        var right = Math.Clamp(metrics.TimeToX(target.End), 0, ActualWidth);

        var lanes = _layout.Build(
            ActualHeight,
            [.. _model.AudioTracks.Select(track => track.Id)],
            _model.Sequence.Titles.Count > 0);

        var kind = target.Kind switch
        {
            ContextTargetKind.Audio => LaneKind.Audio,
            ContextTargetKind.Title => LaneKind.Title,
            _ => LaneKind.Video
        };

        var lane = lanes.FirstOrDefault(item => item.Kind == kind && (kind != LaneKind.Audio || item.TrackId == target.TrackId));

        return new Rect(left, lane.Top, Math.Max(1, right - left), lane.Height);
    }
}
