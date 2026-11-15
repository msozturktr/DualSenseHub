#!/usr/bin/env bash
# Installs the DualSense Hub udev rule. Requires sudo.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
sudo cp "$here/../packaging/70-dualsense.rules" /etc/udev/rules.d/70-dualsense.rules
sudo udevadm control --reload
sudo udevadm trigger --subsystem-match=leds --subsystem-match=hidraw --action=add
echo "Kural kuruldu. Denetleyiciyi cikarip yeniden baglayin."
