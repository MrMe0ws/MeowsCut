using CommunityToolkit.Mvvm.ComponentModel;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Рамка выделенного клипа поверх кадра.
/// </summary>
/// <remarks>
/// Рамка видна, когда под курсором времени стоит именно выделенный кусок:
/// тогда кадр в просмотре — его кадр, и правка мышью по кадру относится к нему.
/// Во время воспроизведения рамку прячем — она мешала бы смотреть.
/// </remarks>
public sealed partial class PreviewViewModel
{
    [ObservableProperty]
    private bool _isSelectedClipOnScreen;

    partial void OnIsPlayingChanged(bool value) => RefreshSelectionFrame();

    private void RefreshSelectionFrame()
    {
        var placed = _timeline.Sequence.ClipAt(_timeline.Playhead);

        IsSelectedClipOnScreen = !IsPlaying &&
                                 placed is { } current &&
                                 _timeline.SelectedClip?.Id == current.Clip.Id;
    }
}
