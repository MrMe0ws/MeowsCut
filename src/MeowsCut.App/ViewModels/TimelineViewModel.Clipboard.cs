using CommunityToolkit.Mvvm.Input;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Доска монтажа: буфер обмена.
/// </summary>
/// <remarks>
/// Буфер свой, а не системный: в него кладутся клипы последовательности — ссылки
/// на кусок источника с его обрезкой, скоростью и звуком, — а не файлы. Отдавать
/// такое в системный буфер бессмысленно: другая программа этого не поймёт,
/// а копирование самого видео нарушило бы правило «видео не держим в памяти».
/// </remarks>
public sealed partial class TimelineViewModel
{
    private IReadOnlyList<Clip> _clipboardVideo = [];
    private AudioClip? _clipboardAudio;

    public bool CanPaste => _clipboardVideo.Count > 0 || _clipboardAudio is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopySelected()
    {
        if (SelectedAudioClip is { } audio)
        {
            _clipboardAudio = audio;
            _clipboardVideo = [];
        }
        else
        {
            // Порядок дорожки, а не порядок выделения: вставленный ряд обязан
            // повторять то, что пользователь видит на ленте.
            _clipboardVideo = [.. SelectedClips.Select(clip => clip.Clip)];
            _clipboardAudio = null;
        }

        PasteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CutSelected()
    {
        CopySelected();
        DeleteSelected();
    }

    /// <summary>
    /// Вставляет содержимое буфера у плейхеда.
    /// </summary>
    /// <remarks>
    /// Звук встаёт ровно в плейхед — на аудиодорожке место произвольное. Видео
    /// встаёт на ближайший стык: резать клип пополам вставкой пользователь не просил,
    /// а «ровно в плейхед» посреди кадра означало бы именно это.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanPaste))]
    private void Paste()
    {
        if (_history is null)
        {
            return;
        }

        if (_clipboardAudio is { } audio)
        {
            PasteAudio(audio);
            return;
        }

        if (_clipboardVideo.Count == 0)
        {
            return;
        }

        // Новые идентификаторы обязательны: старые уже заняты на дорожке,
        // и вставка совпала бы с оригиналом при любой следующей правке.
        // Первый кусок прижимается к стыку, остальные встают за ним встык.
        var copies = _clipboardVideo
            .Select((clip, i) => clip with
            {
                Id = ClipId.New(),
                LeadingGap = i == 0 ? TimeSpan.Zero : clip.LeadingGap
            })
            .ToArray();

        Execute(new InsertClipsCommand(ResolveInsertIndex(), copies));
        _history.EndMergeGroup();

        SelectClips(copies.Select(clip => clip.Id));
    }

    private void PasteAudio(AudioClip audio)
    {
        var copy = audio with { Id = AudioClipId.New(), TimelineStart = Playhead };

        var trackId = Sequence.FindTrack(SelectedAudioTrack) is not null
            ? SelectedAudioTrack
            : Sequence.AudioTracks.Count > 0
                ? Sequence.AudioTracks[^1].Id
                : default;

        if (Sequence.FindTrack(trackId) is null)
        {
            // Дорожки не осталось — вставлять некуда, поэтому заводим её вместе с куском.
            var track = AudioTrack.Empty(Localization.Strings.AudioTrackDefaultName) with { Clips = [copy] };
            Execute(new AddAudioTrackCommand(track));
            SelectAudio(track.Id, copy);
        }
        else
        {
            Execute(new AddAudioClipCommand(trackId, copy));
            SelectAudio(trackId, copy);
        }

        _history?.EndMergeGroup();
    }

    /// <summary>Ближайший к плейхеду стык между клипами.</summary>
    private int ResolveInsertIndex()
    {
        var index = Sequence.ClipCount;

        foreach (var placed in Sequence.EnumeratePlaced())
        {
            if (Playhead < placed.Start + TimeSpan.FromTicks(placed.Clip.TimelineDuration.Ticks / 2))
            {
                index = placed.Index;
                break;
            }
        }

        return index;
    }

    private void SelectClips(IEnumerable<ClipId> ids)
    {
        _selection.Clear();

        foreach (var id in ids)
        {
            _selection.Add(id);
        }

        SelectedAudioClip = null;
        SelectedClip = Clips.FirstOrDefault(clip => _selection.Contains(clip.Id));
        ApplySelectionToClips();
    }
}
