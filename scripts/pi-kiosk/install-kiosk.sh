#!/usr/bin/env bash
# Turns a Raspberry Pi Zero W into a display for the FamilyHub dashboard.
#
# The Zero W is ARMv6 and cannot run .NET, so it does not run the app — it runs Chromium in kiosk
# mode pointed at a PC that does. This script installs an autostart entry, disables screen blanking,
# and waits for the host to come up before launching.
#
# Usage:  ./install-kiosk.sh --host 192.168.1.50           (the PC running FamilyHub)
#         ./install-kiosk.sh --host familyhub-pc --port 5643 --rotate right
#         ./install-kiosk.sh --uninstall
set -euo pipefail

HOST=""
PORT="5643"
ROTATE=""
UNINSTALL=0

while [ $# -gt 0 ]; do
  case "$1" in
    --host)      HOST="$2"; shift 2 ;;
    --port)      PORT="$2"; shift 2 ;;
    --rotate)    ROTATE="$2"; shift 2 ;;   # normal | left | right | inverted
    --uninstall) UNINSTALL=1; shift ;;
    *) echo "Unknown option: $1" >&2; exit 1 ;;
  esac
done

AUTOSTART_DIR="$HOME/.config/autostart"
LAUNCHER="$HOME/.local/bin/familyhub-kiosk.sh"

if [ "$UNINSTALL" = "1" ]; then
  rm -f "$AUTOSTART_DIR/familyhub-kiosk.desktop" "$LAUNCHER"
  echo "Kiosk autostart removed. Reboot to return to the normal desktop."
  exit 0
fi

[ -n "$HOST" ] || { echo "Missing --host (the PC running FamilyHub). Example: --host 192.168.1.50" >&2; exit 1; }

URL="http://$HOST:$PORT/"

# --- Preflight ----------------------------------------------------------------------------------
BROWSER=""
for candidate in chromium-browser chromium; do
  if command -v "$candidate" >/dev/null; then BROWSER="$candidate"; break; fi
done
[ -n "$BROWSER" ] || { echo "Chromium not found. Install it: sudo apt install -y chromium-browser" >&2; exit 1; }

echo "Checking $URL ..."
if command -v curl >/dev/null && curl -sf -m 8 -o /dev/null "$URL"; then
  echo "  reachable."
else
  echo "  WARNING: no response yet. The launcher retries, so this is fine if the PC is off." >&2
  echo "  If it never connects, check the PC's firewall allows inbound TCP $PORT." >&2
fi

mkdir -p "$AUTOSTART_DIR" "$(dirname "$LAUNCHER")"

# --- Launcher -------------------------------------------------------------------------------------
# Flags are chosen for a single-core ARMv6 with no usable GPU compositing: the usual
# --enable-gpu-rasterization advice makes things *worse* here, so rendering stays on the CPU.
cat > "$LAUNCHER" <<EOF
#!/usr/bin/env bash
set -u
URL="$URL"
BROWSER="$BROWSER"

# Stop the screen blanking or dimming — this is a wall display.
xset s off      2>/dev/null || true
xset -dpms      2>/dev/null || true
xset s noblank  2>/dev/null || true
$( [ -n "$ROTATE" ] && echo "xrandr --output \$(xrandr | awk '/ connected/{print \$1; exit}') --rotate $ROTATE 2>/dev/null || true" )

# Hide the pointer if unclutter is present.
command -v unclutter >/dev/null && unclutter -idle 1 -root &

# Chromium refuses to start cleanly after a power cut unless the crash flags are cleared.
PROFILE="\$HOME/.config/familyhub-kiosk"
mkdir -p "\$PROFILE/Default"
sed -i 's/"exit_type":"Crashed"/"exit_type":"Normal"/; s/"exited_cleanly":false/"exited_cleanly":true/' \\
  "\$PROFILE/Default/Preferences" 2>/dev/null || true

# Wait for the host. A wall display often boots before the PC does.
for i in \$(seq 1 60); do
  if curl -sf -m 5 -o /dev/null "\$URL"; then break; fi
  sleep 5
done

exec "\$BROWSER" \\
  --kiosk "\$URL" \\
  --user-data-dir="\$PROFILE" \\
  --noerrdialogs \\
  --disable-infobars \\
  --disable-session-crashed-bubble \\
  --disable-features=Translate,TranslateUI \\
  --disable-pinch \\
  --overscroll-history-navigation=0 \\
  --check-for-update-interval=31536000 \\
  --autoplay-policy=user-gesture-required \\
  --disable-background-networking \\
  --disable-sync \\
  --disable-translate \\
  --disable-smooth-scrolling \\
  --disable-gpu \\
  --disable-software-rasterizer \\
  --renderer-process-limit=1 \\
  --window-position=0,0
EOF
chmod +x "$LAUNCHER"

# --- Autostart ------------------------------------------------------------------------------------
cat > "$AUTOSTART_DIR/familyhub-kiosk.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=FamilyHub Kiosk
Exec=$LAUNCHER
X-GNOME-Autostart-enabled=true
NoDisplay=true
EOF

cat <<EOF

Installed.
  Display URL : $URL
  Launcher    : $LAUNCHER
  Autostart   : $AUTOSTART_DIR/familyhub-kiosk.desktop

Start now without rebooting:
  $LAUNCHER

Quit the kiosk:  Alt+F4, or from ssh:  pkill -f "$BROWSER.*kiosk"
Remove it:       ./install-kiosk.sh --uninstall

The Pi must boot to the desktop, not the console:
  sudo raspi-config  ->  System Options  ->  Boot / Auto Login  ->  Desktop Autologin
EOF
