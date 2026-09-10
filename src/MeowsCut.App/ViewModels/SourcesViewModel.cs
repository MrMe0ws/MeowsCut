using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowsCut.App.Formatting;
using MeowsCut.Core.Editing;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Файлы, добавленные в проект.
/// </summary>
/// <remarks>
/// Список нужен по двум причинам. Первая — вспомнить, что вообще приложено:
/// после десятка перетаскиваний это уже не очевидно. Вторая — положить файл
/// на доску ещё раз, не открывая диалог: один и тот же файл режут на несколько
/// кусков чаще, чем добавляют новый.
/// </remarks>
public sealed partial class SourcesViewModel : ObservableObject
{
    public ObservableCollection<ProjectFileViewModel> Files { get; } = [];

    public bool HasFiles => Files.Count > 0;

    /// <summary>Кладёт файл на доску: видео и фото — в видеоряд, звук — на дорожку.</summary>
    public Action<MediaSource>? AddToBoard { get; set; }

    public Action<string>? ShowInFolder { get; set; }

    public void Update(Project? project)
    {
        Files.Clear();

        foreach (var source in project?.Sources ?? [])
        {
            Files.Add(new ProjectFileViewModel(source, this));
        }

        OnPropertyChanged(nameof(HasFiles));
    }
}

/// <summary>Один файл в списке проекта.</summary>
public sealed partial class ProjectFileViewModel(MediaSource source, SourcesViewModel owner) : ObservableObject
{
    public MediaSource Source { get; } = source;

    public string Title => Source.DisplayName;

    public string Path => Source.FilePath;

    /// <summary>Чем этот файл будет на доске. От этого зависит и место, куда он ляжет.</summary>
    public string Kind => Source.IsImage
        ? Localization.Strings.SourceKindImage
        : Source.Info.HasVideo
            ? Localization.Strings.SourceKindVideo
            : Localization.Strings.SourceKindAudio;

    /// <summary>
    /// Короткая справка под именем: у видео размер кадра и длина, у фотографии —
    /// только размер (длины у неё нет), у звука — длина.
    /// </summary>
    public string Details
    {
        get
        {
            var video = Source.Info.PrimaryVideo;

            if (Source.IsImage)
            {
                return video is null ? string.Empty : $"{video.Size.Width}×{video.Size.Height}";
            }

            var duration = DisplayFormat.Duration(Source.Duration);

            return video is null
                ? duration
                : $"{video.Size.Width}×{video.Size.Height} · {duration}";
        }
    }

    [RelayCommand]
    private void Add() => owner.AddToBoard?.Invoke(Source);

    [RelayCommand]
    private void Locate() => owner.ShowInFolder?.Invoke(Source.FilePath);
}
