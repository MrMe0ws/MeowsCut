# 01. Архитектура

## Стек

| Что | Решение |
|-----|---------|
| Платформа | .NET 8 (LTS): `net8.0-windows` для UI, `net8.0` для остального |
| UI | WPF + MVVM (CommunityToolkit.Mvvm, source generators) |
| DI / хостинг | `Microsoft.Extensions.Hosting` (Generic Host внутри `App.xaml.cs`) |
| Логирование | `Microsoft.Extensions.Logging` + Serilog (rolling file + debug sink) |
| Обработка видео | FFmpeg (CLI-процесс), FFprobe (JSON-вывод) |
| Тесты | xUnit + FluentAssertions + NSubstitute |
| Сериализация | `System.Text.Json` (пресеты, настройки, разбор ffprobe) |

Готовые .NET-обёртки над FFmpeg (Xabe.FFmpeg, FFMpegCore) **не используем**: нужен полный
контроль над аргументами, потоком прогресса и корректным убийством дерева процессов.

## Слои и правило зависимостей

```
        ┌───────────────────────────────┐
        │        MeowsCut.App           │  WPF: Views, ViewModels, Controls
        │  (net8.0-windows, WPF, MVVM)  │  знает только абстракции Core
        └───────────────┬───────────────┘
                        │ ссылается на Core — везде
                        │ ссылается на Ffmpeg — ТОЛЬКО в Composition/ (регистрация DI)
        ┌───────────────▼───────────────┐
        │       MeowsCut.Ffmpeg         │  реализация абстракций через процессы:
        │        (net8.0)               │  аргументы, парсинг, прогресс, temp
        └───────────────┬───────────────┘
                        │
        ┌───────────────▼───────────────┐
        │        MeowsCut.Core          │  модели, enum-ы, интерфейсы, пресеты,
        │   (net8.0, без зависимостей)  │  валидация, планирование, ошибки
        └───────────────────────────────┘
```

Правила, которые нельзя нарушать:

1. `Core` не ссылается ни на что: ни WPF, ни `System.Diagnostics.Process`, ни на FFmpeg.
2. `Ffmpeg` не знает про UI и не имеет ссылок на WPF-типы.
3. `App` создаёт конкретные реализации **только** в `Composition/`; весь остальной код
   работает через интерфейсы (`IMediaProbe`, `IExportEngine`, `IJobQueue`, ...).
4. View (`*.xaml.cs`) не содержит логики, кроме визуальных мелочей: никаких вызовов сервисов.

## Структура решения

Дерево ниже — то, что лежит в репозитории сейчас. Типы сгруппированы по файлам:
мелкие записи одной темы живут вместе (`Formats.cs` — контейнеры и кодеки,
`JobContracts.cs` — очередь), а не каждая в своём файле. Правило «один файл —
один тип» из `CLAUDE.md` про крупные классы, а не про пачку `record`-ов в три строки.

```
MeowsCut/
├─ MeowsCut.sln
├─ Directory.Build.props          # общие свойства: nullable, LangVersion, анализаторы
├─ global.json                    # линейка SDK 8.0 (без привязки к feature band)
├─ CLAUDE.md                      # правила проекта для агента
├─ docs/                          # эта документация
├─ tools/                         # get-ffmpeg.ps1, publish-portable.ps1,
│                                 # build-installer.ps1, installer/
├─ src/
│  ├─ MeowsCut.Core/
│  ├─ MeowsCut.Ffmpeg/
│  └─ MeowsCut.App/
└─ tests/
   ├─ MeowsCut.Core.Tests/
   ├─ MeowsCut.Ffmpeg.Tests/      # есть интеграционные на настоящем ffmpeg
   └─ MeowsCut.App.Tests/
```

### src/MeowsCut.Core

```
MeowsCut.Core/
├─ Media/                 # что прочитали из файла
│   ├─ MediaInfo.cs, MediaStreams.cs (Video/AudioStreamInfo, ContainerInfo, SubtitleStreamInfo)
│   ├─ Rational.cs, TimeRange.cs, FrameSize.cs
│   ├─ MediaFileTypes.cs             # какие расширения считаем видео, звуком, картинкой
│   └─ MediaCapabilities.cs          # какие энкодеры доступны в текущем ffmpeg
├─ Editing/               # доска монтажа
│   ├─ Project.cs, MediaSource.cs, Identifiers.cs (SourceId, ClipId, AudioClipId, TrackId)
│   ├─ SilenceTrimmer.cs              # вырезание пауз по найденной тишине (ADR-36)
│   ├─ Timeline/  Sequence.cs (+ SequenceFormat, PlacedClip), VideoTrack.cs, Clip.cs,
│   │             ClipSettings.cs (ClipAudio, ClipTransform), AudioTrack.cs, AudioClip.cs,
│   │             TitleClip.cs (+ TitleId, TitleAnchor)
│   ├─ Commands/  IEditCommand.cs, SplitAndRemoveCommands.cs, TrimClipEdgeCommand.cs,
│   │             MoveClipInTimeCommand.cs, ArrangeClipCommands.cs, ClipPropertyCommands.cs,
│   │             AudioClipCommands.cs, AudioTrackCommands.cs, TitleCommands.cs,
│   │             ReplaceSequenceCommand.cs
│   └─ History/   EditHistory.cs      # единственный вход для изменения последовательности
├─ Export/                # во что рендерим
│   ├─ ExportSettings.cs, OutputSpecs.cs (ResolutionSpec, FrameRateSpec, AudioSettings)
│   ├─ Formats.cs (ContainerFormat, VideoCodec, AudioCodec), RateControl.cs
│   ├─ CompatibilityMatrix.cs        # какой кодек допустим в каком контейнере
│   ├─ HardwareAcceleration.cs, SubtitleSettings.cs
│   └─ ExportSummary.cs
├─ Presets/
│   ├─ PresetModels.cs, PresetContracts.cs, PresetResults.cs
│   ├─ JsonPresetProvider.cs, PresetApplier.cs, PresetValidator.cs
│   └─ Json/PresetJsonModels.cs      # лимиты площадок живут в JSON, а не в коде
├─ Processing/            # план работ, без знания о ffmpeg
│   └─ ExportPlan.cs (ExportStage, StageKind, IExportPlanner)
├─ Projects/              # черновик .meows
│   ├─ IProjectStore.cs, JsonProjectStore.cs, AutosaveState.cs
│   └─ ProjectDocument.cs, ProjectDocumentReader.cs, ProjectDocumentWriter.cs
├─ Jobs/
│   ├─ JobContracts.cs (IJobQueue, JobDescriptor, JobProgress, JobResult, JobStatus)
│   ├─ JobQueue.cs, JobHandle.cs
├─ Abstractions/
│   ├─ IMediaProbe.cs, IExportEngine.cs, IThumbnailService.cs, IWaveformService.cs
│   ├─ ISilenceDetector.cs (+ SilenceOptions)
│   └─ IMediaToolset.cs (+ IMediaToolsetLocator, IMediaToolsetProvider, ITempWorkspace…)
├─ Diagnostics/
│   ├─ MeowsCutException.cs (+ наследники, AppError, ErrorCode)
│   └─ ErrorPresenter.cs
└─ Configuration/
    ├─ AppSettings.cs, IAppSettingsStore.cs, JsonAppSettingsStore.cs, AppPaths.cs
```

### src/MeowsCut.Ffmpeg

```
MeowsCut.Ffmpeg/
├─ Toolset/
│   ├─ FfmpegToolsetLocator.cs        # порядок поиска бинарников
│   ├─ MediaToolsetProvider.cs        # найденный набор, доступный остальным
│   └─ EncoderProbe.cs                # пробный кадр на каждый энкодер → MediaCapabilities
├─ Execution/
│   ├─ IProcessRunner.cs, ProcessRunner.cs
│   └─ ProcessContracts.cs            # запрос, результат, хвост stderr
├─ Arguments/
│   ├─ FfmpegArgumentBuilder.cs       # только ArgumentList, никакой конкатенации
│   ├─ FilterGraphBuilder.cs, SpeedFilter.cs, SubtitleFilter.cs, AudioMixBuilder.cs
│   ├─ TitleFilter.cs                 # drawtext: текст едет файлом (ADR-35)
│   ├─ EncoderCatalog.cs              # VideoCodec → энкодер + профиль/preset/pix_fmt
│   └─ HardwareEncoders.cs            # nvenc / qsv / amf
├─ Probing/
│   ├─ FfprobeMediaProbe.cs, MediaInfoMapper.cs, FfmpegSilenceDetector.cs
│   └─ Json/FfprobeJsonModels.cs
├─ Planning/
│   └─ FfmpegExportPlanner.cs         # ExportRequest → ExportPlan (стратегии — методы внутри)
├─ Rendering/
│   └─ FfmpegExportEngine.cs          # выполняет ExportPlan стадия за стадией
├─ Progress/
│   ├─ FfmpegProgressParser.cs        # -progress pipe:1 → key=value
│   └─ EtaEstimator.cs
├─ Temp/
│   ├─ TempWorkspace.cs (+ TempWorkspaceFactory), ConcatListWriter.cs, TitleTextStore.cs
└─ Thumbnails/
    ├─ FfmpegThumbnailService.cs, FfmpegWaveformService.cs
```

### src/MeowsCut.App

```
MeowsCut.App/
├─ App.xaml / App.xaml.cs             # Generic Host, DI, Serilog, глобальный обработчик
├─ Composition/ServiceRegistration.cs # AddMeowsCutCore / AddMeowsCutFfmpeg / AddMeowsCutUi
├─ Views/
│   ├─ ShellWindow.xaml               # строка меню вместо заголовка окна, доска, статус
│   ├─ Panes/  PreviewPaneView, TimelinePaneView, SourcesView, InspectorView,
│   │          AudioInspectorView, TitleInspectorView, PresetsView, ExportPanelView
│   └─ Dialogs/ ExportWindow, SettingsWindow, ErrorDialog, ConfirmDialog, SilenceWindow
├─ ViewModels/
│   ├─ ShellViewModel.cs + .Project (черновик) + .Silence (вырезание пауз)
│   ├─ TimelineViewModel.cs + .Audio / .Clipboard / .Pointer
│   ├─ ClipViewModel.cs, PreviewViewModel.cs, SourcesViewModel.cs
│   ├─ InspectorViewModel.cs, AudioInspectorViewModel.cs, TitleInspectorViewModel.cs
│   ├─ PreviewTitle.cs, MediaSummaryViewModel.cs
│   └─ ExportViewModel.cs, ExportOptions.cs, PresetsViewModel.cs, SettingsViewModel.cs
├─ Playback/
│   ├─ IMediaPlayer.cs, MediaElementPlayer.cs, SequencePlaybackController.cs
│   └─ AudioLanePlayer.cs, AudioMixPreview.cs
├─ Timeline/                          # логика доски, отделённая от отрисовки
│   ├─ TimelineMetrics.cs             # время ↔ пиксели, зум, скролл
│   ├─ TimelineLayout.cs, TimelineHitTester.cs, SnapEngine.cs, ToolCursors.cs
│   └─ TimelineThumbnailLoader.cs, ThumbnailImageCache.cs, AudioWaveformCache.cs
├─ Controls/
│   └─ TimelineControl.cs, FramingHost.cs, AspectFrame.cs, SmoothScroll.cs,
│      SliderInteraction.cs
├─ Theming/  ThemeManager.cs, ThemeOption.cs
├─ Localization/  Strings.resx, Strings.cs, LocalizationManager.cs (loc:Tr в разметке)
├─ Services/
│   ├─ UiServices.cs (IDialogService, IFileDialogService, IUiDispatcher, DragDropFileValidator)
│   └─ ShellIntegration.cs, CommandLine.cs, WindowSnapshot.cs, AutosaveService.cs
├─ Formatting/  DisplayFormat.cs, SpeedScale.cs
├─ Converters/CommonConverters.cs
├─ Assets/  app.ico, logo.png
└─ Resources/
    ├─ Palette.Dark.xaml, Palette.Light.xaml   # подменяются на лету (ADR-25)
    └─ Theme.xaml, Controls.xaml, Menu.xaml, Icons.xaml, Busy.xaml, Templates.xaml
```

## Поток данных (главный сценарий)

```
Пользователь бросает файл
        │
        ▼
DragDropFileValidator ──► ProjectViewModel.AddSourceAsync(path)
        │
        ▼
IMediaProbe (ffprobe -print_format json) ──► MediaInfo ──► MediaSource
        │
        ▼
EditHistory.Execute(IEditCommand)  ← ножницы, drag, скорость, удаление (undo/redo)
        │
        ▼
Sequence { VideoTrack { Clip[] } }             ← читают плеер, таймлайн и экспорт
        │
        ▼
ExportViewModel: Project + ExportSettings ──► ExportRequest
        │
        ▼
IExportPlanner ──► ExportPlan { Stage[] }      (чистая логика, тестируется без ffmpeg)
        │
        ▼
IJobQueue.Enqueue(JobDescriptor) ──► JobRunner ──► IExportEngine
        │                                              │
        │                          IProcessRunner ──► ffmpeg.exe (-progress pipe:1)
        │                                              │
        └──◄── JobProgress (percent, eta, stage) ◄─────┘
        │
        ▼
JobResult { OutputPath } ──► ExportDoneView (Открыть файл / Открыть папку)
```

## Почему именно так

**Отдельный `Core` без зависимостей.** Планирование экспорта, валидация пресетов, расчёт
итогового разрешения и оценка размера — чистые функции. Их можно покрыть быстрыми
unit-тестами без установленного ffmpeg и без запуска UI. Ошибки именно в этой логике самые
дорогие (пользователь получит не то видео), поэтому она живёт там, где её легче всего проверить.

**Последовательность клипов вместо «файл + интервалы».** Ядро — таймлайн: упорядоченные клипы,
у каждого свой источник, свои in/out, своя скорость и громкость. Trim, Cut, реорднер и скорость
куска выражаются как операции над клипами, а не как отдельные сущности. Все изменения идут
через `IEditCommand` и `EditHistory` — это единственный способ получить надёжный `Ctrl+Z`.

**`ExportPlan` как промежуточное представление.** ViewModel не строит команды. Она собирает
декларативный `ExportRequest` (последовательность клипов + настройки вывода). Планировщик
превращает это в стадии, движок — в вызовы процессов. За счёт этого:
- можно показать пользователю точную сводку до запуска;
- можно выбрать быструю стратегию (copy) вместо перекодирования;
- новая операция = новая стратегия, ядро не переписывается.

**Аргументы только массивом.** `ProcessStartInfo.ArgumentList`, никакой склейки строк и
никакого `cmd /c`. Это само по себе решает пробелы, кириллицу в путях (`Рабочий стол`),
кавычки и исключает инъекцию через имя файла.

**Одна задача — один `ITempWorkspace`.** Временные файлы живут в папке задачи и удаляются
вместе с ней при любом исходе, включая отмену и падение.

## Целевая платформа и поставка

- **Windows 10 (1809+) x64** и новее. WPF на .NET 8 это покрывает без оговорок.
- **Portable-папка**: распаковал и запустил, установщик не делаем (может появиться позже).
- **ffmpeg лежит рядом с приложением** в подпапке `ffmpeg/`; в разработке — в
  `%LOCALAPPDATA%\MeowsCut\ffmpeg`, чтобы 328 МБ бинарников не синхронизировались в OneDrive
  и не копировались в `bin/` при каждой сборке. Сборка релиза: `tools/publish-portable.ps1`.
- **Язык интерфейса — русский**, но все строки сразу выносятся в `Strings.ru.resx`;
  английский добавляется файлом `Strings.en.resx` без правки кода. Хардкод текста в XAML запрещён.
- Проект лежит в OneDrive-папке с кириллицей в пути — дополнительный аргумент за `ArgumentList`
  и за UTF-8 при записи временных файлов и списков concat.

Состояние окружения и как его восстановить — в [10-DEV-SETUP.md](10-DEV-SETUP.md).
