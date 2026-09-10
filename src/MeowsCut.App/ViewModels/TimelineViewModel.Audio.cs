using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowsCut.App.Timeline;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.History;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Доска монтажа: звуковые дорожки и куски звука на них.
/// </summary>
public sealed partial class TimelineViewModel
{
    /// <summary>
    /// Кладёт звук из файла на аудиодорожку, начиная с плейхеда.
    /// </summary>
    /// <remarks>
    /// Новая дорожка создаётся, только если ни одной ещё нет: иначе каждый
    /// добавленный звук плодил бы полосу, и доска уехала бы за край экрана.
    /// Наложение делается второй дорожкой осознанно — кнопкой «Дорожка».
    /// </remarks>
    public void AppendAudioSource(Project project, MediaSource source)
    {
        _project = project;

        if (_history is null)
        {
            return;
        }

        var clip = AudioClip.FromSource(source, Playhead);

        if (Sequence.AudioTracks.Count == 0)
        {
            var track = AudioTrack.Empty(Localization.Strings.AudioTrackDefaultName) with { Clips = [clip] };
            Execute(new AddAudioTrackCommand(track));
            SelectAudio(track.Id, clip);
        }
        else
        {
            var trackId = Sequence.AudioTracks[^1].Id;
            Execute(new AddAudioClipCommand(trackId, clip));
            SelectAudio(trackId, clip);
        }

        _history.EndMergeGroup();
    }

    /// <summary>Добавляет пустую дорожку — под неё кладут второй слой звука.</summary>
    [RelayCommand(CanExecute = nameof(HasProject))]
    private void AddAudioTrack()
    {
        var title = string.Format(
            System.Globalization.CultureInfo.CurrentUICulture,
            Localization.Strings.AudioTrackNumbered,
            Sequence.AudioTracks.Count + 1);

        Execute(new AddAudioTrackCommand(AudioTrack.Empty(title)));
        _history?.EndMergeGroup();
    }

    /// <summary>Снять звук с выбранного видеоклипа на отдельную дорожку.</summary>
    [RelayCommand(CanExecute = nameof(CanDetachAudio))]
    private void DetachAudio()
    {
        if (SelectedClip is not { } clip)
        {
            return;
        }

        Execute(new DetachClipAudioCommand(clip.Id));
        _history?.EndMergeGroup();
    }

    private bool CanDetachAudio() => SelectedClip is { } clip && clip.Clip.SourceHasAudio;

    [RelayCommand]
    private void RemoveAudioTrack(AudioTrackId trackId)
    {
        Execute(new RemoveAudioTrackCommand(trackId));
        _history?.EndMergeGroup();

        if (SelectedAudioTrack == trackId)
        {
            SelectedAudioClip = null;
        }
    }

    [RelayCommand]
    private void ToggleAudioTrackMuted(AudioTrackId trackId)
    {
        if (Sequence.FindTrack(trackId) is not { } track)
        {
            return;
        }

        Execute(new SetAudioTrackPropertiesCommand(trackId, muted: !track.IsMuted));
        _history?.EndMergeGroup();
    }

    /// <summary>Громкость дорожки целиком — ползунок на её заголовке.</summary>
    public void SetAudioTrackGain(AudioTrackId trackId, double gain)
    {
        if (Sequence.FindTrack(trackId) is not { } track || Math.Abs(track.Gain - gain) < 0.0001)
        {
            return;
        }

        Execute(new SetAudioTrackPropertiesCommand(trackId, gain: gain));
    }

    /// <summary>Свойства выбранного куска звука: громкость, тональность, затухания.</summary>
    public void SetAudioClipProperties(
        double? gain = null,
        int? pitch = null,
        TimeSpan? fadeIn = null,
        TimeSpan? fadeOut = null)
    {
        if (SelectedAudioClip is not { } clip)
        {
            return;
        }

        Execute(new SetAudioClipPropertiesCommand(SelectedAudioTrack, clip.Id, gain, pitch, fadeIn, fadeOut));
        RefreshAudioSelection();
    }

    /// <summary>Завершает серию правок ползунком — дальше история начнёт новую запись.</summary>
    public void EndAudioEdit() => _history?.EndMergeGroup();
}
