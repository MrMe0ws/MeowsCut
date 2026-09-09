# 02. Модели данных и контракты ядра

Ниже — сигнатуры-намерения (что за тип, какие поля, зачем). Спецификация, не готовый код.
Модели ядра — `record` / `record struct`, **иммутабельные**, изменения через `with` и команды
редактирования. Мутабельны только ViewModels.

## 2.1 Медиа-информация (результат ffprobe)

```csharp
sealed record MediaInfo(
    string FilePath,
    long   FileSizeBytes,
    TimeSpan Duration,
    ContainerInfo Container,
    IReadOnlyList<VideoStreamInfo> VideoStreams,
    IReadOnlyList<AudioStreamInfo> AudioStreams,
    IReadOnlyList<SubtitleStreamInfo> SubtitleStreams)
{
    VideoStreamInfo? PrimaryVideo { get; }   // первый видеопоток, не attached_pic
    AudioStreamInfo? PrimaryAudio { get; }
    bool HasAudio { get; }
}

sealed record ContainerInfo(string FormatName, string FormatLongName, long? BitrateBps);

sealed record VideoStreamInfo(
    int Index, string CodecName, string? Profile, string PixelFormat,
    FrameSize Size, Rational FrameRate, Rational SampleAspectRatio,
    long? BitrateBps, int? RotationDegrees, bool HasAlpha, bool IsVariableFrameRate,
    TimeSpan Duration);

sealed record AudioStreamInfo(
    int Index, string CodecName, int Channels, string ChannelLayout,
    int SampleRateHz, long? BitrateBps, TimeSpan Duration, string? Language);
```

Вспомогательные значения:

```csharp
readonly record struct FrameSize(int Width, int Height)
{
    double AspectRatio { get; }
    FrameSize ScaledToFit(int maxW, int maxH);
    FrameSize RoundedToEven();                   // требование yuv420p
}

readonly record struct Rational(int Numerator, int Denominator)   // 30000/1001
{
    double Value { get; }
    static Rational Parse(string ffprobeValue);
}

readonly record struct TimeRange(TimeSpan Start, TimeSpan End)
{
    TimeSpan Duration { get; }
    bool Contains(TimeSpan t);
    bool Overlaps(TimeRange other);
    TimeRange Clamp(TimeSpan mediaDuration);
}
```

`RotationDegrees` берётся из side-data / тега `rotate` — без него вертикальные видео с телефона
уезжают набок. `IsVariableFrameRate` (расхождение `avg_frame_rate` и `r_frame_rate`) — повод
предупредить, что stream-copy нарезка даст рассинхрон.

## 2.2 Проект и последовательность

**Ядро приложения — таймлайн-последовательность клипов**, а не «один файл + список интервалов».
Это позволяет резать, переставлять и настраивать каждый кусок отдельно.

```csharp
sealed record Project(
    IReadOnlyList<MediaSource> Sources,   // добавленные файлы (bin)
    Sequence Sequence,
    ExportSettings Export)
{
    MediaSource GetSource(SourceId id);
}

sealed record MediaSource(SourceId Id, MediaInfo Info, string DisplayName);
readonly record struct SourceId(Guid Value);
readonly record struct ClipId(Guid Value);
```

### Последовательность и дорожка

```csharp
sealed record Sequence(VideoTrack Video, SequenceFormat Format)
{
    TimeSpan Duration { get; }                       // сумма длительностей клипов
    (Clip Clip, TimeSpan SourceTime)? Resolve(TimeSpan timelineTime);   // для плеера и рендера
    TimeSpan StartOf(ClipId id);
}

sealed record SequenceFormat(FrameSize Size, Rational FrameRate)
{
    static SequenceFormat FromFirstClip(MediaInfo info);   // формат задаётся первым клипом
}

sealed record VideoTrack(IReadOnlyList<Clip> Clips)        // упорядочены, встык, без зазоров
{
    int IndexOf(ClipId id);
    VideoTrack Replace(ClipId id, Clip clip);
    VideoTrack Insert(int index, Clip clip);
    VideoTrack RemoveAt(int index);
    VideoTrack Move(int from, int to);
}
```

**Решение по зазорам:** в v1 клипы лежат встык, позиция клипа вычисляется из порядка.
Это модель «нарезал — склеил», ровно то, для чего нужен этот редактор; она исключает целый
класс состояний «чёрная дыра посреди видео». Абсолютные позиции и зазоры добавляются позже
(поле `StartOnTimeline` появится в `Clip`, `Resolve` уже инкапсулирует эту логику).

### Клип — единица монтажа

```csharp
sealed record Clip(
    ClipId Id,
    SourceId SourceId,
    TimeRange SourceRange,        // in/out внутри исходника
    double Speed,                 // 0.1 .. 16.0
    ClipAudio Audio,
    string? Label)
{
    TimeSpan TimelineDuration => SourceRange.Duration / Speed;
    TimeSpan ToSourceTime(TimeSpan offsetInClip) => SourceRange.Start + offsetInClip * Speed;
    Clip WithSpeed(double speed);
    Clip TrimStart(TimeSpan delta);       // с учётом границ исходника
    Clip TrimEnd(TimeSpan delta);
    (Clip left, Clip right) SplitAt(TimeSpan offsetInClip);   // ножницы
}

sealed record ClipAudio(bool Enabled, double VolumeMultiplier);   // 1.0 = без изменений
```

Аудио в v1 жёстко связано с видеоклипом (как связанные A/V в Premiere): режется вместе,
скорость общая, отдельно регулируются только громкость и вкл/выкл. Отдельная аудиодорожка —
следующая по очереди возможность, она добавляется как второй трек в `Sequence`, не ломая клипы.

**Trim и Cut выражаются через операции над клипами**, отдельной сущности для них нет:

| Действие пользователя | Что происходит с моделью |
|---|---|
| Обрезать начало/конец | `TrimStart` / `TrimEnd` крайнего клипа |
| Ножницы в точке | `SplitAt` → два клипа |
| Вырезать фрагмент | два `SplitAt` + удаление среднего клипа с подтягиванием |
| Переставить кусок | `VideoTrack.Move` |
| Ускорить кусок | `WithSpeed` конкретного клипа |

## 2.3 Команды редактирования и история

Доска монтажа без `Ctrl+Z` не работает. Все изменения последовательности идут **только**
через команды — это единственный способ гарантировать корректный undo.

```csharp
interface IEditCommand
{
    string Title { get; }                       // «Разрезать клип» — текст для истории
    Sequence Apply(Sequence sequence);
    bool TryMergeWith(IEditCommand previous, out IEditCommand merged);  // склейка drag-серий
}

sealed class EditHistory
{
    Sequence Current { get; }
    bool CanUndo { get; } bool CanRedo { get; }
    void Execute(IEditCommand command);
    void Undo(); void Redo();
    event EventHandler<SequenceChangedEventArgs>? Changed;
}
```

Реализация undo — **снимками `Sequence`**, а не обратными операциями: последовательность это
несколько десятков иммутабельных записей (килобайты), кадры в ней не хранятся. Обратные
операции для двенадцати типов команд — источник трудноуловимых багов; снимок не ошибается.
Глубина истории — 100 шагов, ограничение по памяти не требуется.

`TryMergeWith` нужен, чтобы перетаскивание края клипа не создавало 200 записей в истории:
серия однотипных команд по одному клипу схлопывается в одну.

Команды v1: `SplitClipCommand`, `RemoveClipCommand` (ripple), `TrimClipEdgeCommand`,
`MoveClipCommand`, `SetClipSpeedCommand`, `SetClipAudioCommand`, `AppendClipCommand`,
`RemoveRangeCommand` (вырезать выделенный интервал), `DuplicateClipCommand`.

## 2.4 Настройки экспорта

```csharp
sealed record ExportSettings(
    ContainerFormat Container,
    VideoSettings Video,
    AudioSettings Audio,
    string OutputPath,
    OverwritePolicy Overwrite);      // Ask | AutoRename | Overwrite

sealed record VideoSettings(
    VideoCodec Codec,
    RateControl RateControl,
    EncodingSpeed Preset,            // маппится на -preset / -cpu-used
    ResolutionSpec Resolution,
    FrameRateSpec FrameRate,
    AdvancedVideoSettings Advanced);

sealed record AdvancedVideoSettings(
    string? PixelFormat,             // yuv420p / yuv420p10le / yuva420p
    string? Profile, string? Level,
    int? KeyframeIntervalFrames,     // -g
    bool TwoPass,
    string? Tune,
    IReadOnlyDictionary<string, string>? RawOptions);   // «я знаю, что делаю»

sealed record AudioSettings(
    bool Enabled, AudioCodec Codec, int BitrateKbps,
    int? SampleRateHz, int? Channels, double MasterVolumeMultiplier);
```

`AdvancedVideoSettings` — то, что скрыто за переключателем «Расширенные» в панели экспорта.
Отдельная запись, чтобы базовый экран не тащил десяток полей, а расширенный не требовал
отдельной модели.

### Управление битрейтом

```csharp
abstract record RateControl
{
    sealed record Auto() : RateControl;                    // эвристика по разрешению/FPS/кодеку
    sealed record ConstantBitrate(int Kbps) : RateControl; // 500/1000/2000/4000/6000/8000/custom
    sealed record ConstantQuality(int Crf) : RateControl;  // CRF/CQ, диапазон зависит от кодека
    sealed record TargetSize(long Bytes) : RateControl;    // два прохода (стикеры, лимиты)
}
```

`RateControlPolicy` (Core) знает: доступен ли CRF для кодека, каков диапазон и дефолт
(x264 → 23, x265 → 28, VP9 → 31, SVT-AV1 → 35), во что разворачивается `Auto`.
Одно место вместо констант, размазанных по UI.

### Разрешение и FPS

```csharp
abstract record ResolutionSpec
{
    sealed record Original() : ResolutionSpec;                  // формат последовательности
    sealed record Preset(int TargetHeight) : ResolutionSpec;    // 2160/1080/720/480/360
    sealed record Custom(int Width, int Height, bool KeepAspect, FitMode Fit) : ResolutionSpec;
    // FitMode: Contain (letterbox) | Cover (crop) | Stretch
    FrameSize Resolve(FrameSize sequenceSize);                  // всегда чётные значения
}

abstract record FrameRateSpec
{
    sealed record Original() : FrameRateSpec;
    sealed record Fixed(double Fps) : FrameRateSpec;            // 24/25/30/50/60/custom
    double? Resolve(Rational sequenceFps);
}
```

Апскейл и подъём FPS разрешены (иногда нужны под требования площадки), но помечаются
предупреждением в сводке — качества не добавят, вес добавят.

### Форматы и кодеки

```csharp
enum ContainerFormat { Mp4, WebM, Mov, Mkv, Avi }
enum VideoCodec      { H264, H265, Vp9, Av1, CopySource }
enum AudioCodec      { Aac, Opus, Mp3, Vorbis, Flac, CopySource, None }
```

`CompatibilityMatrix` — таблица «контейнер × кодек»:

| Контейнер | Видео | Аудио | Заметка |
|-----------|-------|-------|---------|
| MP4 | H264, H265, AV1 | AAC, MP3 | `+faststart` обязателен |
| WebM | VP9, AV1 | Opus, Vorbis | H.264/AAC недопустимы |
| MOV | H264, H265 | AAC | H.265 требует тега `hvc1` |
| MKV | все | все | самый терпимый |
| AVI | H264 | MP3 | легаси, только явным выбором |

Матрица используется трижды: UI гасит несовместимое, валидатор блокирует экспорт,
`PresetApplier` подбирает замену вместо тихой поломки.

## 2.5 Запрос и план экспорта

```csharp
sealed record ExportRequest(Project Project, ExportSettings Settings, string JobTitle);

sealed record ExportPlan(
    IReadOnlyList<ExportStage> Stages,
    TimeSpan ExpectedOutputDuration,
    ExportSummary Summary,
    IReadOnlyList<PlanWarning> Warnings);

sealed record ExportStage(
    StageKind Kind,                  // Analyze | ClipExtract | Concat | Transcode | PassOne | PassTwo | Finalize
    string DisplayName,
    IReadOnlyList<string> Arguments, // заполняет слой Ffmpeg
    double Weight,
    TimeSpan? ExpectedDuration,
    string? OutputTempFile);

sealed record ExportSummary(
    FrameSize Resolution, double Fps, string VideoCodecLabel, string VideoBitrateLabel,
    string AudioLabel, TimeSpan Duration, long? EstimatedSizeBytes, int ClipCount, bool IsLossless);
```

`ExportSummary` — ровно то, что показывается перед экспортом. Считает планировщик, не UI,
поэтому сводка гарантированно описывает то, что реально запустится.

## 2.6 Основные контракты

```csharp
interface IMediaProbe
{
    Task<MediaInfo> ProbeAsync(string filePath, CancellationToken ct);
}

interface IExportPlanner
{
    ExportPlan CreatePlan(ExportRequest request, MediaCapabilities capabilities);
}

interface IExportEngine
{
    Task<JobResult> ExecuteAsync(ExportPlan plan, string outputPath,
                                 IProgress<JobProgress> progress, CancellationToken ct);
}

interface IThumbnailService
{
    Task<string> GetFrameAsync(SourceId source, TimeSpan position, int width, CancellationToken ct);
    IAsyncEnumerable<ThumbnailTile> GetStripAsync(SourceId source, TimeRange range, int count,
                                                  int width, CancellationToken ct);
}

interface IWaveformService                       // полоса громкости под клипом
{
    Task<WaveformPeaks> GetPeaksAsync(SourceId source, int bucketsPerSecond, CancellationToken ct);
}

interface IMediaToolsetLocator
{
    Task<ToolsetLocationResult> LocateAsync(CancellationToken ct);
}
```

## 2.7 Оценка размера файла

`OutputSizeEstimator` (Core, чистая функция):

```
estimatedBytes ≈ (videoBitrateBps + audioBitrateBps) / 8 * outputDurationSeconds * 1.02
```

Для `ConstantQuality` точной оценки нет — показываем диапазон от исходного битрейта и
пометку «зависит от содержимого». Врать конкретным числом при CRF нельзя.
