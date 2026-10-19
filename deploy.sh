#!/usr/bin/env bash
# Build the assembly and copy the mod into the RimWorld Mods folder.
# C: is full on this machine, so all dotnet caches/temp go to D:.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
DEST="/d/Games/Rimworld.v1.6.4850/game/Mods/AdvancedPlantGenetics"
bash "$ROOT/build.sh"
rm -rf "$DEST"
mkdir -p "$DEST"
cp -r "$ROOT/About" "$ROOT/1.6" "$ROOT/Languages" "$ROOT/Textures" "$DEST/"
echo "Deployed to $DEST"
