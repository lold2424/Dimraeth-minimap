# Builds the plugin and assembles the release zips in dist\.
#   dist\DimraethMinimap-vX.Y.Z.zip         install.bat + payload\ (BepInEx + plugin + readme + uninstaller).
#                                           Players extract it anywhere and double-click install.bat, which
#                                           finds the game through Steam. Same zip for first install and updates.
#   dist\DimraethMinimap-vX.Y.Z-update.zip  plugin DLL only, for manual updates
# Nothing from the game is packaged: only BepInEx (LGPL-2.1) and our own DLL.
# Works in Windows PowerShell 5.1.
param(
    [string]$GameDir = 'D:\SteamLibrary\steamapps\common\Dimraeth'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Split-Path -Parent $PSScriptRoot

# Pinned BepInEx build, verified by hash so a release never silently picks up something else.
$bepName   = 'BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788.zip'
$bepUrl    = 'https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip'
$bepSha256 = 'F4CC496BD098A0DF4164B81E3737297707F13A47C2478DBA2F60EEFAB784817A'
$bepLicenseUrl = 'https://raw.githubusercontent.com/BepInEx/BepInEx/master/LICENSE'

$csproj  = Join-Path $root 'src\DimraethMinimap\DimraethMinimap.csproj'
$version = ([xml](Get-Content $csproj)).SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if (-not $version) { throw 'Version not found in csproj' }

Write-Host "== Building v$version"
dotnet build $csproj -c Release -nologo -v q "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$dll = Join-Path $root 'src\DimraethMinimap\bin\Release\net6.0\DimraethMinimap.dll'

$vendor = Join-Path $root 'vendor'
New-Item -ItemType Directory -Force $vendor | Out-Null
$bepZip = Join-Path $vendor $bepName
if (-not (Test-Path $bepZip)) {
    Write-Host '== Downloading BepInEx'
    Invoke-WebRequest -Uri $bepUrl -OutFile $bepZip -UseBasicParsing
}
$hash = (Get-FileHash $bepZip -Algorithm SHA256).Hash
if ($hash -ne $bepSha256) { throw "BepInEx zip hash mismatch: $hash" }

$bepLicense = Join-Path $vendor 'BepInEx-LICENSE.txt'
if (-not (Test-Path $bepLicense)) {
    Invoke-WebRequest -Uri $bepLicenseUrl -OutFile $bepLicense -UseBasicParsing
}

$dist  = Join-Path $root 'dist'
$stage = Join-Path $dist 'stage'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

function Add-Plugin([string]$target) {
    $dir = Join-Path $target 'BepInEx\plugins\DimraethMinimap'
    New-Item -ItemType Directory -Force $dir | Out-Null
    Copy-Item $dll $dir
}

Write-Host '== Staging installer package'
$package = Join-Path $stage 'full'
$full = Join-Path $package 'payload'   # everything in here ends up in the game folder
New-Item -ItemType Directory -Force $package | Out-Null
Expand-Archive -Path $bepZip -DestinationPath $full
Add-Plugin $full
Copy-Item (Join-Path $root 'package\uninstall-minimap.bat') $full
Copy-Item (Join-Path $root 'package\install.bat') $package
# Windows PowerShell 5.1 and old Notepad need a BOM to read Korean correctly.
$bom = New-Object Text.UTF8Encoding $true
[IO.File]::WriteAllText((Join-Path $package 'install.ps1'), [IO.File]::ReadAllText((Join-Path $root 'package\install.ps1')), $bom)
$readme = [IO.File]::ReadAllText((Join-Path $root 'package\README-minimap.txt'))
[IO.File]::WriteAllText((Join-Path $full 'README-minimap.txt'), $readme, $bom)
[IO.File]::WriteAllText((Join-Path $package 'README-minimap.txt'), $readme, $bom)
$lic = Join-Path $full 'licenses-minimap'
New-Item -ItemType Directory -Force $lic | Out-Null
Copy-Item $bepLicense (Join-Path $lic 'BepInEx-LICENSE.txt')
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $lic 'DimraethMinimap-LICENSE.txt')

# cmd.exe can misparse batch files with Unix line endings, whatever git checked out: ship them as CRLF.
Get-ChildItem $package -Recurse -Filter *.bat | ForEach-Object {
    $text = [IO.File]::ReadAllText($_.FullName) -replace "`r?`n", "`r`n"
    [IO.File]::WriteAllText($_.FullName, $text, (New-Object Text.ASCIIEncoding))
}

# Guard: refuse to ship anything that came from the game.
$forbidden = Get-ChildItem $package -Recurse -File | Where-Object {
    $_.FullName -match '\\interop\\' -or $_.Name -match '^(Assembly-CSharp|GameAssembly|UnityPlayer|global-metadata)'
}
if ($forbidden) { throw "Game-derived files in package: $($forbidden.Name -join ', ')" }

Write-Host '== Staging update package'
$update = Join-Path $stage 'update'
Add-Plugin $update

$fullZip   = Join-Path $dist "DimraethMinimap-v$version.zip"
$updateZip = Join-Path $dist "DimraethMinimap-v$version-update.zip"
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $fullZip
Compress-Archive -Path (Join-Path $update '*') -DestinationPath $updateZip
Remove-Item $stage -Recurse -Force

Get-ChildItem $dist | ForEach-Object { '{0}  {1:N1} MB' -f $_.Name, ($_.Length / 1MB) }
