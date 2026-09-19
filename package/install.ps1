# Dimraeth Minimap installer. Finds the game through Steam and copies the files from .\payload into it.
# Started by install.bat. Works in Windows PowerShell 5.1. Needs no admin rights and changes nothing outside the game folder.
param(
    [string]$GameDir,      # skip detection and install here
    [switch]$DetectOnly    # only report where the game was found
)

$ErrorActionPreference = 'Stop'
$AppId = '2402680'
$ExeName = 'Dimraeth.exe'
$payload = Join-Path $PSScriptRoot 'payload'

function Find-SteamRoots {
    $roots = @()
    foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
        try {
            $p = Get-ItemProperty -Path $key -ErrorAction Stop
            foreach ($name in 'SteamPath', 'InstallPath') { if ($p.$name) { $roots += ($p.$name -replace '/', '\') } }
        } catch { }
    }
    $roots += "${env:ProgramFiles(x86)}\Steam", "$env:ProgramFiles\Steam"
    $roots | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique
}

function Find-SteamLibraries {
    # Steam lists every library folder (any drive) in libraryfolders.vdf.
    $libraries = @()
    foreach ($root in Find-SteamRoots) {
        $libraries += $root
        foreach ($vdf in (Join-Path $root 'steamapps\libraryfolders.vdf'), (Join-Path $root 'config\libraryfolders.vdf')) {
            if (-not (Test-Path -LiteralPath $vdf)) { continue }
            foreach ($line in Get-Content -LiteralPath $vdf) {
                if ($line -match '^\s*"path"\s+"(.+)"\s*$') { $libraries += ($Matches[1] -replace '\\\\', '\') }
            }
        }
    }
    $libraries | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique
}

function Find-Game {
    foreach ($library in Find-SteamLibraries) {
        $manifest = Join-Path $library "steamapps\appmanifest_$AppId.acf"
        if (-not (Test-Path -LiteralPath $manifest)) { continue }
        $dirName = 'Dimraeth'
        foreach ($line in Get-Content -LiteralPath $manifest) {
            if ($line -match '^\s*"installdir"\s+"(.+)"\s*$') { $dirName = $Matches[1] }
        }
        $candidate = Join-Path $library "steamapps\common\$dirName"
        if (Test-Path -LiteralPath (Join-Path $candidate $ExeName)) { return $candidate }
    }
    return $null
}

function Test-GameDir([string]$dir) {
    return $dir -and (Test-Path -LiteralPath (Join-Path $dir $ExeName))
}

Write-Host ''
Write-Host '=== Dimraeth Minimap 설치 ===' -ForegroundColor Cyan
Write-Host ''

if (-not $GameDir) { $GameDir = Find-Game }

if ($DetectOnly) {
    if ($GameDir) { Write-Host "게임 폴더: $GameDir" } else { Write-Host '게임 폴더를 찾지 못했습니다.' }
    exit 0
}

if (Test-GameDir $GameDir) {
    Write-Host "게임 폴더를 찾았습니다:" -ForegroundColor Green
    Write-Host "  $GameDir"
} else {
    Write-Host 'Steam에서 Dimraeth를 자동으로 찾지 못했습니다.' -ForegroundColor Yellow
    Write-Host 'Steam 라이브러리에서 Dimraeth 우클릭 > 관리 > 로컬 파일 보기 로 연 폴더의 주소를 붙여 넣어 주세요.'
    while ($true) {
        $answer = (Read-Host '게임 폴더 (그냥 Enter = 취소)').Trim().Trim('"')
        if (-not $answer) { Write-Host '취소했습니다. 아무것도 바꾸지 않았습니다.'; exit 1 }
        if (Test-GameDir $answer) { $GameDir = $answer; break }
        Write-Host "그 폴더에 $ExeName 가 없습니다. 다시 확인해 주세요." -ForegroundColor Yellow
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $payload 'winhttp.dll'))) {
    Write-Host ''
    Write-Host 'payload 폴더를 찾지 못했습니다. zip 을 통째로 푼 뒤 install.bat 을 실행해 주세요.' -ForegroundColor Red
    Write-Host '(압축 프로그램 안에서 바로 실행하면 안 됩니다.)'
    exit 1
}

while (Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($ExeName)) -ErrorAction SilentlyContinue) {
    Write-Host ''
    Write-Host '게임이 켜져 있습니다. 게임을 끈 뒤 Enter 를 눌러 주세요.' -ForegroundColor Yellow
    [void](Read-Host)
}

$firstInstall = -not (Test-Path -LiteralPath (Join-Path $GameDir 'BepInEx\core'))
Write-Host ''
Write-Host '파일을 복사하는 중...'
# Settings the player already changed live in BepInEx\config, which the payload does not contain, so they survive updates.
Copy-Item -Path (Join-Path $payload '*') -Destination $GameDir -Recurse -Force

$plugin = Join-Path $GameDir 'BepInEx\plugins\DimraethMinimap\DimraethMinimap.dll'
if (-not (Test-Path -LiteralPath $plugin)) { Write-Host '복사에 실패했습니다.' -ForegroundColor Red; exit 1 }
$version = (Get-Item -LiteralPath $plugin).VersionInfo.FileVersion

Write-Host ''
Write-Host "설치 완료! (미니맵 $version)" -ForegroundColor Green
if ($firstInstall) {
    Write-Host ''
    Write-Host '처음 설치했으므로 다음 게임 실행은 1~3분 걸리고 인터넷 연결이 필요합니다.'
    Write-Host '(모드 로더가 게임을 분석하는 과정이며, 그 다음부터는 평소와 같습니다.)'
}
Write-Host ''
Write-Host '조작: F7 켜기/끄기, PageUp/PageDown 확대/축소'
Write-Host "제거: 게임 폴더의 uninstall-minimap.bat 실행"
Write-Host ''
exit 0
