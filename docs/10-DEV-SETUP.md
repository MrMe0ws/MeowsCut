# 10. Окружение разработки

## Что установлено (2026-09-09, эта машина)

| Компонент | Версия | Где лежит |
|---|---|---|
| .NET SDK | **8.0.425** | `%LOCALAPPDATA%\Microsoft\dotnet` (установка per-user, без прав администратора) |
| .NET runtime | 8.0.7 + WindowsDesktop 8.0.7 | `C:\Program Files\dotnet` (было в системе) |
| FFmpeg / FFprobe | BtbN `win64-gpl`, N-126482 (2026-09-09) | `%LOCALAPPDATA%\MeowsCut\ffmpeg` |
| Git | 2.x | `C:\Program Files\Git` |

Переменные окружения пользователя, добавленные при установке:

```
Path        += %LOCALAPPDATA%\Microsoft\dotnet   (в начало)
DOTNET_ROOT  = %LOCALAPPDATA%\Microsoft\dotnet
DOTNET_CLI_TELEMETRY_OPTOUT = 1
```

Открытые до установки терминалы новую переменную не увидят — нужен перезапуск оболочки.

## Проверка

```powershell
dotnet --version                       # 8.0.425
dotnet --list-sdks
& "$env:LOCALAPPDATA\MeowsCut\ffmpeg\ffmpeg.exe" -hide_banner -version
```

## Доступные энкодеры в поставляемой сборке ffmpeg

Проверено фактическим `ffmpeg -encoders`:

- Видео: `libx264`, `libx265`, `libvpx-vp9`, `libsvtav1`, `libaom-av1`
- Аудио: `aac`, `libopus`, `libmp3lame`, `libvorbis`, `flac`
- Аппаратные: `h264_nvenc`, `hevc_nvenc`, `av1_nvenc`, `h264_qsv`, `h264_amf`

Аппаратные энкодеры в v1 не используются (см. [00-VISION.md](00-VISION.md)), но `EncoderProbe`
их увидит, и включение позже — вопрос одной записи в `EncoderCatalog`.

## Почему ffmpeg не в репозитории

Статическая сборка — `ffmpeg.exe` 164 МБ + `ffprobe.exe` 164 МБ. Проект лежит в синхронизируемой
папке OneDrive, и копирование бинарников в `bin/` при каждой сборке означало бы сотни мегабайт
трафика синхронизации на ровном месте. Поэтому:

- **Разработка:** ffmpeg живёт в `%LOCALAPPDATA%\MeowsCut\ffmpeg`, локатор находит его третьим шагом.
- **Portable-релиз:** `tools/publish-portable.ps1` кладёт бинарники в папку публикации как `ffmpeg/`,
  где локатор находит их вторым шагом (основной путь поставки).
- `.gitignore` исключает `third_party/`, `bin/`, `obj/`, `publish/`.

## Восстановление окружения на чистой машине

```powershell
# .NET 8 SDK (per-user, без администратора)
curl.exe -sSL -o dotnet-install.ps1 https://dot.net/v1/dotnet-install.ps1
./dotnet-install.ps1 -Channel 8.0 -Quality GA -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"

# FFmpeg (то же сделает tools/get-ffmpeg.ps1, когда будет написан на M1)
curl.exe -L -o ffmpeg.zip https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip
Expand-Archive ffmpeg.zip -DestinationPath .
# ffmpeg.exe, ffprobe.exe, LICENSE.txt → %LOCALAPPDATA%\MeowsCut\ffmpeg
```

Альтернатива для SDK, если доступен winget с правами администратора:
`winget install Microsoft.DotNet.SDK.8`. На этой машине user-scope установка через winget
не сработала («применимый установщик не найден»), поэтому использован официальный скрипт.

## Скрипты проекта (появятся по мере реализации)

| Скрипт | Этап | Назначение |
|---|---|---|
| `tools/get-ffmpeg.ps1` | M1 | скачать и разложить ffmpeg в `%LOCALAPPDATA%\MeowsCut\ffmpeg` |
| `tools/publish-portable.ps1` | M8 | `dotnet publish` + копирование ffmpeg + сборка zip-архива |
| `tools/make-testclips.ps1` | M3 | генерация тестовых клипов (`testsrc`, `sine`) для интеграционных тестов |
