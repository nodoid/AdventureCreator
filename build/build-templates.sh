#!/usr/bin/env bash
# Builds the player templates used by Studio › Export for fast, SDK-free exports:
#   artifacts/templates/console/<rid>/adventure-player[.exe]   single-file console players
#   artifacts/templates/Adventure Player.app                    macOS graphical player (when run on a Mac)
#   artifacts/templates/windows/                                Windows graphical player (when run on Windows)
set -euo pipefail
cd "$(dirname "$0")/.."
OUT=artifacts/templates
mkdir -p "$OUT/console"

for rid in ${RIDS:-osx-arm64 osx-x64 win-x64 linux-x64}; do
  echo "== console player $rid"
  dotnet publish src/AdventureSystem.ConsolePlayer -c Release -r "$rid" --self-contained \
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none \
    -o "$OUT/console/$rid"
done

case "$(uname -s)" in
  Darwin)
    echo "== macOS graphical player"
    dotnet publish src/AdventureSystem.Player -f net10.0-maccatalyst -c Release -p:CreatePackage=false
    # The universal (arm64 + x64) bundle is produced in the bin folder rather than the publish folder.
    APP="src/AdventureSystem.Player/bin/Release/net10.0-maccatalyst/Adventure Player.app"
    rm -rf "$OUT/Adventure Player.app"
    cp -R "$APP" "$OUT/Adventure Player.app"
    ;;
  MINGW*|MSYS*|CYGWIN*)
    echo "== Windows graphical player"
    dotnet publish src/AdventureSystem.Player -f net10.0-windows10.0.19041.0 -c Release \
      -p:WindowsPackageType=None -p:SelfContained=true -p:WindowsAppSDKSelfContained=true -o "$OUT/windows"
    ;;
esac
echo "Templates are in $OUT"
