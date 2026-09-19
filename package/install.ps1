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

# Korean on a Korean Windows, English everywhere else. The display language alone is not enough:
# some hosts start PowerShell with an en-US UI culture on an otherwise Korean system.
$Korean = @((Get-UICulture).Name, (Get-Culture).Name, [Globalization.CultureInfo]::InstalledUICulture.Name) -like 'ko*'
$Korean = [bool]$Korean
function T([string]$ko, [string]$en) { if ($Korean) { $ko } else { $en } }

function Test-GameDir([string]$dir) {
    return $dir -and (Test-Path -LiteralPath (Join-Path $dir $ExeName))
}

Write-Host ''
Write-Host (T '=== Dimraeth Minimap 설치 ===' '=== Dimraeth Minimap installer ===') -ForegroundColor Cyan
Write-Host ''

if (-not $GameDir) { $GameDir = Find-Game }

if ($DetectOnly) {
    if ($GameDir) { Write-Host ((T '게임 폴더: ' 'Game folder: ') + $GameDir) } else { Write-Host (T '게임 폴더를 찾지 못했습니다.' 'Game folder not found.') }
    exit 0
}

if (Test-GameDir $GameDir) {
    Write-Host (T '게임 폴더를 찾았습니다:' 'Found the game folder:') -ForegroundColor Green
    Write-Host "  $GameDir"
} else {
    Write-Host (T 'Steam에서 Dimraeth를 자동으로 찾지 못했습니다.' 'Could not find Dimraeth through Steam automatically.') -ForegroundColor Yellow
    Write-Host (T 'Steam 라이브러리에서 Dimraeth 우클릭 > 관리 > 로컬 파일 보기 로 연 폴더의 주소를 붙여 넣어 주세요.' 'In your Steam library: right-click Dimraeth > Manage > Browse local files, then paste that folder path here.')
    while ($true) {
        $answer = (Read-Host (T '게임 폴더 (그냥 Enter = 취소)' 'Game folder (just Enter = cancel)')).Trim().Trim('"')
        if (-not $answer) { Write-Host (T '취소했습니다. 아무것도 바꾸지 않았습니다.' 'Cancelled. Nothing was changed.'); exit 1 }
        if (Test-GameDir $answer) { $GameDir = $answer; break }
        Write-Host (T "그 폴더에 $ExeName 가 없습니다. 다시 확인해 주세요." "There is no $ExeName in that folder. Please check again.") -ForegroundColor Yellow
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $payload 'winhttp.dll'))) {
    Write-Host ''
    Write-Host (T 'payload 폴더를 찾지 못했습니다. zip 을 통째로 푼 뒤 install.bat 을 실행해 주세요.' 'The payload folder is missing. Extract the whole zip first, then run install.bat.') -ForegroundColor Red
    Write-Host (T '(압축 프로그램 안에서 바로 실행하면 안 됩니다.)' '(Do not run it from inside the archive viewer.)')
    exit 1
}

$instantReturns = 0
while (Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($ExeName)) -ErrorAction SilentlyContinue) {
    Write-Host ''
    Write-Host (T '게임이 켜져 있습니다. 게임을 끈 뒤 Enter 를 눌러 주세요.' 'The game is running. Close it, then press Enter.') -ForegroundColor Yellow
    $asked = Get-Date
    [void](Read-Host)
    # Without a real console Read-Host returns at once; do not spin forever then.
    if (((Get-Date) - $asked).TotalMilliseconds -lt 200) { $instantReturns++ } else { $instantReturns = 0 }
    if ($instantReturns -ge 3) {
        Write-Host (T '게임이 아직 켜져 있어 설치를 중단했습니다. 아무것도 바꾸지 않았습니다.' 'The game is still running, so nothing was installed.') -ForegroundColor Red
        exit 1
    }
}

$firstInstall = -not (Test-Path -LiteralPath (Join-Path $GameDir 'BepInEx\core'))
Write-Host ''
Write-Host (T '파일을 복사하는 중...' 'Copying files...')
# Settings the player already changed live in BepInEx\config, which the payload does not contain, so they survive updates.
Copy-Item -Path (Join-Path $payload '*') -Destination $GameDir -Recurse -Force

$plugin = Join-Path $GameDir 'BepInEx\plugins\DimraethMinimap\DimraethMinimap.dll'
if (-not (Test-Path -LiteralPath $plugin)) { Write-Host (T '복사에 실패했습니다.' 'Copying failed.') -ForegroundColor Red; exit 1 }
$version = (Get-Item -LiteralPath $plugin).VersionInfo.FileVersion

Write-Host ''
Write-Host (T "설치 완료! (미니맵 $version)" "Installed! (minimap $version)") -ForegroundColor Green
if ($firstInstall) {
    Write-Host ''
    Write-Host (T '처음 설치했으므로 다음 게임 실행은 1~3분 걸리고 인터넷 연결이 필요합니다.' 'First install: the next game launch takes 1-3 minutes and needs an internet connection.')
    Write-Host (T '(모드 로더가 게임을 분석하는 과정이며, 그 다음부터는 평소와 같습니다.)' '(The mod loader analyses the game once; after that, launches are normal.)')
}
Write-Host ''
Write-Host (T '조작: F7 설정 창, F8 미니맵 켜기/끄기, PageUp/PageDown 확대/축소' 'Keys: F7 settings, F8 show/hide minimap, PageUp/PageDown zoom')
Write-Host (T '제거: 게임 폴더의 uninstall-minimap.bat 실행' 'Uninstall: run uninstall-minimap.bat in the game folder')
Write-Host ''
exit 0
