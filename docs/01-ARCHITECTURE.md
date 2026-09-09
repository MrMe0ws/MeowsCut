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

```
MeowsCut/
├─ MeowsCut.sln
├─ Directory.Build.props          # общие свойства: nullable, LangVersion, анализаторы
├─ .editorconfig
├─ global.json                    # фиксация версии SDK
├─ docs/                          # эта документация
├─ assets/                        # иконки, логотип, шрифты
├─ tools/                         # скрипты: загрузка ffmpeg для dev, сборка installer
├─ src/
│  ├─ MeowsCut.Core/
│  ├─ MeowsCut.Ffmpeg/
│  └─ MeowsCut.App/
└─ tests/
   ├─ MeowsCut.Core.Tests/
   └─ MeowsCut.Ffmpeg.Tests/
```

### src/MeowsCut.Core

```
MeowsCut.Core/
├─ Media/                 # что прочитали из файла
│   ├─ MediaInfo.cs, VideoStreamInfo.cs, AudioStreamInfo.cs, ContainerInfo.cs
│   ├─ Rational.cs, MediaTime.cs, TimeRange.cs, FrameSize.cs
│   └─ MediaCapabilities.cs           # какие энкодеры доступны в текущем ffmpeg
├─ Editing/               # доска монтажа: последовательность клипов
│   ├─ Project.cs, MediaSource.cs, SourceId.cs, ClipId.cs
│   ├─ Timeline/  Sequence.cs, VideoTrack.cs, Clip.cs, ClipAudio.cs, SequenceFormat.cs
│   ├─ Commands/  IEditCommand.cs, SplitClipCommand.cs, RemoveClipCommand.cs,
│   │             TrimClipEdgeCommand.cs, MoveClipCommand.cs, SetClipSpeedCommand.cs,
│   │             SetClipAudioCommand.cs, AppendClipCommand.cs, RemoveRangeCommand.cs
│   └─ History/   EditHistory.cs, SequenceChangedEventArgs.cs
├─ Export/                # во что рендерим
│   ├─ ExportSettings.cs, VideoSettings.cs, AudioSettings.cs
│   ├─ RateControl.cs, ResolutionSpec.cs, FrameRateSpec.cs
│   ├─ ContainerFormat.cs, VideoCodec.cs, AudioCodec.cs
│   ├─ ExportRequest.cs, ExportSummary.cs, OutputSizeEstimator.cs
│   └─ CompatibilityMatrix.cs         # какой кодек допустим в каком контейнере
├─ Presets/
│   ├─ PresetDefinition.cs, PresetGroup.cs, PresetConstraints.cs
│   ├─ IPresetProvider.cs, IPresetApplier.cs, IPresetValidator.cs
│   └─ PresetApplyResult.cs, PresetValidationResult.cs
├─ Processing/            # план работ, без знания о ffmpeg
│   ├─ ExportPlan.cs, ExportStage.cs, StageKind.cs
│   └─ IExportPlanner.cs
├─ Jobs/
│   ├─ IJobQueue.cs, JobDescriptor.cs, JobHandle.cs
│   ├─ JobStatus.cs, JobProgress.cs, JobResult.cs
│   └─ IJobRunner.cs
├─ Abstractions/
│   ├─ IMediaProbe.cs, IExportEngine.cs, IThumbnailService.cs
│   ├─ IMediaToolset.cs, IMediaToolsetLocator.cs, IToolsetHealthCheck.cs
│   ├─ ITempWorkspaceFactory.cs, ITempWorkspace.cs
│   └─ IFileSystem.cs, IClock.cs
├─ Diagnostics/
│   ├─ MeowsCutException.cs + наследники
│   ├─ AppError.cs, ErrorCode.cs, IErrorPresenter.cs
│   └─ Result.cs, Result{T}.cs
├─ Configuration/
│   ├─ AppSettings.cs, IAppSettingsStore.cs, AppPaths.cs
└─ Validation/
    ├─ ProjectValidator.cs, ExportSettingsValidator.cs
```

### src/MeowsCut.Ffmpeg

```
MeowsCut.Ffmpeg/
├─ Toolset/
│   ├─ FfmpegToolsetLocator.cs        # порядок поиска бинарников
│   ├─ FfmpegToolset.cs               # пути + версия + возможности
│   ├─ EncoderProbe.cs                # ffmpeg -encoders / -hwaccels → MediaCapabilities
│   └─ FfmpegHealthCheck.cs           # старт приложения: есть ли, работает ли
├─ Execution/
│   ├─ IProcessRunner.cs, ProcessRunner.cs
│   ├─ ProcessRequest.cs, ProcessResult.cs
│   ├─ StdErrRingBuffer.cs            # последние N строк для диагностики
│   └─ ProcessTerminator.cs           # q в stdin → grace period → Kill(entireProcessTree)
├─ Arguments/
│   ├─ FfmpegArgumentBuilder.cs       # типобезопасная сборка IReadOnlyList<string>
│   ├─ FilterGraphBuilder.cs, FilterChain.cs, FilterNode.cs
│   ├─ FilterValueEscaper.cs          # экранирование внутри filter_complex
│   └─ EncoderCatalog.cs              # VideoCodec → энкодер + профиль/preset/pix_fmt
├─ Probing/
│   ├─ FfprobeMediaProbe.cs
│   ├─ Json/FfprobeResponse.cs (+ Stream/Format DTO)
│   └─ MediaInfoMapper.cs
├─ Planning/
│   ├─ FfmpegExportPlanner.cs         # ExportRequest → ExportPlan
│   └─ Strategies/
│       ├─ StreamCopyTrimStrategy.cs
│       ├─ FilterGraphSegmentsStrategy.cs
│       ├─ ConcatDemuxerStrategy.cs
│       ├─ SpeedStrategy.cs
│       └─ ScaleFpsStrategy.cs
├─ Encoding/
│   ├─ FfmpegExportEngine.cs          # выполняет ExportPlan стадия за стадией
│   └─ TwoPassRunner.cs               # для целевого размера (стикеры)
├─ Progress/
│   ├─ FfmpegProgressReader.cs        # -progress pipe:1 → key=value
│   ├─ ProgressSnapshot.cs, EtaEstimator.cs
│   └─ StageProgressAggregator.cs     # веса стадий → общий процент
├─ Temp/
│   ├─ TempWorkspaceFactory.cs, TempWorkspace.cs
│   ├─ ConcatListWriter.cs
│   └─ OrphanTempCleaner.cs
└─ Thumbnails/
    ├─ FfmpegThumbnailService.cs, ThumbnailCache.cs
```

### src/MeowsCut.App

```
MeowsCut.App/
├─ App.xaml / App.xaml.cs             # Generic Host, DI, глобальный обработчик исключений
├─ Composition/
│   ├─ ServiceRegistration.cs         # AddCore(), AddFfmpeg(), AddUi()
│   └─ ToolRegistration.cs            # регистрация инструментов редактора
├─ Views/
│   ├─ ShellWindow.xaml               # рамка приложения
│   ├─ Panes/  EmptyStateView, SourceBinView, PreviewView, TimelineView,
│   │          InspectorView, ExportPanelView, ExportProgressView, ExportDoneView
│   ├─ Inspector/  ClipPropertiesView, VideoSettingsView, AudioSettingsView,
│   │              AdvancedSettingsView, PresetsView
│   └─ Dialogs/ SettingsWindow, FfmpegSetupWindow, ErrorDialog
├─ ViewModels/
│   ├─ ShellViewModel.cs, ProjectViewModel.cs
│   ├─ SourceBinViewModel.cs, PreviewViewModel.cs
│   ├─ TimelineViewModel.cs, ClipViewModel.cs, InspectorViewModel.cs
│   └─ ExportViewModel.cs, PresetsViewModel.cs, SettingsViewModel.cs, FfmpegSetupViewModel.cs
├─ Playback/
│   ├─ IMediaPlayer.cs, MediaElementPlayer.cs
│   └─ SequencePlaybackController.cs, PlayheadTimer.cs
├─ Timeline/                          # логика доски, отделённая от отрисовки
│   ├─ TimelineTool.cs (Select | Razor | Hand | Zoom)
│   ├─ TimelineMetrics.cs             # время ↔ пиксели, зум, скролл
│   ├─ SnapEngine.cs, HitTester.cs, DragSession.cs
├─ Controls/
│   ├─ TimelineControl.cs, ClipRenderer.cs, WaveformRenderer.cs, PlayheadAdorner.cs
│   └─ TimecodeBox.cs, DropZone.cs, LabeledSlider.cs, TransportBar.cs
├─ Localization/
│   ├─ Strings.ru.resx, Strings.en.resx, ILocalizer.cs
├─ Services/
│   ├─ IDialogService / DialogService
│   ├─ IFileDialogService / FileDialogService
│   ├─ IShellIntegration / ShellIntegration      # открыть файл / показать в проводнике
│   ├─ IUiDispatcher / UiDispatcher
│   └─ RecentFilesService.cs, DragDropFileValidator.cs
├─ Converters/, Behaviors/
└─ Resources/
    ├─ Theme.Dark.xaml, Theme.Light.xaml, Colors.xaml
    └─ Typography.xaml, Controls.xaml, Icons.xaml
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
