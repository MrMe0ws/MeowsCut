<#
.SYNOPSIS
    Собирает установщик Meows Cut (exe).

.DESCRIPTION
    Сначала делается portable-папка тем же скриптом, что и для zip-поставки,
    потом она упаковывается компилятором Inno Setup. Установщик ставится
    в профиль пользователя и не требует прав администратора — редактору видео
    они не нужны, а у пользователя их может не быть.

    Inno Setup нужен отдельно: https://jrsoftware.org/isdl.php
    Ставится тихо: innosetup-*.exe /VERYSILENT /CURRENTUSER /SP-

.PARAMETER Version
    Версия в имени файла и в списке установленных программ.

.PARAMETER Iscc
    Путь к ISCC.exe, если он не в стандартном месте.

.EXAMPLE
    ./tools/build-installer.ps1 -Version 1.0.0
#>
[CmdletBinding()]
param(
    [string] $Version = '1.0.0',
    [string] $Iscc,
    [string] $FfmpegDirectory = (Join-Path $env:LOCALAPPDATA 'MeowsCut\ffmpeg')
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$portable = Join-Path $root 'publish\MeowsCut'
$outputDir = Join-Path $root 'publish\installer'
$script = Join-Path $PSScriptRoot 'installer\MeowsCut.iss'

if (-not $Iscc) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe')
    )

    $Iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $Iscc -or -not (Test-Path $Iscc)) {
    throw 'Не найден ISCC.exe. Установите Inno Setup: https://jrsoftware.org/isdl.php'
}

# Portable-папка — вход для установщика, поэтому собирается тем же скриптом.
& (Join-Path $PSScriptRoot 'publish-portable.ps1') -FfmpegDirectory $FfmpegDirectory

if (-not (Test-Path $portable)) {
    throw "Не найдена собранная папка $portable"
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

Write-Host 'Собираю установщик…'

& $Iscc "/DSourceDir=$portable" "/DAppVersion=$Version" "/O$outputDir" $script
if ($LASTEXITCODE -ne 0) { throw "ISCC завершился с кодом $LASTEXITCODE" }

$setup = Get-ChildItem $outputDir -Filter '*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$size = [math]::Round($setup.Length / 1MB, 1)

Write-Host ''
Write-Host "Готово: $($setup.FullName) ($size МБ)"
Write-Host 'Ставится без прав администратора, в профиль пользователя.'
