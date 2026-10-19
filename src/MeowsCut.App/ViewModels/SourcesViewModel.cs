using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowsCut.App.Formatting;
using MeowsCut.App.Timeline;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.ViewModels;

/// <summary>Что показывать в панели медиа.</summary>
public enum SourceFilter
{
    All = 0,
    Video,
    Image,
    Audio
}

/// <summary>
/// Файлы, добавленные в проект.
/// </summary>
/// <remarks>
/// Список нужен по двум причинам. Первая — вспомнить, что вообще приложено:
/// после десятка перетаскиваний это уже не очевидно. Вторая — положить файл
/// на доску ещё раз, не открывая диалог: один и тот же файл режут на несколько
/// кусков чаще, чем добавляют новый.
///
/// Картинки и видео идут сеткой обложек, звук — отдельным списком под ними:
/// у звука нет кадра, и пустая клетка в сетке ничего бы не сказала.
/// </remarks>
public sealed partial class SourcesViewModel(SourceThumbnailLoader? thumbnails = null) : ObservableObject
{
    private readonly List<ProjectFileViewModel> _all = [];

    public ObservableCollection<ProjectFileViewModel> Files { get; } = [];

    /// <summary>Видео и фото — сеткой обложек.</summary>
    public ObservableCollection<ProjectFileViewModel> VisualFiles { get; } = [];

    /// <summary>Звук — списком под сеткой.</summary>
    public ObservableCollection<ProjectFileViewModel> AudioFiles { get; } = [];

    public bool HasFiles => Files.Count > 0;

    public int Count => Files.Count;

    public bool HasVisualFiles => VisualFiles.Count > 0;

    public bool HasAudioFiles => AudioFiles.Count > 0;

    [ObservableProperty]
    private SourceFilter _filter;

    /// <summary>Кладёт файл на доску: видео и фото — в видеоряд, звук — на дорожку.</summary>
    public Action<MediaSource>? AddToBoard { get; set; }

    public Action<string>? ShowInFolder { get; set; }

    public void Update(Project? project)
    {
        // Обложки, что уже готовы, переживают пересборку списка: иначе каждое
        // добавление файла заново гоняло бы ffmpeg по всем остальным.
        var ready = _all.Where(file => file.Thumbnail is not null)
            .ToDictionary(file => file.Path, file => file.Thumbnail!);

        _all.Clear();

        foreach (var source in project?.Sources ?? [])
        {
            var file = new ProjectFileViewModel(source, this);
            if (ready.TryGetValue(file.Path, out var image))
            {
                file.Thumbnail = image;
            }

            _all.Add(file);
        }

        Files.Clear();
        foreach (var file in _all)
        {
            Files.Add(file);
        }

        MarkUsage(project?.Sequence);
        ApplyFilter();
        thumbnails?.Load(_all);

        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(Count));
    }

    /// <summary>Отмечает файлы, куски которых лежат на доске.</summary>
    public void MarkUsage(Sequence? sequence)
    {
        var used = new HashSet<SourceId>();

        if (sequence is not null)
        {
            foreach (var clip in sequence.Video.Clips)
            {
                used.Add(clip.SourceId);
            }

            foreach (var clip in sequence.AudioTracks.SelectMany(track => track.Clips))
            {
                used.Add(clip.SourceId);
            }
        }

        foreach (var file in _all)
        {
            file.IsOnBoard = used.Contains(file.Source.Id);
        }
    }

    partial void OnFilterChanged(SourceFilter value) => ApplyFilter();

    private void ApplyFilter()
    {
        VisualFiles.Clear();
        AudioFiles.Clear();

        foreach (var file in _all)
        {
            var visible = Filter switch
            {
                SourceFilter.Video => file.IsVideo,
                SourceFilter.Image => file.IsImage,
                SourceFilter.Audio => file.IsAudio,
                _ => true
            };

            if (visible)
            {
                (file.IsAudio ? AudioFiles : VisualFiles).Add(file);
            }
        }

        OnPropertyChanged(nameof(HasVisualFiles));
        OnPropertyChanged(nameof(HasAudioFiles));
    }
}

/// <summary>Один файл в списке проекта.</summary>
public sealed partial class ProjectFileViewModel(MediaSource source, SourcesViewModel owner) : ObservableObject
{
    public MediaSource Source { get; } = source;

    public string Title => Source.DisplayName;

    public string Path => Source.FilePath;

    public bool IsImage => Source.IsImage;

    public bool IsAudio => !Source.IsImage && !Source.Info.HasVideo;

    public bool IsVideo => !IsImage && !IsAudio;

    /// <summary>Обложка; null — ещё готовится или у файла её нет (звук).</summary>
    [ObservableProperty]
    private System.Windows.Media.ImageSource? _thumbnail;

    /// <summary>Куски файла уже лежат на доске — галочка на обложке.</summary>
    [ObservableProperty]
    private bool _isOnBoard;

    /// <summary>Чем этот файл будет на доске. От этого зависит и место, куда он ляжет.</summary>
    public string Kind => IsImage
        ? Localization.Strings.SourceKindImage
        : IsVideo
            ? Localization.Strings.SourceKindVideo
            : Localization.Strings.SourceKindAudio;

    /// <summary>Плашка на обложке: длина у видео и звука, слово «фото» у снимка.</summary>
    public string Badge => IsImage
        ? Localization.Strings.SourceBadgeImage
        : DisplayFormat.Duration(Source.Duration);

    /// <summary>
    /// Короткая справка: у видео размер кадра и длина, у фотографии —
    /// только размер (длины у неё нет), у звука — длина.
    /// </summary>
    public string Details
    {
        get
        {
            var video = Source.Info.PrimaryVideo;

            if (IsImage)
            {
                return video is null ? string.Empty : $"{video.Size.Width}×{video.Size.Height}";
            }

            var duration = DisplayFormat.Duration(Source.Duration);

            return video is null
                ? duration
                : $"{video.Size.Width}×{video.Size.Height} · {duration}";
        }
    }

    /// <summary>Подсказка над обложкой: полное имя, что это и где лежит.</summary>
    public string Hint => $"{Title}\n{Kind} · {Details}\n{Path}";

    [RelayCommand]
    private void Add() => owner.AddToBoard?.Invoke(Source);

    [RelayCommand]
    private void Locate() => owner.ShowInFolder?.Invoke(Source.FilePath);
}
