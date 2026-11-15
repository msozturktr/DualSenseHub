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
echo "Kuruldu: $APP"
echo "Başlatıcı: dualsensehub (uygulama menüsünde 'DualSense Hub'); oturum açılışında tepsiye otomatik başlar."
