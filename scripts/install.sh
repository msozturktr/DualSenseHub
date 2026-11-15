#!/usr/bin/env bash
# User-level install (no sudo): publishes the app, adds launcher, icon and tray autostart. Idempotent.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP="$HOME/.local/share/dualsensehub"
BIN="$HOME/.local/bin"
ICON_DIR="$HOME/.local/share/icons/hicolor/256x256/apps"
APPS="$HOME/.local/share/applications"
AUTOSTART="$HOME/.config/autostart"

mkdir -p "$APP" "$BIN" "$ICON_DIR" "$APPS" "$AUTOSTART"
dotnet publish "$ROOT/DualSenseHub.csproj" -c Release -o "$APP" --nologo -v q

running_pids() {
  local d t
  for d in /proc/[0-9]*; do
    t="$(readlink "$d/exe" 2>/dev/null || true)"
    case "$t" in "$APP/DualSenseHub"|"$APP/DualSenseHub (deleted)") echo "${d#/proc/}" ;; esac
  done
}

# Replace a running instance: ask it to quit over the socket, wait, fall back to kill, then relaunch.
RUNNING_PIDS="$(running_pids)"
RELAUNCH=0
if [ -n "$RUNNING_PIDS" ]; then
  RELAUNCH=1
  echo "Çalışan örnek kapatılıyor ($RUNNING_PIDS)..."
  "$APP/DualSenseHub" --quit >/dev/null 2>&1 || true
  for _ in $(seq 1 50); do
    [ -z "$(running_pids)" ] && break
    sleep 0.1
  done
  LEFT="$(running_pids)"
  if [ -n "$LEFT" ]; then echo "Soket komutu yetmedi; sonlandırılıyor: $LEFT"; kill $LEFT 2>/dev/null || true; sleep 1; fi
fi
ln -sf "$APP/DualSenseHub" "$BIN/dualsensehub"
install -m 644 "$ROOT/Assets/icon.png" "$ICON_DIR/dualsensehub.png"

cat > "$APPS/dualsensehub.desktop" <<DESK
[Desktop Entry]
Type=Application
Name=DualSense Hub
Comment=DualSense lightbar, haptics and game modes
Exec=$BIN/dualsensehub
Icon=dualsensehub
Terminal=false
Categories=Utility;Settings;
StartupWMClass=DualSenseHub
DESK

cat > "$AUTOSTART/dualsensehub.desktop" <<DESK
[Desktop Entry]
Type=Application
Name=DualSense Hub
Exec=$BIN/dualsensehub --tray
Icon=dualsensehub
Terminal=false
X-GNOME-Autostart-enabled=true
DESK

command -v update-desktop-database >/dev/null && update-desktop-database "$APPS" 2>/dev/null || true
command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -q -t "$HOME/.local/share/icons/hicolor" 2>/dev/null || true
if [ "$RELAUNCH" = 1 ]; then
  setsid -f "$BIN/dualsensehub" --tray >/dev/null 2>&1 || true
  sleep 2
  echo "Yeniden başlatıldı: $(running_pids | tr '\n' ' ')"
fi
echo "Kuruldu: $APP"
echo "NOT: ETS2 eklentisi (ets2-plugin) değiştiyse ETS2'yi yeniden başlatın."
echo "Başlatıcı: dualsensehub (uygulama menüsünde 'DualSense Hub'); oturum açılışında tepsiye otomatik başlar."
