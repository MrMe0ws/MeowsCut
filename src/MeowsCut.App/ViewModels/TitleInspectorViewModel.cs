using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowsCut.App.Formatting;
using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.ViewModels;

/// <summary>Место надписи на кадре — пункт списка в инспекторе.</summary>
public sealed record TitleAnchorOption(TitleAnchor Anchor, string Title)
{
    public static readonly IReadOnlyList<TitleAnchorOption> All =
    [
        new(TitleAnchor.TopLeft, "Сверху слева"),
        new(TitleAnchor.TopCenter, "Сверху по центру"),
        new(TitleAnchor.TopRight, "Сверху справа"),
        new(TitleAnchor.MiddleLeft, "По середине слева"),
        new(TitleAnchor.MiddleCenter, "По центру"),
        new(TitleAnchor.MiddleRight, "По середине справа"),
        new(TitleAnchor.BottomLeft, "Снизу слева"),
        new(TitleAnchor.BottomCenter, "Снизу по центру"),
        new(TitleAnchor.BottomRight, "Снизу справа")
    ];

    public static TitleAnchorOption For(TitleAnchor anchor) =>
        All.FirstOrDefault(option => option.Anchor == anchor) ?? All[7];

    public override string ToString() => Title;
}

/// <summary>Готовый цвет надписи. Палитра вместо колеса: чаще всего хватает пяти.</summary>
public sealed record TitleColorOption(string Value, string Title)
{
    public static readonly IReadOnlyList<TitleColorOption> All =
    [
        new("#FFFFFF", "Белый"),
        new("#000000", "Чёрный"),
        new("#FFD400", "Жёлтый"),
        new("#FF4D4D", "Красный"),
        new("#4DD2FF", "Голубой")
    ];

    public static TitleColorOption For(string value) =>
        All.FirstOrDefault(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase))
        ?? All[0];

    public override string ToString() => Title;
}

/// <summary>
/// Свойства выбранной надписи: текст, время, место и вид.
/// </summary>
/// <remarks>
/// Как и у клипа, правки идут только через команды таймлайна — иначе
/// история отмен рассыпалась бы.
/// </remarks>
public sealed partial class TitleInspectorViewModel : ObservableObject
{
    private readonly TimelineViewModel _timeline;
    private bool _updating;

    public TitleInspectorViewModel(TimelineViewModel timeline)
    {
        _timeline = timeline;

        _timeline.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TimelineViewModel.SelectedTitle))
            {
                Refresh();
            }
        };

        _timeline.SequenceChanged += (_, _) => Refresh();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private TitleClip? _title;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private double _startSeconds;

    [ObservableProperty]
    private double _durationSeconds = 3d;

    [ObservableProperty]
    private double _scalePercent = 6d;

    [ObservableProperty]
    private TitleAnchorOption _anchor = TitleAnchorOption.All[7];

    [ObservableProperty]
    private TitleColorOption _color = TitleColorOption.All[0];

    [ObservableProperty]
    private bool _backdrop = true;

    public bool HasSelection => Title is not null;

    public IReadOnlyList<TitleAnchorOption> Anchors { get; } = TitleAnchorOption.All;

    public IReadOnlyList<TitleColorOption> Colors { get; } = TitleColorOption.All;

    public string RangeText => Title is { } title
        ? $"{DisplayFormat.Duration(title.TimelineStart)} → {DisplayFormat.Duration(title.TimelineEnd)}"
        : string.Empty;

    private void Refresh()
    {
        _updating = true;

        Title = _timeline.SelectedTitle;

        if (Title is { } title)
        {
            Text = title.Text;
            StartSeconds = Math.Round(title.TimelineStart.TotalSeconds, 2);
            DurationSeconds = Math.Round(title.Duration.TotalSeconds, 2);
            ScalePercent = Math.Round(title.Scale * 100, 1);
            Anchor = TitleAnchorOption.For(title.Anchor);
            Color = TitleColorOption.For(title.Color);
            Backdrop = title.Backdrop;
        }

        _updating = false;

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(RangeText));
    }

    partial void OnTextChanged(string value)
    {
        if (_updating || Title is not { } title || title.Text == value)
        {
            return;
        }

        _timeline.ChangeTitle(new SetTitleTextCommand(title.Id, value));
    }

    partial void OnStartSecondsChanged(double value)
    {
        if (_updating || Title is not { } title)
        {
            return;
        }

        var start = TimeSpan.FromSeconds(Math.Max(0, value));
        if (Math.Abs((title.TimelineStart - start).TotalSeconds) < 0.01)
        {
            return;
        }

        _timeline.ChangeTitle(new MoveTitleCommand(title.Id, start));
    }

    partial void OnDurationSecondsChanged(double value)
    {
        if (_updating || Title is not { } title)
        {
            return;
        }

        var duration = TimeSpan.FromSeconds(value);
        if (Math.Abs((title.Duration - duration).TotalSeconds) < 0.01)
        {
            return;
        }

        _timeline.ChangeTitle(new SetTitleDurationCommand(title.Id, duration));
    }

    partial void OnScalePercentChanged(double value) => ApplyLook(scale: value / 100d);

    partial void OnAnchorChanged(TitleAnchorOption value) => ApplyLook(anchor: value.Anchor);

    partial void OnColorChanged(TitleColorOption value) => ApplyLook(color: value.Value);

    partial void OnBackdropChanged(bool value) => ApplyLook(backdrop: value);

    private void ApplyLook(
        TitleAnchor? anchor = null,
        double? scale = null,
        string? color = null,
        bool? backdrop = null)
    {
        if (_updating || Title is not { } title)
        {
            return;
        }

        _timeline.ChangeTitle(new SetTitleLookCommand(title.Id, anchor, scale, color, backdrop));
    }

    [RelayCommand]
    private void Remove()
    {
        if (Title is { } title)
        {
            _timeline.RemoveTitle(title.Id);
        }
    }

    /// <summary>Ползунки отпущены — следующая правка станет отдельной записью в истории.</summary>
    [RelayCommand]
    private void EndInteraction() => _timeline.EndInteraction();
}
