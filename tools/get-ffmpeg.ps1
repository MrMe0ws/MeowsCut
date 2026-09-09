<#
.SYNOPSIS
    Скачивает ffmpeg.exe и ffprobe.exe для разработки.

.DESCRIPTION
    Бинарники не хранятся в репозитории: статическая сборка занимает ~330 МБ, а проект
    лежит в синхронизируемой папке OneDrive. Скрипт кладёт их в %LOCALAPPDATA%\MeowsCut\ffmpeg,
    где их находит FfmpegToolsetLocator (третий шаг поиска).

.PARAMETER Destination
    Куда положить бинарники. По умолчанию — папка для разработки.

.PARAMETER Force
    Перекачать, даже если файлы уже на месте.

.EXAMPLE
    ./tools/get-ffmpeg.ps1
#>
[CmdletBinding()]
param(
    [string] $Destination = (Join-Path $env:LOCALAPPDATA 'MeowsCut\ffmpeg'),
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

# GPL-сборка: x264, x265, libvpx-vp9, SVT-AV1, libaom, opus, lame, vorbis + nvenc/qsv/amf
$url = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip'

$ffmpegPath = Join-Path $Destination 'ffmpeg.exe'
$ffprobePath = Join-Path $Destination 'ffprobe.exe'

if (-not $Force -and (Test-Path $ffmpegPath) -and (Test-Path $ffprobePath)) {
    Write-Host "FFmpeg уже на месте: $Destination"
    & $ffmpegPath -hide_banner -version | Select-Object -First 1
    return
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("meowscut-ffmpeg-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null

try {
    $zip = Join-Path $work 'ffmpeg.zip'
    Write-Host "Скачиваю $url"
    curl.exe -L --fail --retry 2 -o $zip $url
    if ($LASTEXITCODE -ne 0) { throw "Не удалось скачать архив (код $LASTEXITCODE)" }

    Write-Host 'Распаковываю…'
    Expand-Archive -Path $zip -DestinationPath $work -Force

    $binDirectory = (Get-ChildItem -Recurse -Filter 'ffmpeg.exe' $work | Select-Object -First 1).DirectoryName
    if (-not $binDirectory) { throw 'В архиве не найден ffmpeg.exe' }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item (Join-Path $binDirectory 'ffmpeg.exe') $Destination -Force
    Copy-Item (Join-Path $binDirectory 'ffprobe.exe') $Destination -Force

    # Лицензия обязана лежать рядом: сборка распространяется под GPL
    $license = Get-ChildItem -Recurse -Include 'LICENSE*' $work | Select-Object -First 1
    if ($license) { Copy-Item $license.FullName $Destination -Force }

    Write-Host "Готово: $Destination"
    & $ffmpegPath -hide_banner -version | Select-Object -First 1
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
