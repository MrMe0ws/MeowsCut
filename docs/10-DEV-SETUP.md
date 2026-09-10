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

## Запуск и проверка интерфейса

```powershell
dotnet run --project src/MeowsCut.App                 # обычный запуск
dotnet run --project src/MeowsCut.App -- "видео.mp4"  # сразу открыть файл
```

Путь к видео аргументом нужен и как функция («Открыть с помощью», перетаскивание
файла на exe), и для проверки второго экрана.

Флаг `--screenshot <путь.png>` рисует окно в PNG средствами WPF и закрывается.
Снимок делает само приложение, а не захват экрана: в кадр не попадает ничего
постороннего, и вёрстку можно проверять на любой машине.

```powershell
$exe = "src/MeowsCut.App/bin/Debug/net8.0-windows/MeowsCut.exe"
Start-Process $exe -ArgumentList @("--screenshot", '"C:\temp\ui.png"', '"C:\видео\клип.mp4"')
```

Пути с пробелами в `Start-Process -ArgumentList` обязательно брать в кавычки —
иначе аргумент разобьётся на части и файл не откроется.

## Сборка портативной версии

```powershell
./tools/publish-portable.ps1 -Zip
```

Получается папка `publish\MeowsCut` (~463 МБ): self-contained сборка под win-x64
плюс FFmpeg рядом с exe. Устанавливать .NET на целевой машине не нужно, папку
достаточно скопировать. Локатор найдёт FFmpeg вторым шагом — рядом с приложением.

## Значок приложения

`src/MeowsCut.App/Assets/app.ico` собран из `Assets/logo.png` в семи размерах
(16, 24, 32, 48, 64, 128, 256) и подключён свойством `ApplicationIcon`. Если логотип
меняется, значок надо пересобрать: Windows берёт из файла размер под ситуацию,
и одной картинки 256×256 в списке файлов мало — она мылится.

## Сборка установщика

```powershell
./tools/build-installer.ps1 -Version 1.0.0
```

Нужен Inno Setup — компилятор `ISCC.exe`. Ставится без администратора:

```powershell
# https://jrsoftware.org/isdl.php, либо релиз с GitHub авторов
innosetup-7.1.0-x64.exe /VERYSILENT /CURRENTUSER /SP- /NORESTART
```

На этой машине он стоит в `%LOCALAPPDATA%\Programs\Inno Setup 7`.

Результат — `publish\installer\MeowsCut-<версия>-setup.exe`, около 137 МБ:
папка на 463 МБ сжимается почти вчетверо, потому что основную её часть
составляют ffmpeg и ffprobe. Установщик ставит приложение в профиль
пользователя и не требует прав администратора.

## Ловушки окружения

- **PowerShell 5.1 портит UTF-8**: `Get-Content` без `-Encoding UTF8` читает файл как ANSI,
  и обратная запись превращает кириллицу в кракозябры. Для правки исходников
  использовать редактор, а не конвейер `Get-Content | Set-Content`.
- Логи пишутся в UTF-8 **с BOM** — иначе `Get-Content` и Блокнот показывают мусор.
- **Скрипты `.ps1` с русским текстом тоже нужны с BOM**: Windows PowerShell 5.1
  без него читает файл как ANSI и падает на разборе строк.

## Скрипты проекта (появятся по мере реализации)

| Скрипт | Этап | Назначение |
|---|---|---|
| `tools/get-ffmpeg.ps1` | M1 | скачать и разложить ffmpeg в `%LOCALAPPDATA%\MeowsCut\ffmpeg` |
| `tools/publish-portable.ps1` | M8 | `dotnet publish` + копирование ffmpeg + сборка zip-архива |
| `tools/build-installer.ps1` | M10 | portable-папка + Inno Setup = установщик exe |
| `tools/make-testclips.ps1` | M3 | генерация тестовых клипов (`testsrc`, `sine`) для интеграционных тестов |
