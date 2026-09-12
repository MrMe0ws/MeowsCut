using CommunityToolkit.Mvvm.ComponentModel;
using MeowsCut.App.Formatting;
using MeowsCut.App.Timeline;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Клип в том виде, в каком его рисует таймлайн: положение в пикселях, подпись, кадры.
/// </summary>
/// <remarks>
/// Тонкая проекция модели, а не её копия. Пересоздаётся при каждом изменении
/// последовательности, но идентичность сохраняется по <see cref="Id"/> —
/// иначе выделение слетало бы после любой правки.
/// </remarks>
public sealed partial class ClipViewModel : ObservableObject
{
    public ClipViewModel(PlacedClip placed, MediaSource? source)
    {
        Id = placed.Clip.Id;
        Update(placed, source);
    }

    public ClipId Id { get; }

    public Clip Clip { get; private set; } = null!;

    public TimeSpan Start { get; private set; }

    public TimeSpan End => Start + Clip.TimelineDuration;

    public string Title { get; private set; } = string.Empty;

    public string DurationText { get; private set; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Полоса кадров. Заполняется лениво и может быть пустой.</summary>
    public IReadOnlyList<ClipThumbnail> Thumbnails { get; set; } = [];

    public string? SourcePath { get; private set; }

    public void Update(PlacedClip placed, MediaSource? source)
    {
        Clip = placed.Clip;
        Start = placed.Start;
        SourcePath = source?.FilePath;
        Title = source?.DisplayName ?? "клип";
        DurationText = DisplayFormat.Duration(placed.Clip.TimelineDuration);
    }

    /// <summary>Фотография: кадр у неё один, и полоса заполняется им целиком.</summary>
    public bool IsImage => Clip.SourceIsImage;

    /// <summary>
    /// Время внутри исходника для точки таймлайна — нужно для кадров полосы.
    /// У фотографии кадр всегда один: искать в ней десятую секунду бессмысленно,
    /// а ffmpeg на такую перемотку просто ничего не вернёт.
    /// </summary>
    public TimeSpan SourceTimeAt(TimeSpan timelineTime) =>
        IsImage ? TimeSpan.Zero : Clip.ToSourceTime(timelineTime - Start);
}
