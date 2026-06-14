param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $PublishDir,

    [string] $OutDir = "artifacts",

    [string] $LibOpusSource = ""
)

$ErrorActionPreference = "Stop"

$PublishDir = Resolve-Path $PublishDir
$OutDir = New-Item -ItemType Directory -Force -Path $OutDir | Select-Object -ExpandProperty FullName
$StageName = "TS-DJ-$Version-win-x64"
$Stage = Join-Path $OutDir $StageName
$Zip = Join-Path $OutDir "TS-DJ-$Version-win-x64.zip"

if (Test-Path $Stage) {
    Remove-Item -Recurse -Force $Stage
}
if (Test-Path $Zip) {
    Remove-Item -Force $Zip
}

New-Item -ItemType Directory -Path $Stage | Out-Null
Copy-Item -Path (Join-Path $PublishDir "*") -Destination $Stage -Recurse -Force

$libDir = Join-Path $Stage "lib\x64"
New-Item -ItemType Directory -Path $libDir -Force | Out-Null

$opusDest = Join-Path $libDir "libopus.dll"
if ($LibOpusSource -and (Test-Path $LibOpusSource)) {
    Copy-Item -Path $LibOpusSource -Destination $opusDest -Force
} else {
    $candidates = @(
        "C:\msys64\mingw64\bin\libopus-0.dll",
        "C:\msys64\mingw64\bin\libopus.dll"
    )
    $found = $false
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            Copy-Item -Path $candidate -Destination $opusDest -Force
            $found = $true
            break
        }
    }
    if (-not $found) {
        throw "libopus DLL not found. Pass -LibOpusSource or install mingw-w64-x86_64-opus via MSYS2."
    }
}

@'
TS-DJ — Windows x64

Requirements:
  - .NET 8 runtime (https://dotnet.microsoft.com/download/dotnet/8.0)

Run:
  TS-DJ.App.exe

libopus.dll is included under lib\x64\ for Opus voice encoding.
'@ | Set-Content -Path (Join-Path $Stage "INSTALL-windows.txt") -Encoding UTF8

Compress-Archive -Path $Stage -DestinationPath $Zip -Force
Write-Host "Created $Zip"
