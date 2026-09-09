# 06. Очередь задач, ошибки, логирование, настройки

## 6.1 Очередь обработки

Даже когда задача одна, очередь нужна: она даёт единое место для прогресса, отмены,
логирования и корректного завершения при выходе из приложения.

```csharp
interface IJobQueue
{
    JobHandle Enqueue(JobDescriptor descriptor);
    IReadOnlyList<JobHandle> ActiveJobs { get; }
    event EventHandler<JobStateChangedEventArgs>? StateChanged;
    Task DrainAsync(CancellationToken ct);          // корректное завершение при выходе
}

sealed record JobDescriptor(
    string Title,                                   // «Экспорт clip_edited.mp4»
    JobKind Kind,                                   // Export | Preview | Thumbnails | Probe
    Func<IProgress<JobProgress>, CancellationToken, Task<JobResult>> Work);

sealed class JobHandle
{
    Guid Id { get; }
    string Title { get; }
    JobStatus Status { get; }                       // Queued | Running | Completed | Failed | Canceled
    JobProgress Progress { get; }
    Task<JobResult> Completion { get; }
    void Cancel();
}
```

Реализация: `System.Threading.Channels.Channel<JobHandle>` (unbounded) + фоновый worker,
запускаемый как `IHostedService`. Степень параллелизма — **1 по умолчанию** (кодирование и так
съедает все ядра; параллельный запуск замедлит обе задачи), значение вынесено в настройки,
чтобы будущий пакетный режим мог его поднять.

Каждая задача получает собственный `CancellationTokenSource`, связанный с общим токеном
завершения приложения. При закрытии окна: отмена всех задач → `DrainAsync` → очистка temp.

Прогресс уходит наружу через `IProgress<JobProgress>`; ViewModel подписывается и маршалит
обновления в UI-поток через `IUiDispatcher` с троттлингом ~10 обновлений в секунду —
ffmpeg присылает блоки чаще, а перерисовывать интерфейс 60 раз в секунду ради процента незачем.

## 6.2 Обработка ошибок

Два канала, каждый со своей задачей:

**Ожидаемые ситуации → `Result<T>`**: валидация настроек, нарушения ограничений пресета,
неподдерживаемый файл, отмена. Это нормальный ход работы, а не исключение.

**Исключительные ситуации → типизированные исключения:**

```csharp
abstract class MeowsCutException : Exception { ErrorCode Code { get; } }

sealed class FfmpegNotFoundException     : MeowsCutException   // бинарники не найдены
sealed class FfmpegExecutionException    : MeowsCutException   // ExitCode + StdErrTail + Arguments
sealed class MediaProbeException         : MeowsCutException   // файл не читается ffprobe
sealed class UnsupportedMediaException   : MeowsCutException   // нет видеопотока, DRM, битый файл
sealed class OutputWriteException        : MeowsCutException   // нет прав, нет места, файл занят
sealed class TempWorkspaceException      : MeowsCutException
```

Перевод для пользователя — `IErrorPresenter`, единственное место, где технический сбой
превращается в человеческий текст:

```csharp
sealed record AppError(
    ErrorCode Code,
    string Title,          // «Не удалось сохранить видео»
    string Message,        // «На диске D: не хватает места: нужно ~1.2 ГБ, свободно 400 МБ.»
    string? Suggestion,    // «Освободите место или выберите другую папку.»
    string? TechnicalDetails,   // аргументы + хвост stderr, скрыто под «Подробности»
    bool IsRecoverable);
```

Правила:
- Пользователь никогда не видит `ExitCode 1` первым экраном; технические детали доступны, но свёрнуты.
- В диалоге ошибки есть кнопки «Скопировать детали» и «Открыть лог» — этого достаточно
  для внятного баг-репорта.
- Распознавание частых причин по stderr (таблица подстрок → `ErrorCode`): `No space left`,
  `Permission denied`, `Invalid data found`, `Unknown encoder`, `moov atom not found`.
- Необработанные исключения перехватываются глобально (`DispatcherUnhandledException`,
  `TaskScheduler.UnobservedTaskException`, `AppDomain.UnhandledException`): пишем в лог,
  показываем диалог, приложение по возможности продолжает работу.
- Отмена — не ошибка: `OperationCanceledException` никогда не доходит до диалога.

## 6.3 Логирование

`Microsoft.Extensions.Logging` + Serilog:

```
%LOCALAPPDATA%\MeowsCut\logs\meowscut-20260909.log     (rolling по дням, хранить 7 файлов)
```

- Уровни: `Information` — жизненный цикл (старт, открытие файла, запуск/финиш задачи),
  `Debug` — полная команда ffmpeg и параметры плана, `Warning` — деградации (нет энкодера,
  пресет не загрузился), `Error` — сбои с кодом и хвостом stderr.
- В релизной сборке по умолчанию `Information`, переключатель «Подробные логи» в настройках
  поднимает до `Debug` без перезапуска (`LoggingLevelSwitch`).
- Логи не содержат содержимого видео; путь к файлу пишется — он нужен для диагностики
  (это локальное приложение, данные никуда не отправляются).
- Каждая задача логируется с `JobId` в scope, чтобы строки нескольких стадий не путались.
- Кнопка «Открыть папку логов» в настройках.

## 6.4 Настройки приложения

```csharp
sealed record AppSettings(
    string? FfmpegDirectory,          // переопределение автопоиска
    string? DefaultOutputDirectory,   // null = рядом с исходником
    string OutputNameTemplate,        // "{name}_meows{ext}"
    AppTheme Theme,                   // Dark | Light | System
    bool VerboseLogging,
    int MaxParallelJobs,              // 1
    long ThumbnailCacheLimitBytes,
    IReadOnlyList<string> RecentFiles);
```

Хранение: `%APPDATA%\MeowsCut\settings.json`, запись атомарная (temp + `File.Replace`),
битый файл → дефолты + бэкап испорченного рядом. `IAppSettingsStore` с кэшированием в памяти
и событием изменения.

`AppPaths` — единственное место, где вычисляются пути приложения (settings, logs, temp,
thumbs, presets). Никаких `Environment.GetFolderPath` по коду.

## 6.5 DI и запуск приложения

```csharp
// App.xaml.cs
var host = Host.CreateApplicationBuilder()
    .AddMeowsCutCore()        // валидаторы, планировщик, пресеты, настройки
    .AddMeowsCutFfmpeg()      // локатор, runner, probe, engine, thumbnails, temp
    .AddMeowsCutUi()          // ViewModels, диалоги, инструменты
    .Build();
```

Времена жизни:
- **Singleton**: `IAppSettingsStore`, `IMediaToolsetLocator`, `IJobQueue`, `IPresetProvider`,
  `IThumbnailService`, `ILogger`, `IUiDispatcher`.
- **Transient**: `IExportEngine`, `IProcessRunner`, ViewModels инструментов, диалоги.
- **Scoped** в WPF не используем — область запроса отсутствует, это только путает.

DI применяется там, где даёт пользу: подмена реализаций в тестах (`IProcessRunner`,
`IFileSystem`, `IClock`), список инструментов, платформенные сервисы. Мелкие чистые
хелперы (`Clip.SplitAt()`, `Rational.Parse`) остаются статикой — заводить интерфейс
ради них нет смысла.

Порядок старта:
1. Хост построен, логи подняты, настройки прочитаны.
2. Окно показано сразу (пустое состояние).
3. Фоном: `OrphanTempCleaner`, затем `FfmpegHealthCheck`.
4. Нет ffmpeg → окно настройки; есть → возможности закэшированы, интерфейс разблокирован.

## 6.6 Тестирование

- `MeowsCut.Core.Tests` — планировщик (какая стратегия выбрана для каждого набора операций),
  модель таймлайна (разрез, ripple-удаление, границы, undo/redo), раскладка `atempo`, расчёт разрешения с
  сохранением пропорций и чётностью, матрица совместимости, применение и валидация пресетов,
  оценка размера. Быстрые, без ffmpeg.
- `MeowsCut.Ffmpeg.Tests` — парсер `-progress`, парсер JSON ffprobe (на зафиксированных
  образцах вывода), билдер аргументов (сравнение с эталонным списком), экранирование фильтров,
  `ConcatListWriter` (пути с пробелами, кириллицей и апострофом), `ProcessRunner` на
  фиктивном исполняемом файле.
- Интеграционные (помечены `[Trait("Category","RequiresFfmpeg")]`, не в обычном прогоне):
  реальный экспорт коротких сгенерированных клипов (`testsrc`), проверка длительности и
  разрешения результата через ffprobe.
