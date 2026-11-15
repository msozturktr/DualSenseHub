#!/usr/bin/env bash
# Copies the ETS2 telemetry plugin into the game's plugins folder (Steam/Proton install).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GAME="${ETS2_DIR:-$HOME/.local/share/Steam/steamapps/common/Euro Truck Simulator 2}"
DLL="$ROOT/ets2-plugin/dualsensehub_ets2.dll"
[ -f "$DLL" ] || "$ROOT/ets2-plugin/build.sh"
[ -d "$GAME/bin/win_x64" ] || { echo "ETS2 not found at: $GAME (set ETS2_DIR)" >&2; exit 1; }
mkdir -p "$GAME/bin/win_x64/plugins"
cp "$DLL" "$GAME/bin/win_x64/plugins/"
echo "Installed to $GAME/bin/win_x64/plugins/dualsensehub_ets2.dll"
