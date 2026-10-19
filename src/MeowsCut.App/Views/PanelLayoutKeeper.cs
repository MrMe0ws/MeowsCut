using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MeowsCut.App.ViewModels;
using MeowsCut.Core.Configuration;

namespace MeowsCut.App.Views;

/// <summary>
/// Раскладка главного окна: возвращает размеры панелей с прошлого запуска,
/// запоминает новые и даёт доске вырасти под добавленные дорожки звука.
/// </summary>
/// <remarks>
/// Высот у доски две. Своя — та, что пользователь выставил разделителем; она
/// и сохраняется. Нужная — под видеоряд и первые три дорожки. Показывается
/// большая из них, поэтому удалённая дорожка возвращает доске прежний размер,
/// а не оставляет её раздутой.
/// </remarks>
internal sealed class PanelLayoutKeeper
{
    private const double MinSourcesShare = 0.1;
    private const double MaxSourcesShare = 0.9;

    private readonly ShellWindow _window;
    private readonly ShellViewModel _viewModel;

    private double _ownBoardHeight;
    private double _sourcesShare;
    /// <summary>Высота под дорожки. null — ещё не считали, 0 — пользователь ужал доску сам.</summary>
    private double? _fitBoardHeight;

    public PanelLayoutKeeper(ShellWindow window, ShellViewModel viewModel)
    {
        _window = window;
        _viewModel = viewModel;
        _ownBoardHeight = window.BoardRow.Height.Value;
        _sourcesShare = Share(window.SourcesRow.Height.Value, window.InspectorRow.Height.Value);

        Restore(viewModel.SavedLayout);

        window.Workspace.SizeChanged += (_, _) => Apply();
        viewModel.Timeline.PropertyChanged += OnTimelineChanged;
    }

    /// <summary>Разделитель отпустили: размер, выставленный руками, становится своим.</summary>
    public void OnSplitterMoved(object sender)
    {
        if (sender is GridSplitter splitter && ReferenceEquals(splitter.Parent, _window.Workspace))
        {
            // Доску ужали ниже того, что ей нужно под дорожки, — значит, так и задумано:
            // сама она больше не растёт, пока дорожек не станет другое число.
            _ownBoardHeight = _window.BoardRow.ActualHeight;
            _fitBoardHeight = 0d;
        }

        Save();
    }

    public void Save()
    {
        var sources = _window.SourcesRow.ActualHeight;
        var inspector = _window.InspectorRow.ActualHeight;
        if (sources > 0 && inspector > 0)
        {
            _sourcesShare = Share(sources, inspector);
        }

        var side = _window.SideColumn.ActualWidth > 0
            ? _window.SideColumn.ActualWidth
            : _window.SideColumn.Width.Value;

        var maximized = _window.WindowState == WindowState.Maximized;
        var size = _window.WindowState == WindowState.Normal
            ? new Size(_window.ActualWidth, _window.ActualHeight)
            : _window.RestoreBounds.Size;

        _ = _viewModel.SaveLayoutAsync(new PanelLayout(
            Math.Round(_ownBoardHeight),
            Math.Round(side),
            Math.Round(_sourcesShare, 3),
            Math.Round(size.Width),
            Math.Round(size.Height),
            maximized));
    }

    private void Restore(PanelLayout? layout)
    {
        if (layout is null)
        {
            return;
        }

        var area = SystemParameters.WorkArea;
        if (layout.WindowWidth > 0 && layout.WindowHeight > 0)
        {
            _window.Width = Math.Clamp(layout.WindowWidth, _window.MinWidth, Math.Max(_window.MinWidth, area.Width));
            _window.Height = Math.Clamp(layout.WindowHeight, _window.MinHeight, Math.Max(_window.MinHeight, area.Height));
        }

        if (layout.Maximized)
        {
            _window.WindowState = WindowState.Maximized;
        }

        var side = _window.SideColumn;
        if (layout.SideWidth > 0)
        {
            side.Width = new GridLength(Math.Clamp(layout.SideWidth, side.MinWidth, side.MaxWidth));
        }

        if (layout.SourcesShare > 0)
        {
            _sourcesShare = Math.Clamp(layout.SourcesShare, MinSourcesShare, MaxSourcesShare);
            _window.SourcesRow.Height = new GridLength(_sourcesShare, GridUnitType.Star);
            _window.InspectorRow.Height = new GridLength(1 - _sourcesShare, GridUnitType.Star);
        }

        if (layout.BoardHeight > 0)
        {
            _ownBoardHeight = layout.BoardHeight;
            _window.BoardRow.Height = new GridLength(Math.Max(layout.BoardHeight, _window.BoardRow.MinHeight));
        }
    }

    private void OnTimelineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TimelineViewModel.FitHeight))
        {
            return;
        }

        // Доска перестраивается после того, как разметка узнает о новых дорожках:
        // сразу после правки высота панели инструментов над ней ещё не посчитана.
        _window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _fitBoardHeight = NeededBoardHeight();
            Apply();
        });
    }

    /// <summary>Высота доски вместе с панелью инструментов над ней.</summary>
    private double? NeededBoardHeight()
    {
        var pane = _window.TimelinePane;
        if (pane.ActualHeight <= 0 || pane.Board.ActualHeight <= 0)
        {
            return null;
        }

        var toolbar = pane.ActualHeight - pane.Board.ActualHeight;
        return _viewModel.Timeline.FitHeight + toolbar;
    }

    private void Apply()
    {
        var workspace = _window.Workspace;
        if (workspace.ActualHeight <= 0)
        {
            return;
        }

        _fitBoardHeight ??= NeededBoardHeight();

        var row = _window.BoardRow;
        var preview = workspace.RowDefinitions[0].MinHeight;
        var splitter = workspace.RowDefinitions[1].ActualHeight;
        var most = Math.Max(row.MinHeight, workspace.ActualHeight - preview - splitter);

        var target = Math.Clamp(Math.Max(_ownBoardHeight, _fitBoardHeight ?? 0d), row.MinHeight, most);

        if (Math.Abs(row.ActualHeight - target) > 0.5)
        {
            row.Height = new GridLength(target);
        }
    }

    private static double Share(double sources, double inspector) =>
        Math.Clamp(sources / (sources + inspector), MinSourcesShare, MaxSourcesShare);
}
