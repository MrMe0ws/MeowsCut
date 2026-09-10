<#
.SYNOPSIS
    Собирает portable-папку Meows Cut со встроенным FFmpeg.

.DESCRIPTION
    Результат — папка, которую достаточно скопировать на другой компьютер
    с Windows 10 x64: .NET устанавливать не нужно (сборка self-contained),
    FFmpeg лежит рядом с exe и находится вторым шагом поиска локатора.

    Бинарники FFmpeg берутся из папки разработки (%LOCALAPPDATA%\MeowsCut\ffmpeg);
    в репозитории они не хранятся — см. docs/10-DEV-SETUP.md.

.PARAMETER Output
    Куда положить сборку. По умолчанию publish\MeowsCut рядом с решением.

.PARAMETER FfmpegDirectory
    Откуда взять ffmpeg.exe и ffprobe.exe.

.PARAMETER Zip
    Дополнительно упаковать результат в zip-архив.

.PARAMETER StopRunning
    Закрыть запущенную из папки сборки Meows Cut вместо остановки с ошибкой.
    Без этого ключа скрипт чужие процессы не трогает.

.EXAMPLE
    ./tools/publish-portable.ps1 -Zip

.EXAMPLE
    ./tools/publish-portable.ps1 -StopRunning
#>
[CmdletBinding()]
param(
    [string] $Output,
    [string] $FfmpegDirectory = (Join-Path $env:LOCALAPPDATA 'MeowsCut\ffmpeg'),
    [switch] $Zip,
    [switch] $StopRunning
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not $Output) { $Output = Join-Path $root 'publish\MeowsCut' }

$project = Join-Path $root 'src\MeowsCut.App\MeowsCut.App.csproj'
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

# FFmpeg проверяем до сборки: без него portable-папка бессмысленна
$ffmpeg = Join-Path $FfmpegDirectory 'ffmpeg.exe'
$ffprobe = Join-Path $FfmpegDirectory 'ffprobe.exe'

if (-not (Test-Path $ffmpeg) -or -not (Test-Path $ffprobe)) {
    throw "В $FfmpegDirectory нет ffmpeg.exe и ffprobe.exe. Запустите tools/get-ffmpeg.ps1"
}

# Запущенная из этой же папки сборка держит свои файлы, и удалить их нельзя.
# Без явной проверки это выглядело отказом в доступе к случайной библиотеке
# вроде PresentationCore.resources.dll — по такому сообщению причину не найти.
$running = @(Get-Process -Name 'MeowsCut' -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and $_.Path.StartsWith($Output, [System.StringComparison]::OrdinalIgnoreCase)
})

if ($running.Count -gt 0) {
    if (-not $StopRunning) {
        $list = ($running | ForEach-Object { "PID $($_.Id)" }) -join ', '
        throw "Meows Cut запущен из $Output ($list) и держит свои файлы. Закройте приложение и повторите — или запустите скрипт с ключом -StopRunning."
    }

    Write-Host "Закрываю запущенную сборку в $Output"
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

if (Test-Path $Output) {
    Write-Host "Очищаю $Output"

    try {
        Remove-Item -Recurse -Force $Output -ErrorAction Stop
    }
    catch [System.UnauthorizedAccessException] {
        # Держит не сам MeowsCut: проводник с открытым предпросмотром, антивирус,
        # синхронизация OneDrive. Имя файла из исключения — единственная зацепка.
        throw "Не удалось очистить $Output — файл занят другой программой: $($_.Exception.Message)"
    }
}

Write-Host 'Собираю приложение (self-contained, win-x64)…'

& $dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    --output $Output

if ($LASTEXITCODE -ne 0) { throw "dotnet publish завершился с кодом $LASTEXITCODE" }

# FFmpeg рядом с приложением — основной путь поиска в portable-поставке
$bundled = Join-Path $Output 'ffmpeg'
New-Item -ItemType Directory -Force $bundled | Out-Null

Copy-Item $ffmpeg $bundled -Force
Copy-Item $ffprobe $bundled -Force

# Лицензия сборки FFmpeg обязана лежать рядом: она распространяется под GPL
$license = Join-Path $FfmpegDirectory 'LICENSE.txt'
if (Test-Path $license) { Copy-Item $license $bundled -Force }

$readme = @'
Meows Cut — портативная сборка

Как запустить: MeowsCut.exe. Устанавливать ничего не нужно.

Что внутри:
  MeowsCut.exe   — само приложение
  ffmpeg\        — FFmpeg и FFprobe (лицензия GPL, файл LICENSE.txt рядом)

Настройки, логи и кэш кадров хранятся в профиле пользователя:
  %APPDATA%\MeowsCut       — настройки и пользовательские пресеты
  %LOCALAPPDATA%\MeowsCut  — логи, кэш кадров, временные файлы

Свои пресеты (в том числе с изменёнными лимитами Telegram) кладите
в %APPDATA%\MeowsCut\presets — они перекрывают встроенные.
'@

Set-Content -Path (Join-Path $Output 'ПРОЧТИ МЕНЯ.txt') -Value $readme -Encoding UTF8

$size = (Get-ChildItem -Recurse $Output | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ("Готово: {0} ({1:N0} МБ)" -f $Output, $size)

if ($Zip) {
    $archive = "$Output.zip"
    if (Test-Path $archive) { Remove-Item -Force $archive }

    Write-Host 'Упаковываю в zip…'
    Compress-Archive -Path (Join-Path $Output '*') -DestinationPath $archive
    Write-Host "Архив: $archive"
}
