# 10. Окружение разработки

## Что установлено (2026-09-11, эта машина)

| Компонент | Версия | Где лежит |
|---|---|---|
| .NET SDK | 8.0.101, **8.0.118** | `C:\Program Files\dotnet` (системная установка) |
| .NET runtime | 8.0.18 + WindowsDesktop 8.0.18 | `C:\Program Files\dotnet` |
| FFmpeg / FFprobe | BtbN `win64-gpl`, N-126492 (2026-09-10) | `%LOCALAPPDATA%\MeowsCut\ffmpeg` |
| Inno Setup | 7.1.0 | `%LOCALAPPDATA%\Programs\Inno Setup 7` |
| Git | 2.x | `C:\Program Files\Git` |

Отдельной per-user установки SDK в `%LOCALAPPDATA%\Microsoft\dotnet` нет, особых переменных
окружения проект не требует — хватает системного `dotnet` из `PATH`.

`global.json` намеренно не привязан к feature band 8.0.4xx:

```json
{ "sdk": { "rollForward": "latestMinor", "version": "8.0.100" } }
```

Требование `8.0.425` с `rollForward: latestFeature` означало бы, что на машине с любым
другим band (скажем, 8.0.1xx) сборка падает с «A compatible .NET SDK was not found» —
хотя на нём всё собирается без единого предупреждения. Достаточно любого SDK 8.0.

## Проверка

```powershell
dotnet --version                       # 8.0.118
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
# .NET 8 SDK (per-user, без администратора) — если системного ещё нет
curl.exe -sSL -o dotnet-install.ps1 https://dot.net/v1/dotnet-install.ps1
./dotnet-install.ps1 -Channel 8.0 -Quality GA -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"

# FFmpeg
./tools/get-ffmpeg.ps1
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

## Скрипты проекта

| Скрипт | Этап | Назначение |
|---|---|---|
| `tools/get-ffmpeg.ps1` | M1 | скачать и разложить ffmpeg в `%LOCALAPPDATA%\MeowsCut\ffmpeg` |
| `tools/publish-portable.ps1` | M8 | `dotnet publish` + копирование ffmpeg + сборка zip-архива |
| `tools/build-installer.ps1` | M10 | portable-папка + Inno Setup = установщик exe |
| `tools/make-testclips.ps1` | M3 | генерация тестовых клипов (`testsrc`, `sine`) для интеграционных тестов |
