using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Media;

namespace MeowsCut.Core.Editing;

/// <summary>
/// Проект: добавленные файлы и собранная из них последовательность.
/// Источники хранятся отдельно от клипов, поэтому один файл можно резать
/// на сколько угодно кусков, не дублируя его характеристики.
/// </summary>
public sealed record Project(IReadOnlyList<MediaSource> Sources, Sequence Sequence)
{
    public static readonly Project Empty = new([], Sequence.Empty);

    public bool IsEmpty => Sequence.IsEmpty;

    /// <summary>Создаёт проект из одного файла: источник плюс клип на всю длину.</summary>
    public static Project FromMedia(MediaInfo info)
    {
        var source = MediaSource.FromMedia(info);
        return new Project([source], Sequence.FromSource(source));
    }

    public MediaSource? Find(SourceId id) => Sources.FirstOrDefault(source => source.Id == id);

    public MediaSource Require(SourceId id) =>
        Find(id) ?? throw new EditOperationException($"Источник {id} отсутствует в проекте.");

    /// <summary>
    /// Тот же файл, добавленный повторно, не создаёт второй источник:
    /// иначе на таймлайне появились бы клипы-двойники с разными идентификаторами.
    /// </summary>
    public (Project Project, MediaSource Source) WithSource(MediaInfo info)
    {
        var existing = Sources.FirstOrDefault(source =>
            string.Equals(source.FilePath, info.FilePath, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            return (this, existing);
        }

        var source = MediaSource.FromMedia(info);
        var sources = new List<MediaSource>(Sources) { source };

        var project = this with { Sources = sources };

        // Формат последовательности задаёт первый добавленный файл.
        if (Sequence.IsEmpty && Sources.Count == 0)
        {
            project = project with { Sequence = Sequence with { Format = SequenceFormat.FromMedia(info) } };
        }

        return (project, source);
    }

    public Project WithSequence(Sequence sequence) => this with { Sequence = sequence };

    /// <summary>Источники, реально используемые клипами — их и подаём на вход ffmpeg.</summary>
    public IReadOnlyList<MediaSource> UsedSources()
    {
        var used = new List<MediaSource>();

        foreach (var clip in Sequence.Video.Clips)
        {
            if (used.Any(source => source.Id == clip.SourceId))
            {
                continue;
            }

            var source = Find(clip.SourceId);
            if (source is not null)
            {
                used.Add(source);
            }
        }

        return used;
    }
}
