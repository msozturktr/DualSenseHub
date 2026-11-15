#!/usr/bin/env bash
# Removes everything install.sh created (settings in ~/.config/dualsensehub are kept).
set -euo pipefail
rm -rf "$HOME/.local/share/dualsensehub"
rm -f "$HOME/.local/bin/dualsensehub" \
      "$HOME/.local/share/icons/hicolor/256x256/apps/dualsensehub.png" \
      "$HOME/.local/share/applications/dualsensehub.desktop" \
      "$HOME/.config/autostart/dualsensehub.desktop"
command -v update-desktop-database >/dev/null && update-desktop-database "$HOME/.local/share/applications" 2>/dev/null || true
echo "Kaldırıldı. Ayarlar ~/.config/dualsensehub altında duruyor."
