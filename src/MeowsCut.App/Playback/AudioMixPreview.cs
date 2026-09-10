using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.Playback;

/// <summary>
/// Наложенный звук в предпросмотре: отдельные дорожки поверх звука видеоряда.
/// </summary>
/// <remarks>
/// Своего микшера здесь нет — каждая дорожка играет своим MediaPlayer,
/// а сводит их звуковая подсистема Windows. Точность стыков поэтому такая же,
/// как у самого предпросмотра (ADR-14): на перемотке дорожки подтягиваются к общей
/// позиции, а расхождение внутри воспроизведения выправляется по порогу.
/// Всё, что система не умеет — сдвиг тональности, затухания, усиление выше единицы, —
/// остаётся привилегией экспорта.
/// </remarks>
public sealed class AudioMixPreview
{
    private readonly List<AudioLanePlayer> _lanes = [];

    private Sequence _sequence = Sequence.Empty;
    private Project? _project;

    public void Attach(Project project)
    {
        _project = project;
        _sequence = project.Sequence;
    }

    public void UpdateProject(Project project) => _project = project;

    public void UpdateSequence(Sequence sequence) => _sequence = sequence;

    public void Detach()
    {
        foreach (var lane in _lanes)
        {
            lane.Close();
        }

        _project = null;
        _sequence = Sequence.Empty;
    }

    /// <summary>
    /// Приводит дорожки к состоянию таймлайна.
    /// </summary>
    /// <param name="seeked">
    /// Перемотка, а не обычный шаг. При перемотке позиция задаётся всем дорожкам жёстко.
    /// </param>
    public void Sync(TimeSpan position, bool playing, bool seeked)
    {
        var tracks = _sequence.AudioTracks;
        EnsureLanes(tracks.Count);

        for (var i = 0; i < _lanes.Count; i++)
        {
            if (i >= tracks.Count)
            {
                _lanes[i].Silence();
                continue;
            }

            var track = tracks[i];
            var clip = track.ClipAt(position);
            var path = clip is null ? null : _project?.Find(clip.SourceId)?.FilePath;

            _lanes[i].Sync(track, path, position, playing, seeked);
        }
    }

    /// <summary>
    /// Заводит проигрыватели под число дорожек. Лишние не удаляются, а замолкают:
    /// дорожку часто добавляют и убирают, а пересоздание элемента стоит открытия файла.
    /// </summary>
    private void EnsureLanes(int count)
    {
        while (_lanes.Count < count)
        {
            _lanes.Add(new AudioLanePlayer());
        }
    }
}
