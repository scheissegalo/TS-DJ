param(
    [Parameter(Mandatory = $true)]
    [string] $PublishDir,

    [string] $Version = ""
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$VersionsFile = Join-Path $ScriptDir "versions.env"

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $env:YTDLP_VERSION
}

if ([string]::IsNullOrWhiteSpace($Version) -and (Test-Path $VersionsFile)) {
    Get-Content $VersionsFile | ForEach-Object {
        if ($_ -match '^\s*YTDLP_VERSION=(.+)\s*$') {
            $Version = $Matches[1].Trim()
        }
    }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "yt-dlp version not specified. Pass -Version or set YTDLP_VERSION in versions.env."
}

$PublishDir = (Resolve-Path $PublishDir).Path
$DestDir = Join-Path $PublishDir "tools\yt-dlp\win-x64"
New-Item -ItemType Directory -Path $DestDir -Force | Out-Null

$Url = "https://github.com/yt-dlp/yt-dlp/releases/download/$Version/yt-dlp.exe"
$DestExe = Join-Path $DestDir "yt-dlp.exe"

Write-Host "Downloading yt-dlp $Version..."
Invoke-WebRequest -Uri $Url -OutFile $DestExe -UseBasicParsing

if (-not (Test-Path $DestExe)) {
    throw "Download failed: $DestExe not created."
}

$VersionOutput = & $DestExe --version
Write-Host "Verified yt-dlp: $VersionOutput"

Write-Host "Installed yt-dlp to $DestExe"
