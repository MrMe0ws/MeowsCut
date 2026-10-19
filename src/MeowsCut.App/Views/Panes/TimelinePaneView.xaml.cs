using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MeowsCut.App.Controls;
using MeowsCut.App.Localization;
using MeowsCut.App.ViewModels;

namespace MeowsCut.App.Views.Panes;

/// <summary>
/// Доска монтажа с панелью инструментов и чипом параметра у куска.
/// </summary>
/// <remarks>
/// Чип открывается из меню по правой кнопке. Он правит те же модели, что и
/// панель свойств справа, поэтому два места правки всегда показывают одно и то же,
/// а undo склеивает правку так же, как с ползунка в панели.
/// </remarks>
public partial class TimelinePaneView : UserControl
{
    private static readonly Dictionary<ClipParameter, (string Title, string Icon)> Captions = new()
    {
        [ClipParameter.Speed] = (Strings.CtxSpeed, "Icon.Speed"),
        [ClipParameter.Volume] = (Strings.CtxVolume, "Icon.Volume"),
        [ClipParameter.FadeIn] = (Strings.CtxFadeIn, "Icon.Fade"),
        [ClipParameter.FadeOut] = (Strings.CtxFadeOut, "Icon.FadeOut"),
        [ClipParameter.Fit] = (Strings.CtxFit, "Icon.Frame"),
        [ClipParameter.Rotation] = (Strings.CtxRotate, "Icon.RotateRight"),
        [ClipParameter.Zoom] = (Strings.CtxZoom, "Icon.Zoom"),
        [ClipParameter.Position] = (Strings.CtxPosition, "Icon.MoveHorizontal"),
        [ClipParameter.Motion] = (Strings.CtxMotion, "Icon.Motion"),
        [ClipParameter.AudioGain] = (Strings.CtxVolume, "Icon.Volume"),
        [ClipParameter.AudioSpeed] = (Strings.CtxSpeed, "Icon.Speed"),
        [ClipParameter.Pitch] = (Strings.CtxPitch, "Icon.Note"),
        [ClipParameter.AudioFadeIn] = (Strings.CtxFadeIn, "Icon.Fade"),
        [ClipParameter.AudioFadeOut] = (Strings.CtxFadeOut, "Icon.FadeOut"),
        [ClipParameter.TrackGain] = (Strings.CtxTrackGain, "Icon.Layers"),
        [ClipParameter.TitleText] = (Strings.CtxTitleText, "Icon.Text"),
        [ClipParameter.TitleDuration] = (Strings.CtxTitleDuration, "Icon.Speed"),
        [ClipParameter.TitlePlace] = (Strings.CtxTitlePlace, "Icon.Frame"),
        [ClipParameter.TitleSize] = (Strings.CtxTitleSize, "Icon.Zoom"),
        [ClipParameter.TitleColor] = (Strings.CtxTitleColor, "Icon.Sliders")
    };

    public TimelinePaneView() => InitializeComponent();

    private void OnParameterRequested(object? sender, ParameterRequestEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is not ShellViewModel shell)
        {
            return;
        }

        // Карточка живёт во всплывающем окне и не наследует данные окна —
        // модели раздаются ей явно.
        VideoEditors.DataContext = shell.Inspector;
        AudioEditors.DataContext = shell.AudioInspector;
        TitleEditors.DataContext = shell.TitleInspector;

        VideoEditors.Visibility = Show(e.Kind == ContextTargetKind.Clip);
        AudioEditors.Visibility = Show(e.Kind == ContextTargetKind.Audio);
        TitleEditors.Visibility = Show(e.Kind == ContextTargetKind.Title);

        VideoEditors.Parameter = AudioEditors.Parameter = TitleEditors.Parameter = e.Parameter;

        var (title, icon) = Captions[e.Parameter];
        ChipTitle.Text = title;
        ChipIcon.Data = TryFindResource(icon) as Geometry;

        Chip.ShowOver(e.Anchor);
    }

    private void OnChipClose(object sender, RoutedEventArgs e) => Chip.IsOpen = false;

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
