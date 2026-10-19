#!/usr/bin/env bash
# In-game integration test: isolated save-data folder, minimal mod list (Harmony, Core, DLCs as installed, this mod,
# the test harness). Never touches your real config, saves or mod list.
# Output: $DATA/apgtest_results.txt and screenshots in $DATA/shots.
# Pass --pa to also load Progression: Agriculture (+ ReplaceLib) for the compatibility check.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GAME="/d/Games/Rimworld.v1.6.4850/game"
DEV="/d/FunNoTrabajo/_devtmp"
DATA="$DEV/apgdata"
export NUGET_PACKAGES="D:\FunNoTrabajo\_devtmp\nuget" DOTNET_CLI_HOME="D:\FunNoTrabajo\_devtmp\home"
export TMP="D:\FunNoTrabajo\_devtmp\tmp" TEMP="D:\FunNoTrabajo\_devtmp\tmp" DOTNET_NOLOGO=1

bash "$ROOT/deploy.sh"
mkdir -p "$ROOT/Tests/APGTest/Assemblies"
dotnet build "$ROOT/Tests/APGTest/Src/APGTest.csproj" -c Release -nologo -v q -p:OutputPath="$(cygpath -w "$ROOT/Tests/APGTest/Assemblies")\\"
rm -rf "$GAME/Mods/APGTest"; mkdir -p "$GAME/Mods/APGTest"
cp -r "$ROOT/Tests/APGTest/About" "$ROOT/Tests/APGTest/Assemblies" "$GAME/Mods/APGTest/"

rm -rf "$DATA"; mkdir -p "$DATA/Config"
cat > "$DATA/Config/ModsConfig.xml" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<ModsConfigData>
  <version>1.6.4850 rev645</version>
  <activeMods>
    <li>brrainz.harmony</li><li>ludeon.rimworld</li><li>ludeon.rimworld.biotech</li>PA_MODS
    <li>iyedm.advancedplantgenetics</li><li>iyedm.apgtest</li>
  </activeMods>
</ModsConfigData>
XML
# Muted: the harness never needs sound.
cat > "$DATA/Config/Prefs.xml" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<PrefsData>
  <volumeMaster>0</volumeMaster>
  <volumeGame>0</volumeGame>
  <volumeMusic>0</volumeMusic>
  <volumeAmbient>0</volumeAmbient>
  <volumeUI>0</volumeUI>
  <runInBackground>true</runInBackground>
</PrefsData>
XML
if [[ " $* " == *" --pa "* ]]; then
  sed -i 's#PA_MODS#<li>ferny.replacelib</li><li>ferny.progressionagriculture</li>#' "$DATA/Config/ModsConfig.xml"
else
  sed -i 's#PA_MODS##' "$DATA/Config/ModsConfig.xml"
fi
(cd "$GAME" && timeout 900 ./RimWorldWin64.exe -savedatafolder="$(cygpath -w "$DATA")" -logfile "$(cygpath -w "$DEV/apg_player.log")" \
  -quicktest -screen-width 1600 -screen-height 900 -popupwindow) || true
rm -rf "$GAME/Mods/APGTest"
cat "$DATA/apgtest_results.txt"
grep -q "DONE fails=0" "$DATA/apgtest_results.txt"
