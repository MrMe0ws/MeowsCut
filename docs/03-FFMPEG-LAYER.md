# 03. FFmpeg abstraction layer

Единственное место в приложении, где существуют процессы, аргументы и stderr.
Всё остальное общается через `IMediaProbe`, `IExportEngine`, `IThumbnailService`.

## 3.1 Поиск бинарников (`IMediaToolsetLocator`)

Приложение поставляется **portable**: бинарники ffmpeg лежат рядом с exe, поэтому основной
сценарий — папка `ffmpeg/`. Но пути всё равно **никогда не хардкодятся**, порядок поиска,
первое совпадение выигрывает:

1. Явный путь из настроек пользователя (`AppSettings.FfmpegDirectory`) — если нужна своя сборка.
2. Папка `ffmpeg/` рядом с исполняемым файлом — **основной путь поставки** (portable-релиз).
3. `%LOCALAPPDATA%\MeowsCut\ffmpeg` — **путь для разработки** (см. [10-DEV-SETUP.md](10-DEV-SETUP.md)).
4. Переменная окружения `MEOWSCUT_FFMPEG_DIR`.
5. `PATH` (перебор `ffmpeg.exe`, `ffprobe.exe`).
6. Типовые места установки: `%LOCALAPPDATA%\Microsoft\WinGet\Links`, `C:\ProgramData\chocolatey\bin`,
   `%USERPROFILE%\scoop\shims`, `C:\ffmpeg\bin`.

Почему бинарники не лежат в репозитории: статическая сборка — это 328 МБ на два exe, а проект
находится в синхронизируемой папке OneDrive; каждый билд копировал бы их ещё и в `bin/`.
Поэтому в разработке ffmpeg живёт в `%LOCALAPPDATA%\MeowsCut\ffmpeg` (шаг 3), а в выходную папку
он попадает только при сборке portable-релиза — скриптом `tools/publish-portable.ps1`.

Используемая сборка — BtbN `win64-gpl` (x264, x265, libvpx-vp9, SVT-AV1, libaom, opus, lame,
vorbis + аппаратные энкодеры nvenc/qsv/amf). Лицензия GPL: при распространении рядом должны
лежать `LICENSE.txt` сборки и ссылка на исходники.

```csharp
sealed record ToolsetLocationResult(
    bool Found, string? FfmpegPath, string? FfprobePath,
    ToolsetSource Source, string? Version, string? FailureReason);
```

Найденный набор валидируется (`ffmpeg -version`, `ffprobe -version`), результат кэшируется в
`AppSettings` вместе с версией. Если версия изменилась — кэш возможностей сбрасывается.

### Проверка при запуске

`FfmpegHealthCheck` выполняется на старте асинхронно, **не блокируя показ окна**:

1. Локатор ищет бинарники.
2. `ffmpeg -hide_banner -encoders` → парсим доступные энкодеры;
   `-hwaccels` → доступные аппаратные ускорители.
3. Результат → `MediaCapabilities` (что реально можно кодировать), кэшируется на диск.
4. Не найдено → окно `FfmpegSetupWindow`: объяснение, кнопка «Указать папку», кнопка
   «Как установить» (winget-команда с копированием в буфер). Открытие видео заблокировано,
   приложение не падает.

`MediaCapabilities` — причина, по которой UI не предлагает AV1, если сборка ffmpeg без
`libsvtav1`. Лучше не показать опцию, чем показать и упасть на экспорте.

## 3.2 Запуск процессов (`IProcessRunner`)

```csharp
sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,       // строго список, никакой строки целиком
    string? WorkingDirectory = null,
    bool RedirectStdIn = true);            // нужен для graceful stop

sealed record ProcessResult(int ExitCode, IReadOnlyList<string> StdErrTail, TimeSpan Elapsed);

interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        ProcessRequest request,
        Action<string>? onStdOutLine,      // канал прогресса
        Action<string>? onStdErrLine,      // диагностика
        CancellationToken ct);
}
```

Как именно запускаем:

- `ProcessStartInfo.ArgumentList.Add(...)` для каждого аргумента → Windows-экранирование делает
  сам рантайм. Пробелы, кириллица, `&`, кавычки в именах файлов перестают быть проблемой.
- `UseShellExecute = false`, `CreateNoWindow = true` — консольное окно не мигает.
- `StandardOutputEncoding = StandardErrorEncoding = UTF8` — иначе кириллица в логах превращается в мусор.
- Чтение stdout/stderr **асинхронно и параллельно** (`BeginOutputReadLine` / отдельные `Task`),
  иначе процесс встаёт на заполненном пайпе.
- stderr пишется в `StdErrRingBuffer` (последние 200 строк) — в лог целиком уходит только при ошибке.
- Полная команда логируется на уровне `Debug` в виде готовой к копированию строки — незаменимо
  при разборе жалоб.

## 3.3 Сборка аргументов (`FfmpegArgumentBuilder`)

Fluent-билдер, возвращающий `IReadOnlyList<string>`. Ни одного места, где строится строка команды.

```csharp
var args = FfmpegArgumentBuilder.Create()
    .HideBanner().OverwriteOutput().NoStdin()
    .ProgressToStdout()                       // -progress pipe:1 -nostats
    .LogLevel(FfmpegLogLevel.Error)
    .InputSeek(TimeSpan.FromSeconds(5))       // -ss перед -i = быстрый поиск
    .Input(sourcePath)
    .FilterComplex(graph)
    .Map("[v]").Map("[a]")
    .VideoCodec("libx264").Crf(23).Preset("medium").PixelFormat("yuv420p")
    .AudioCodec("aac").AudioBitrate(192)
    .MovFlags("+faststart")
    .Output(tempOutputPath)
    .Build();
```

Правила билдера:

- Порядок аргументов ffmpeg значим (глобальные → входные → фильтры → выходные) — билдер
  собирает секции в правильном порядке независимо от порядка вызовов.
- `-y` ставится всегда, но пишем мы во временный файл, а не поверх пользовательского.
- Точность seek: `-ss` до `-i` быстрый, но при stream-copy режет по ключевым кадрам;
  при перекодировании даёт точный результат (ffmpeg делает точный seek с декодированием).
  Для точного copy-вырезания используется другая стратегия (см. 3.5).

### Граф фильтров

`FilterGraphBuilder` строит `-filter_complex` из типизированных узлов, а не конкатенацией:

```csharp
sealed record FilterNode(string Name, IReadOnlyDictionary<string, string> Parameters,
                         IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs);
```

`FilterValueEscaper` экранирует значения по правилам ffmpeg (`\`, `:`, `,`, `[`, `]`, `'`).
Критично для `drawtext`/путей внутри фильтров в будущем; для путей входа/выхода фильтры не
используются вовсе.

Типовые фильтры:

| Задача | Видео | Аудио |
|--------|-------|-------|
| Вырезать интервал | `trim=start=..:end=..,setpts=PTS-STARTPTS` | `atrim=..,asetpts=PTS-STARTPTS` |
| Склеить N интервалов | `concat=n=N:v=1:a=1` | — |
| Скорость K | `setpts=PTS/K` | цепочка `atempo` |
| Масштаб | `scale=w:h:force_original_aspect_ratio=decrease` + `pad` | — |
| FPS | `fps=N` | — |
| Громкость | — | `volume=K` |
| Sample rate | — | `aresample=N` |

`atempo` принимает 0.5–2.0, поэтому скорость раскладывается в произведение:
0.25 → `atempo=0.5,atempo=0.5`; 4.0 → `atempo=2.0,atempo=2.0`; 3.0 → `atempo=2.0,atempo=1.5`.
Раскладку делает `SpeedStrategy` (чистая функция, покрыта тестами).

## 3.4 Получение информации о файле

`ffprobe -v quiet -print_format json -show_format -show_streams -show_entries stream_side_data <file>`

Ответ разбирается `System.Text.Json` в DTO, затем `MediaInfoMapper` приводит к доменной модели:
дроби (`30000/1001`) → `Rational`, строки длительности → `TimeSpan`, поворот из side-data,
битрейт из потока или, если его нет, из формата.

Отсутствие обязательных полей — не исключение: у многих файлов нет `bit_rate` у потока, а у
VFR-видео `avg_frame_rate` может быть `0/0`. Маппер аккуратно деградирует до `null` и UI
показывает «неизвестно», а не падает.

## 3.5 Стратегии выполнения (`FfmpegExportPlanner`)

Вход планировщика — **последовательность клипов** (`Sequence`), а не «файл + интервалы».
Каждый клип имеет свой источник, свои in/out, свою скорость и громкость. Планировщик выбирает
самый дешёвый способ, дающий корректный результат.

**A. Remux (без перекодирования).** Условия: один клип на всю длину источника, скорость 1.0,
меняется только контейнер, кодеки совместимы. → `-c copy`. Секунды вместо минут.

**B. Один клип со stream-copy.** Условия: один клип, скорость 1.0, без масштаба/FPS/громкости,
выбран режим «Быстро». → `-ss <in> -i src -to <out> -c copy`. Границы прилипают к ключевым
кадрам (до ~секунды) — UI предупреждает; режим «Точно» переключает на C.

**C. Один проход через filter_complex (основная стратегия).** Все клипы, скорости, громкости,
масштаб и FPS — в одном графе, один запуск ffmpeg, без временных файлов. Каждый источник
добавляется как отдельный `-i`, каждый клип превращается в пару цепочек:

```
# клип 1: source0 04.5–19.2, speed 1.0
[0:v]trim=4.5:19.2,setpts=PTS-STARTPTS[v0];
[0:a]atrim=4.5:19.2,asetpts=PTS-STARTPTS,volume=1.0[a0];
# клип 2: source1 00.0–12.0, speed 2.0
[1:v]trim=0:12,setpts=(PTS-STARTPTS)/2.0[v1];
[1:a]atrim=0:12,asetpts=PTS-STARTPTS,atempo=2.0[a1];
# сборка последовательности и общий формат вывода
[v0][a0][v1][a1]concat=n=2:v=1:a=1[vc][ac];
[vc]scale=-2:1080:flags=lanczos,fps=30,format=yuv420p[v];
[ac]aformat=sample_rates=48000:channel_layouts=stereo[a]
```

Точность кадр-в-кадр, один проход, линейный прогресс. Клип с выключенным звуком заменяется
на `anullsrc` нужной длительности — иначе `concat` рассыпается на несовпадении числа потоков.
Источник без аудиодорожки обрабатывается так же.

**D. Клипы + concat demuxer (быстрый режим).** Каждый клип вырезается с `-c copy` во временный
файл, затем `-f concat -safe 0 -i list.txt -c copy`. Быстро, но границы по ключевым кадрам,
требует одинаковых параметров всех потоков и не поддерживает скорость ≠ 1.0.
Предлагается как «Быстро (без перекодирования)», когда условия выполнимы.

**E. Два прохода (целевой размер).** Для стикеров и лимитов размера: `-pass 1` в `NUL`,
затем `-pass 2`. Логи прохода — во временной папке задачи.

Правило выбора: **C по умолчанию, A/B/D — оптимизации, применяемые только когда безопасно.**
Каждая стратегия — отдельный класс с методом `bool CanHandle(request)` + `BuildStages(...)`,
поэтому новую операцию добавляем, не трогая остальные.

## 3.6 Прогресс

Запускаем с `-progress pipe:1 -nostats -loglevel error`. ffmpeg пишет в stdout блоки:

```
frame=120
fps=59.8
total_size=1048576
out_time_us=4000000
speed=2.01x
progress=continue        ← конец блока; в самом конце progress=end
```

`FfmpegProgressReader` собирает блок до строки `progress=`, парсит в снимок:

```csharp
sealed record ProgressSnapshot(long? Frame, double? Fps, TimeSpan? OutTime,
                               long? TotalSizeBytes, double? SpeedFactor, bool Completed);
```

Процент внутри стадии: `OutTime / stage.ExpectedDuration`. Ожидаемую длительность знает
планировщик (например, при вырезании и ускорении вдвое это не длина исходника, а
`сумма длительностей клипов с учётом их скоростей`) — поэтому прогресс не «залипает на 30%» и не «прыгает до 100%».

Общий процент: `StageProgressAggregator` складывает стадии с весами:
`total = Σ(weight_i × percent_i)`. Веса пропорциональны ожидаемой длительности стадии
(для двухпроходного режима первый проход ≈ 0.45, второй ≈ 0.55).

ETA: `EtaEstimator` держит EMA от `speed` (сглаживание ~0.2), остаток =
`(expected − outTime) / smoothedSpeed`, значение не показывается первые 2 секунды и никогда
не увеличивается скачками — иначе цифра выглядит сломанной.

```csharp
sealed record JobProgress(
    double Percent, TimeSpan Elapsed, TimeSpan? Remaining,
    string StageName,                 // «Обработка видео (2 из 3)»
    string? DetailLine,               // «120 кадров, 2.0x»
    int StageIndex, int StageCount);
```

Fallback: если по какой-то причине `-progress` молчит более 10 секунд, читаем `time=` из
stderr (регулярка) — не первый выбор, но подстраховка для экзотических сборок.

## 3.7 Отмена

Единая точка: `CancellationToken` от `JobHandle.Cancel()`.

```
ct.Cancel()
   │
   ├─► ProcessTerminator: записать "q\n" в stdin ffmpeg (корректное завершение)
   ├─► ждать 1500 мс завершения
   ├─► если жив → Process.Kill(entireProcessTree: true)
   ├─► дождаться выхода, закрыть пайпы
   ├─► TempWorkspace.DisposeAsync() → удалить все временные файлы стадии
   ├─► удалить недописанный <output>.part
   └─► JobStatus.Canceled (никакого диалога ошибки)
```

Что важно:
- `entireProcessTree: true` обязателен: ffmpeg может порождать дочерние процессы.
- `OperationCanceledException` ловится в `JobRunner` и превращается в статус, а не в ошибку.
- Отмена идемпотентна: повторный клик по «Отмена» ничего не ломает.
- Все `Process` создаются в `try/finally` с `Dispose`, регистрация в `ct.Register` освобождается.
- При выходе из приложения все активные задачи отменяются и temp-папки чистятся.

## 3.8 Стратегия временных файлов

```
%LOCALAPPDATA%\MeowsCut\
├─ temp\
│  └─ job-<guid>\          ← ITempWorkspace, живёт ровно столько, сколько задача
│     ├─ seg-000.mp4
│     ├─ concat.txt
│     └─ pass.log
├─ thumbs\<hash-файла>\    ← кэш миниатюр, LRU-очистка по размеру (лимит ~500 МБ)
└─ logs\
```

Правила:

1. `ITempWorkspace : IAsyncDisposable` — создаётся на задачу, удаляется при **любом** исходе.
2. При старте приложения `OrphanTempCleaner` удаляет папки задач старше 24 часов
   (последствия аварийного завершения).
3. Итоговый файл пишется как `<output>.meowscut.part` **в целевой папке** и переименовывается
   `File.Move(part, final, overwrite)` только после `ExitCode == 0`. Пользователь никогда не
   получает битый файл, и переименование внутри тома атомарно (в отличие от копирования из temp).
4. Перед стартом проверяем свободное место: `оценка размера × 2.5`. Не хватает — понятная ошибка
   до начала работы, а не через 20 минут.
5. `concat.txt` пишется в UTF-8 **без BOM**, пути в формате `file '<abs-path>'`, одинарные
   кавычки внутри пути экранируются как `'\''`. С `-safe 0`.
6. Никаких `Path.GetTempFileName()` (лимит 65535 файлов и мусор в системном temp) и никакого
   хранения кадров/видео в памяти — обмен между стадиями только через файлы.

## 3.9 Миниатюры и превью

- Превью воспроизведения — WPF `MediaElement` с исходным файлом (без ffmpeg вообще).
  Если системный кодек файл не тянет (например, экзотический контейнер), UI автоматически
  переключается на покадровый предпросмотр из кэша миниатюр.
- Полоса миниатюр для таймлайна: `ffmpeg -ss <t> -i <in> -frames:v 1 -vf scale=160:-2 out.jpg`,
  запрашивается пачками, лениво, с отменой при перемотке; кэш по хешу «путь+размер+mtime».
- Предпросмотр результата (эффектов) в v1 — кнопка «Проверить 5 секунд»: тот же движок
  экспортирует короткий фрагмент во временный файл. Переиспользуем весь пайплайн вместо
  того, чтобы строить второй, реалтайм-рендерер.
