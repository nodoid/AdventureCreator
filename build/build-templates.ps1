# Builds the player templates used by Studio › Export for fast, SDK-free exports (Windows version of build-templates.sh):
#   artifacts\templates\windows\                              Windows graphical player (folder with AdventureCreator.Player.exe)
#   artifacts\templates\console\<rid>\adventure-player[.exe]   single-file console players
param([string[]] $Rids = @('win-x64', 'win-arm64', 'osx-arm64', 'osx-x64', 'linux-x64'))
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
$out = 'artifacts\templates'
New-Item -ItemType Directory -Force "$out\console" | Out-Null

# The graphical player is built first: runtime-specific console publishes restore the shared Core library for one
# runtime only, which a MAUI build afterwards can't use. A self-contained Windows App SDK app needs an architecture;
# RuntimeIdentifierOverride (not -r) keeps it from flowing into the Android/iOS targets of referenced projects.
Write-Host "== Windows graphical player"
$arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
dotnet publish src\AdventureCreator.Player -f net10.0-windows10.0.19041.0 -c Release -p:RuntimeIdentifierOverride=$arch `
    -p:StandaloneWindows=true -o "$out\windows"
if ($LASTEXITCODE -ne 0) { throw "Windows player failed" }

foreach ($rid in $Rids) {
    Write-Host "== console player $rid"
    dotnet publish src\AdventureCreator.ConsolePlayer -c Release -r $rid --self-contained `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o "$out\console\$rid"
    if ($LASTEXITCODE -ne 0) { throw "console player $rid failed" }
}
Write-Host "Templates are in $out"
