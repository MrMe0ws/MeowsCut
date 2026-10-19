using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeowsCut.App.Localization;
using MeowsCut.App.ViewModels;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.Controls;

/// <summary>
/// Меню по правой кнопке на доске: параметры куска и действия с ним.
/// </summary>
/// <remarks>
/// Пункт-параметр не меняет значение сам, а открывает рядом с куском чип,
/// где его выставляют ползунком, колесом или числом. Так правка идёт там же,
/// куда смотрит глаз, — не надо переводить взгляд на панель свойств.
/// Перед каждым пунктом значок: найти «Затухание» по клину быстрее, чем по слову.
/// </remarks>
public static class TimelineMenus
{
    public static ContextMenu ForTarget(TimelineViewModel model, ContextTarget target, Action<ClipParameter> open)
    {
        var menu = new ContextMenu();
        var items = menu.Items;

        switch (target.Kind)
        {
            case ContextTargetKind.Clip:
                items.Add(Parameter(Strings.CtxSpeed, "Icon.Speed", ClipParameter.Speed, open));
                items.Add(Parameter(Strings.CtxVolume, "Icon.Volume", ClipParameter.Volume, open));
                items.Add(Separator());
                items.Add(Parameter(Strings.CtxFadeIn, "Icon.Fade", ClipParameter.FadeIn, open));
                items.Add(Parameter(Strings.CtxFadeOut, "Icon.FadeOut", ClipParameter.FadeOut, open));
                items.Add(Separator());
                items.Add(Parameter(Strings.CtxFit, "Icon.Frame", ClipParameter.Fit, open));
                items.Add(Parameter(Strings.CtxRotate, "Icon.RotateRight", ClipParameter.Rotation, open));
                items.Add(Parameter(Strings.CtxZoom, "Icon.Zoom", ClipParameter.Zoom, open));
                items.Add(Parameter(Strings.CtxPosition, "Icon.MoveHorizontal", ClipParameter.Position, open));
                items.Add(Parameter(Strings.CtxMotion, "Icon.Motion", ClipParameter.Motion, open));
                items.Add(Separator());
                items.Add(Action(Strings.CtxSplitHere, "Icon.Split", () => model.SplitAt(target)));
                items.Add(Command(Strings.CtxDuplicate, "Icon.Duplicate", model.DuplicateSelectedCommand, "Ctrl+D"));
                items.Add(Command(Strings.CtxDetachAudio, "Icon.Note", model.DetachAudioCommand));
                items.Add(Command(Strings.CtxDelete, "Icon.Trash", model.DeleteSelectedCommand, "Del"));
                break;

            case ContextTargetKind.Audio:
                items.Add(Parameter(Strings.CtxVolume, "Icon.Volume", ClipParameter.AudioGain, open));
                items.Add(Parameter(Strings.CtxSpeed, "Icon.Speed", ClipParameter.AudioSpeed, open));
                items.Add(Parameter(Strings.CtxPitch, "Icon.Note", ClipParameter.Pitch, open));
                items.Add(Separator());
                items.Add(Parameter(Strings.CtxFadeIn, "Icon.Fade", ClipParameter.AudioFadeIn, open));
                items.Add(Parameter(Strings.CtxFadeOut, "Icon.FadeOut", ClipParameter.AudioFadeOut, open));
                items.Add(Separator());
                items.Add(Parameter(Strings.CtxTrackGain, "Icon.Layers", ClipParameter.TrackGain, open));
                items.Add(Command(Strings.CtxTrackMute, "Icon.VolumeOff", model.ToggleAudioTrackMutedCommand, parameter: target.TrackId));
                items.Add(Separator());
                items.Add(Action(Strings.CtxSplitHere, "Icon.Split", () => model.SplitAt(target)));
                items.Add(Command(Strings.CtxDelete, "Icon.Trash", model.DeleteSelectedCommand, "Del"));
                break;

            case ContextTargetKind.Title:
                items.Add(Parameter(Strings.CtxTitleText, "Icon.Text", ClipParameter.TitleText, open));
                items.Add(Parameter(Strings.CtxTitleDuration, "Icon.Speed", ClipParameter.TitleDuration, open));
                items.Add(Parameter(Strings.CtxTitlePlace, "Icon.Frame", ClipParameter.TitlePlace, open));
                items.Add(Parameter(Strings.CtxTitleSize, "Icon.Zoom", ClipParameter.TitleSize, open));
                items.Add(Parameter(Strings.CtxTitleColor, "Icon.Sliders", ClipParameter.TitleColor, open));
                items.Add(Separator());
                items.Add(Command(Strings.CtxDelete, "Icon.Trash", model.DeleteSelectedCommand, "Del"));
                break;
        }

        return menu;
    }

    /// <summary>Меню дорожки звука: по пустому месту дорожки и по её значку слева.</summary>
    public static ContextMenu ForTrack(TimelineViewModel model, AudioTrack track)
    {
        var menu = new ContextMenu();

        menu.Items.Add(Command(
            track.IsMuted ? Strings.TrackUnmute : Strings.CtxTrackMute,
            track.IsMuted ? "Icon.Volume" : "Icon.VolumeOff",
            model.ToggleAudioTrackMutedCommand,
            parameter: track.Id));
        menu.Items.Add(Separator());
        menu.Items.Add(Command(Strings.RemoveTrack, "Icon.Trash", model.RemoveAudioTrackCommand, parameter: track.Id));

        return menu;
    }

    private static MenuItem Parameter(string header, string icon, ClipParameter parameter, Action<ClipParameter> open) =>
        Action(header, icon, () => open(parameter));

    private static MenuItem Action(string header, string icon, Action action)
    {
        var item = Item(header, icon);
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem Command(string header, string icon, ICommand command, string gesture = "", object? parameter = null)
    {
        var item = Item(header, icon);
        item.Command = command;
        item.CommandParameter = parameter;
        item.InputGestureText = gesture;
        return item;
    }

    private static MenuItem Item(string header, string icon) => new()
    {
        Header = header,
        Icon = Application.Current?.TryFindResource(icon)
    };

    private static Separator Separator() => new()
    {
        Style = Application.Current?.TryFindResource("MenuSeparator") as Style
    };
}
