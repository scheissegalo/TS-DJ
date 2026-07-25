param(
    [Parameter(Mandatory = $true)]
    [string] $PublishDir,

    [string] $Build = ""
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$VersionsFile = Join-Path $ScriptDir "versions.env"

if ([string]::IsNullOrWhiteSpace($Build)) {
    $Build = $env:FFMPEG_BUILD
}

if ([string]::IsNullOrWhiteSpace($Build) -and (Test-Path $VersionsFile)) {
    Get-Content $VersionsFile | ForEach-Object {
        if ($_ -match '^\s*FFMPEG_BUILD=(.+)\s*$') {
            $Build = $Matches[1].Trim()
        }
    }
}

if ([string]::IsNullOrWhiteSpace($Build)) {
    $Build = "latest"
}

$PublishDir = (Resolve-Path $PublishDir).Path
$DestDir = Join-Path $PublishDir "tools\ffmpeg\win-x64"
New-Item -ItemType Directory -Path $DestDir -Force | Out-Null

$ZipName = "ffmpeg-master-$Build-win64-gpl.zip"
$Url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/$Build/$ZipName"
$TempZip = Join-Path ([System.IO.Path]::GetTempPath()) "ts-dj-ffmpeg-$Build.zip"
$TempExtract = Join-Path ([System.IO.Path]::GetTempPath()) "ts-dj-ffmpeg-extract"

if (Test-Path $TempExtract) {
    Remove-Item -Recurse -Force $TempExtract
}

Write-Host "Downloading ffmpeg ($Build)..."
Invoke-WebRequest -Uri $Url -OutFile $TempZip -UseBasicParsing

Write-Host "Extracting ffmpeg..."
Expand-Archive -Path $TempZip -DestinationPath $TempExtract -Force

$FfmpegExe = Get-ChildItem -Path $TempExtract -Recurse -Filter "ffmpeg.exe" |
    Where-Object { $_.FullName -match '[\\/]bin[\\/]ffmpeg\.exe$' } |
    Select-Object -First 1

if (-not $FfmpegExe) {
    throw "ffmpeg.exe not found in downloaded archive."
}

$DestExe = Join-Path $DestDir "ffmpeg.exe"
Copy-Item -Path $FfmpegExe.FullName -Destination $DestExe -Force

$LicenseFile = Get-ChildItem -Path $TempExtract -Recurse -Filter "LICENSE.txt" | Select-Object -First 1
if ($LicenseFile) {
    Copy-Item -Path $LicenseFile.FullName -Destination (Join-Path $DestDir "LICENSE.txt") -Force
}

Remove-Item -Force $TempZip
Remove-Item -Recurse -Force $TempExtract

$VersionOutput = & $DestExe -version 2>&1 | Select-Object -First 1
Write-Host "Verified ffmpeg: $VersionOutput"

Write-Host "Installed ffmpeg to $DestExe"
