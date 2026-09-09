# 04. Система пресетов и Telegram Stickers

Требование заказчика: **ограничения Telegram не зашиваются в UI**. Пресет — это данные,
а не код и не разметка. Меняется требование — правится JSON, приложение не пересобирается.

## 4.1 Модель пресета

```csharp
sealed record PresetDefinition(
    string Id,                       // "telegram.video-sticker"
    string GroupId,                  // "telegram"
    string Title,                    // "Видеостикер"
    string Description,              // текст для человека, а не спецификация
    string? IconKey,
    int Order,
    PresetTemplate Template,         // что выставить
    PresetConstraints Constraints,   // что проверить
    IReadOnlyList<string> Notes);    // подсказки в панели ("аудио будет удалено")

sealed record PresetGroup(string Id, string Title, string? Description, int Order);
```

`PresetTemplate` — частичные настройки: заданные поля применяются, `null` оставляет текущее.

```csharp
sealed record PresetTemplate(
    ContainerFormat? Container,
    VideoCodec? VideoCodec,
    RateControl? RateControl,
    ResolutionSpec? Resolution,
    FrameRateSpec? FrameRate,
    string? PixelFormat,
    bool? AudioEnabled,
    AudioCodec? AudioCodec,
    int? AudioBitrateKbps,
    int? AudioSampleRateHz,
    int? AudioChannels,
    IReadOnlyDictionary<string, string>? ExtraEncoderOptions);  // редкие ключи вроде "-deadline good"

sealed record PresetConstraints(
    TimeSpan? MaxDuration,
    long? MaxFileSizeBytes,
    int? MaxWidth, int? MaxHeight,
    bool? RequireSquare,
    bool? RequireEvenDimensions,
    double? MaxFps,
    bool? ForbidAudio,
    IReadOnlyList<ContainerFormat>? AllowedContainers,
    IReadOnlyList<VideoCodec>? AllowedVideoCodecs);
```

## 4.2 Сервисы

```csharp
interface IPresetProvider
{
    IReadOnlyList<PresetGroup> GetGroups();
    IReadOnlyList<PresetDefinition> GetPresets(string groupId);
    PresetDefinition? FindById(string id);
    Task ReloadAsync(CancellationToken ct);
}

interface IPresetApplier
{
    PresetApplyResult Apply(PresetDefinition preset, Project project, ExportSettings current);
}

sealed record PresetApplyResult(
    ExportSettings Settings,
    Project Project,                           // пресет может подрезать клипы под лимит длительности
    IReadOnlyList<PresetChange> Changes,       // «FPS: 60 → 30» — показываем пользователю
    IReadOnlyList<PresetWarning> Warnings);

interface IPresetValidator
{
    PresetValidationResult Validate(Project project, ExportSettings settings, PresetConstraints c);
}

sealed record PresetValidationResult(
    bool IsSatisfied,
    IReadOnlyList<ConstraintViolation> Violations,   // с предложенным автоисправлением
    IReadOnlyList<string> Hints);
```

Ключевое поведение: применение пресета **никогда не молчит**. Все изменения возвращаются
списком и показываются в панели: «Аудио выключено», «Длительность обрезана до 3 с»,
«Кодек изменён на VP9». Пользователь понимает, что произошло с его видео.

## 4.3 Хранение и загрузка

```
MeowsCut.Core/Presets/Definitions/     (встроенные ресурсы, поставляются с приложением)
├─ general.json          # Web / Мессенджер / Максимальное качество / Маленький файл
└─ telegram.json         # раздел Telegram Stickers

%APPDATA%\MeowsCut\presets\*.json      (пользовательские, перекрывают встроенные по Id)
```

- Формат версионируется полем `schemaVersion`; загрузчик отвергает неизвестную версию с записью в лог,
  но приложение продолжает работать на встроенных пресетах.
- Битый пользовательский JSON не роняет приложение: файл пропускается, в лог — причина,
  в настройках — отметка «пресеты пользователя частично не загружены».
- `ReloadAsync` позволяет обновить пресеты без перезапуска (кнопка в настройках).

Пример записи:

```json
{
  "schemaVersion": 1,
  "group": { "id": "telegram", "title": "Telegram", "order": 10 },
  "presets": [
    {
      "id": "telegram.video-sticker",
      "title": "Видеостикер",
      "description": "Квадратное короткое видео без звука для набора стикеров",
      "order": 10,
      "template": {
        "container": "WebM",
        "videoCodec": "Vp9",
        "rateControl": { "kind": "TargetSize", "bytes": 256000 },
        "resolution": { "kind": "Custom", "width": 512, "height": 512,
                        "keepAspect": true, "fit": "Contain" },
        "frameRate": { "kind": "Fixed", "fps": 30 },
        "pixelFormat": "yuva420p",
        "audioEnabled": false,
        "extraEncoderOptions": { "-deadline": "good", "-cpu-used": "2", "-auto-alt-ref": "0" }
      },
      "constraints": {
        "maxDuration": "00:00:03",
        "maxFileSizeBytes": 256000,
        "maxWidth": 512, "maxHeight": 512,
        "requireSquare": true,
        "maxFps": 30,
        "forbidAudio": true,
        "allowedContainers": ["WebM"],
        "allowedVideoCodecs": ["Vp9"]
      },
      "notes": [
        "Звук в стикерах не воспроизводится и будет удалён",
        "Если фрагмент длиннее лимита, он будет обрезан с начала выделения"
      ]
    }
  ]
}
```

Стартовый набор группы `telegram`: «Видеостикер», «Эмодзи», «Видеосообщение (кружок)»,
«Сжать для отправки». Конкретные значения лимитов — в JSON и легко правятся, если Telegram
поменяет требования.

## 4.4 Что делает пресет в UI

1. Пользователь открывает раздел **Telegram Stickers** и выбирает карточку пресета.
2. `IPresetApplier` применяет шаблон к текущим `ExportSettings` и, при лимите длительности, к `Sequence`.
3. Панель показывает список изменений и заметок.
4. `IPresetValidator` проверяет результат; нарушения выводятся как «Требуется: длительность
   не более 3 с» с кнопкой **Исправить** (автофикс: обрезать/уменьшить/выключить аудио).
5. Экспорт заблокирован, пока есть неисправленные нарушения уровня Error.

Ручные поля остаются доступными: пресет — стартовая точка, а не клетка. Если пользователь
меняет параметр вручную, пресет помечается как «изменён», а валидатор продолжает предупреждать
о нарушениях лимитов.

## 4.5 Целевой размер файла

Ограничение размера (стикеры) реализуется в `RateControl.TargetSize`:

1. Планировщик считает `bitrate ≈ (targetBytes × 8) / durationSeconds × 0.95`.
2. Двухпроходное кодирование VP9 с `-b:v` этой величины.
3. После кодирования размер проверяется; если промах вверх — одна повторная попытка с
   коэффициентом от фактического результата (максимум 2 итерации, каждая — отдельная стадия
   с собственным прогрессом).

Больше двух итераций не делаем: время пользователя дороже последних килобайт, а вместо этого
показываем понятный совет («сократите длительность или уменьшите детализацию»).
