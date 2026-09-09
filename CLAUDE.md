# Meows Cut — правила проекта

Desktop-видеоредактор под Windows 10+: доска монтажа (таймлайн, клипы, ножницы) без тяжести
большого NLE. C# / .NET 8 / WPF / MVVM / FFmpeg. Всё локально, portable-папка, интерфейс русский.

## Документация (читать перед работой)

Полная документация в [docs/](docs/README.md). Ключевые файлы:

- [docs/00-VISION.md](docs/00-VISION.md) — идея, аудитория, границы v1
- [docs/01-ARCHITECTURE.md](docs/01-ARCHITECTURE.md) — слои, проекты, структура папок
- [docs/02-DOMAIN-MODEL.md](docs/02-DOMAIN-MODEL.md) — `Sequence` / `Clip` / команды / история
- [docs/03-FFMPEG-LAYER.md](docs/03-FFMPEG-LAYER.md) — работа с ffmpeg
- [docs/07-IMPLEMENTATION-PLAN.md](docs/07-IMPLEMENTATION-PLAN.md) — этапы M0–M9
- [docs/09-PROGRESS.md](docs/09-PROGRESS.md) — **где мы остановились** (обновлять в конце сессии)
- [docs/08-DECISIONS.md](docs/08-DECISIONS.md) — журнал решений (пополнять при новых)
- [docs/10-DEV-SETUP.md](docs/10-DEV-SETUP.md) — окружение: .NET SDK 8.0.425, ffmpeg

## Нерушимые правила

1. UI не знает про FFmpeg. Никаких `Process`, аргументов и stderr вне `MeowsCut.Ffmpeg`.
2. Из View не вызываются сервисы. View → ViewModel → интерфейсы `Core`.
3. `Core` не ссылается ни на WPF, ни на FFmpeg, ни на `System.Diagnostics.Process`.
4. **Последовательность меняется только через `EditHistory.Execute(IEditCommand)`.**
   Прямая мутация `Sequence` из ViewModel незаметно ломает undo.
5. Аргументы процессов — только `ProcessStartInfo.ArgumentList`. Никакой конкатенации, никакого `cmd /c`.
6. Пути к ffmpeg не хардкодятся: только `IMediaToolsetLocator`.
7. Все длительные операции — `async` с `CancellationToken`. Никаких `.Result` / `.Wait()`.
8. Отмена обязана реально убивать процесс (`Kill(entireProcessTree: true)`) и чистить temp.
9. Видео не держим в памяти — только потоково через файлы.
10. Итог пишем в `<output>.part` и переименовываем после успеха. Исходники read-only всегда.
11. Один файл — один тип, ~250 строк максимум. Раздувать файлы нельзя.
12. Никакого текста интерфейса в XAML и коде — только `Strings.ru.resx` (задел под английский).
13. Лимиты пресетов (в т.ч. Telegram) живут в JSON, а не в UI и не в коде.
14. Новый инструмент/операция = новая команда + стратегия планировщика. Ядро не переписывается.

## Тон интерфейса

Пользователь понимает FPS, битрейт, кодеки, разрешение. Термины называть прямо (CRF — это CRF),
подсказки — одной строкой, обучающих мастеров не делать. Редкие параметры прятать за
переключателем «Расширенные», а не упрощать функциональность.

## Окружение

`dotnet` 8.0.425 (`%LOCALAPPDATA%\Microsoft\dotnet`), ffmpeg/ffprobe в
`%LOCALAPPDATA%\MeowsCut\ffmpeg`. Бинарники ffmpeg в репозиторий не коммитятся (328 МБ,
папка синхронизируется OneDrive) — в portable-релиз их кладёт `tools/publish-portable.ps1`.

## Состояние

Архитектура пересмотрена под доску монтажа, реализация не начата. Следующий шаг — M0.
