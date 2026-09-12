using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowsCut.App.Formatting;
using MeowsCut.App.Playback;
using MeowsCut.App.Services;
using MeowsCut.App.Timeline;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Editing;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Предпросмотр: воспроизведение собранной последовательности.
/// </summary>
/// <remarks>
/// Во время воспроизведения кадры даёт системный проигрыватель, при перемотке —
/// он же в режиме паузы. Если файл системе не по зубам, показываются кадры,
/// извлечённые ffmpeg: лучше медленный предпросмотр, чем чёрный прямоугольник.
/// </remarks>
public sealed partial class PreviewViewModel : ObservableObject
{
    private const int FrameWidth = 720;
    private static readonly TimeSpan FrameDebounce = TimeSpan.FromMilliseconds(80);

    private readonly IThumbnailService _thumbnails;
    private readonly ThumbnailImageCache _cache;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimelineViewModel _timeline;
    private readonly MediaElementPlayer _player;
    private readonly SequencePlaybackController _playback;
    private readonly AudioMixPreview _audio = new();
    private readonly DispatcherTimer _timer;

    private CancellationTokenSource? _cancellation;
    private Project? _project;
    private bool _syncingPlayhead;

    public PreviewViewModel(
        IThumbnailService thumbnails,
        ThumbnailImageCache cache,
        IUiDispatcher dispatcher,
        TimelineViewModel timeline)
    {
        _thumbnails = thumbnails;
        _cache = cache;
        _dispatcher = dispatcher;
        _timeline = timeline;

        _player = new MediaElementPlayer();
        _playback = new SequencePlaybackController(_player);

        // Шаг воспроизведения дёргается таймером интерфейса: сам контроллер
        // остаётся проверяемым без диспетчера и реального времени.
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };

        _timer.Tick += (_, _) =>
        {
            _playback.Tick();
            _audio.Sync(_playback.Position, _playback.IsPlaying, seeked: false);
        };

        _playback.PositionChanged += OnPlaybackPositionChanged;
        _playback.StillImageChanged += (_, path) => _dispatcher.Post(() => ShowStillImage(path));
        _playback.BlackScreenChanged += (_, black) => _dispatcher.Post(() => IsBlackScreen = black);
        _playback.StateChanged += (_, _) => _dispatcher.Post(RefreshPlaybackState);

        _timeline.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TimelineViewModel.Playhead))
            {
                OnPlayheadMoved();
            }
        };

        _timeline.SequenceChanged += (_, sequence) =>
        {
            _playback.UpdateSequence(sequence);
            _audio.UpdateSequence(sequence);
            _audio.Sync(_playback.Position, _playback.IsPlaying, seeked: true);

            RefreshTexts();
            RefreshFormat();
            RequestFrame();
        };
    }

    /// <summary>Пропорции кадра ролика: 1.78 — горизонтальный, 0.5625 — вертикальный.</summary>
    [ObservableProperty]
    private double _frameAspect = 16d / 9d;

    /// <summary>
    /// Заполнять кадр с обрезкой вместо полей по краям.
    /// </summary>
    /// <remarks>
    /// Главный вопрос вертикального формата: горизонтальное видео либо стоит
    /// полосой посреди чёрного экрана, либо заполняет кадр, теряя края. Верного
    /// ответа нет — он зависит от того, что в кадре, поэтому выбор отдан
    /// пользователю и виден сразу в предпросмотре.
    /// </remarks>
    [ObservableProperty]
    private bool _fillFrame;

    /// <summary>Формат ролика: пропорции кадра, в который складывается монтаж.</summary>
    public IReadOnlyList<SequenceFormatOption> FormatOptions { get; } = SequenceFormatOption.All;

    [ObservableProperty]
    private SequenceFormatOption _selectedFormat = SequenceFormatOption.All[0];

    /// <summary>Размер кадра словами: его спрашивают ровно тогда, когда меняют формат.</summary>
    [ObservableProperty]
    private string _frameSizeText = string.Empty;

    partial void OnSelectedFormatChanged(SequenceFormatOption value) => ApplyFormat();

    partial void OnFillFrameChanged(bool value) => ApplyFormat();

    /// <summary>
    /// Складывает выбор пользователя в формат последовательности и отдаёт его истории.
    /// </summary>
    private void ApplyFormat()
    {
        if (_updatingFormat || !_timeline.HasProject)
        {
            return;
        }

        var current = _timeline.Sequence.Format;
        var fit = FillFrame ? Core.Export.FitMode.Cover : Core.Export.FitMode.Contain;

        var format = SelectedFormat.Aspect is { } aspect
            ? current.WithAspect(aspect) with { Fit = fit }

            // «Как в исходнике» возвращает кадр первого файла: заново считать его
            // неоткуда, но пропорции исходника у проекта уже есть.
            : OriginalFormat() with { Fit = fit, IsCustom = false };

        if (format == current)
        {
            return;
        }

        _timeline.SetSequenceFormat(format);
    }

    private Core.Editing.Timeline.SequenceFormat OriginalFormat()
    {
        var sequence = _timeline.Sequence;
        var first = sequence.Video.Clips.Count > 0 && _project is not null
            ? _project.Find(sequence.Video.Clips[0].SourceId)
            : null;

        return first is null
            ? sequence.Format
            : Core.Editing.Timeline.SequenceFormat.FromMedia(first.Info) with
            {
                FrameRate = sequence.Format.FrameRate
            };
    }

    /// <summary>Приводит переключатели к тому, что стоит в последовательности.</summary>
    private void RefreshFormat()
    {
        var format = _timeline.Sequence.Format;

        _updatingFormat = true;

        FrameAspect = format.AspectRatio;
        FillFrame = format.Fit == Core.Export.FitMode.Cover;
        FrameSizeText = $"{format.Size.Width}×{format.Size.Height}";
        SelectedFormat = SequenceFormatOption.For(format);

        _updatingFormat = false;
    }

    private bool _updatingFormat;

    /// <summary>
    /// Надписи, попадающие в текущий кадр.
    /// </summary>
    /// <remarks>
    /// Рисуются поверх предпросмотра средствами WPF — приблизительно, но
    /// достаточно, чтобы выбрать место и размер. Иначе надпись впервые
    /// показалась бы только в готовом файле.
    /// </remarks>
    public System.Collections.ObjectModel.ObservableCollection<PreviewTitle> VisibleTitles { get; } = [];

    private void RefreshTitles()
    {
        var playhead = _timeline.Playhead;
        var titles = _timeline.Sequence.Titles;

        VisibleTitles.Clear();

        foreach (var title in titles)
        {
            if (playhead >= title.TimelineStart && playhead < title.TimelineEnd)
            {
                VisibleTitles.Add(PreviewTitle.From(title));
            }
        }
    }

    /// <summary>Визуальный элемент проигрывателя для размещения в разметке.</summary>
    public FrameworkElement PlayerVisual => _player.Visual;

    [ObservableProperty]
    private BitmapSource? _frame;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlayerVisible))]
    [NotifyPropertyChangedFor(nameof(IsFrameVisible))]
    private bool _useFallbackFrames;

    /// <summary>
    /// Под курсором фотография. Показываем её саму: системный проигрыватель
    /// картинку не откроет, а его ошибка увела бы весь предпросмотр в запасной режим.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlayerVisible))]
    [NotifyPropertyChangedFor(nameof(IsFrameVisible))]
    private bool _isStillImage;

    /// <summary>
    /// Под курсором пустое место: зазор между клипами или хвост, где звук уже идёт,
    /// а картинки ещё нет. Показываем чёрный экран — ровно то, что будет в файле.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlayerVisible))]
    [NotifyPropertyChangedFor(nameof(IsFrameVisible))]
    private bool _isBlackScreen;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private string _positionText = "00:00.00";

    [ObservableProperty]
    private string _durationText = "00:00.00";

    /// <summary>
    /// Кадрирование клипа под курсором. Показывается в предпросмотре приблизительно:
    /// точный результат даёт только экспорт, но выбрать, какая часть кадра останется,
    /// без этого было нельзя вовсе.
    /// </summary>
    [ObservableProperty]
    private double _frameZoom = 1d;

    [ObservableProperty]
    private double _frameOffsetX;

    [ObservableProperty]
    private double _frameOffsetY;

    [ObservableProperty]
    private int _frameRotation;

    /// <summary>
    /// Насколько кадр сейчас затемнён: 0 — картинка видна, 1 — чёрный экран.
    /// </summary>
    /// <remarks>
    /// Без этого затухание нельзя было бы увидеть до экспорта: пользователь
    /// ставит секунду затемнения, прокручивает к концу клипа и видит обычный кадр —
    /// ровно то ощущение «настройка не работает», ради которого поле и добавляли.
    /// </remarks>
    [ObservableProperty]
    private double _fadeDim;

    public bool IsPlayerVisible => !UseFallbackFrames && !IsStillImage && !IsBlackScreen;

    /// <summary>Картинку показывают и запасной режим, и фотография в видеоряду.</summary>
    public bool IsFrameVisible => (UseFallbackFrames || IsStillImage) && !IsBlackScreen;

    public void Attach(Project project)
    {
        _project = project;
        _playback.Attach(project);
        _audio.Attach(project);
        _timer.Start();

        RefreshTexts();
        RefreshFormat();
        RequestFrame();
    }

    /// <summary>В проект добавился файл — плееру нужен обновлённый список источников.</summary>
    public void UpdateProject(Project project)
    {
        _project = project;
        _playback.UpdateProject(project);
        _audio.UpdateProject(project);
    }

    public void Detach()
    {
        _timer.Stop();
        _cancellation?.Cancel();
        _playback.Detach();
        _audio.Detach();

        _project = null;
        Frame = null;
        IsStillImage = false;
        IsBlackScreen = false;
        UseFallbackFrames = false;
        IsPlaying = false;
        PositionText = DurationText = "00:00.00";
    }

    [RelayCommand]
    private void TogglePlay()
    {
        if (UseFallbackFrames)
        {
            return;
        }

        _playback.TogglePlay();
        _audio.Sync(_playback.Position, _playback.IsPlaying, seeked: true);
        RefreshPlaybackState();
    }

    [RelayCommand]
    private void Step(double seconds)
    {
        _playback.Pause();
        _audio.Sync(_playback.Position, playing: false, seeked: true);

        var target = _timeline.Playhead + TimeSpan.FromSeconds(seconds);
        _timeline.Playhead = target < TimeSpan.Zero
            ? TimeSpan.Zero
            : target > _timeline.Duration ? _timeline.Duration : target;
    }

    [RelayCommand]
    private void GoToStart() => _timeline.Playhead = TimeSpan.Zero;

    [RelayCommand]
    private void GoToEnd() => _timeline.Playhead = _timeline.Duration;

    /// <summary>Плейхед двигал пользователь — перематываем проигрыватель.</summary>
    private void OnPlayheadMoved()
    {
        RefreshTexts();

        if (_syncingPlayhead)
        {
            return;
        }

        _playback.Seek(_timeline.Playhead);
        _audio.Sync(_timeline.Playhead, _playback.IsPlaying, seeked: true);
        RequestFrame();
    }

    /// <summary>Позицию сдвинуло воспроизведение — двигаем плейхед, не зациклившись.</summary>
    private void OnPlaybackPositionChanged(object? sender, TimeSpan position) =>
        _dispatcher.Post(() =>
        {
            _syncingPlayhead = true;
            _timeline.Playhead = position;
            _syncingPlayhead = false;

            RefreshTexts();
        });

    /// <summary>Показывает фотографию из видеоряда или возвращает кадр проигрывателю.</summary>
    private void ShowStillImage(string? path)
    {
        if (path is null)
        {
            IsStillImage = false;
            return;
        }

        IsStillImage = true;
        Frame = _cache.Load(path);
    }

    private void RefreshPlaybackState()
    {
        IsPlaying = _playback.IsPlaying;
        UseFallbackFrames = _playback.IsPlayerUnavailable;

        if (UseFallbackFrames)
        {
            RequestFrame();
        }
    }

    private void RefreshTexts()
    {
        PositionText = DisplayFormat.Duration(_timeline.Playhead);
        DurationText = DisplayFormat.Duration(_timeline.Duration);
        RefreshFraming();
        RefreshTitles();
    }

    /// <summary>Кадрирование берётся у клипа под курсором и меняется вместе с ним.</summary>
    private void RefreshFraming()
    {
        var placed = _timeline.Sequence.ClipAt(_timeline.Playhead);
        var transform = placed?.Clip.Transform ?? Core.Editing.Timeline.ClipTransform.Identity;

        FrameZoom = transform.Zoom;
        FrameOffsetX = transform.OffsetX;
        FrameOffsetY = transform.OffsetY;
        FrameRotation = transform.Rotation;
        FadeDim = placed is { } clip ? DimAt(clip, _timeline.Playhead) : 0d;
    }

    /// <summary>
    /// Затемнение кадра в точке таймлайна: на краях затухания — полный чёрный,
    /// дальше линейно к нулю. Так же считает и фильтр fade при экспорте.
    /// </summary>
    private static double DimAt(Core.Editing.Timeline.PlacedClip placed, TimeSpan playhead)
    {
        var offset = playhead - placed.Start;
        var clip = placed.Clip;

        var fadeIn = clip.EffectiveFadeIn;
        if (fadeIn > TimeSpan.Zero && offset < fadeIn)
        {
            return 1d - offset / fadeIn;
        }

        var fadeOut = clip.EffectiveFadeOut;
        if (fadeOut > TimeSpan.Zero)
        {
            var fromEnd = clip.TimelineDuration - offset;
            if (fromEnd < fadeOut)
            {
                return 1d - Math.Max(fromEnd / fadeOut, 0d);
            }
        }

        return 0d;
    }

    /// <summary>
    /// Кадр из ffmpeg. Нужен, когда системный проигрыватель не открыл файл;
    /// в обычном режиме кадр показывает он сам.
    /// </summary>
    private void RequestFrame()
    {
        if (!UseFallbackFrames)
        {
            return;
        }

        _cancellation?.Cancel();
        _cancellation = new CancellationTokenSource();

        _ = RefreshFrameAsync(_timeline.Playhead, _cancellation.Token);
    }

    private async Task RefreshFrameAsync(TimeSpan playhead, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(FrameDebounce, cancellationToken).ConfigureAwait(false);

            if (_project is null)
            {
                return;
            }

            var lookup = _timeline.Sequence.Resolve(playhead)
                         ?? _timeline.Sequence.Resolve(playhead - TimeSpan.FromMilliseconds(40));

            if (lookup is not { } resolved || _project.Find(resolved.Clip.SourceId) is not { } source)
            {
                return;
            }

            var path = await _thumbnails
                .GetFrameAsync(source.FilePath, resolved.SourceTime, FrameWidth, cancellationToken)
                .ConfigureAwait(false);

            if (path is null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var image = _cache.Load(path);
            if (image is not null)
            {
                _dispatcher.Post(() => Frame = image);
            }
        }
        catch (OperationCanceledException)
        {
            // Пользователь продолжил двигать плейхед — этот кадр уже не нужен.
        }
    }
}
