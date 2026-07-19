#!/usr/bin/env bash
# Installs FamilyHub on a Raspberry Pi as systemd user services:
#
#   familyhub.service         the dashboard itself, restarted if it ever exits
#   familyhub-update.service  one update cycle, restarting the dashboard on success
#   familyhub-update.timer    runs the update check on an interval
#
# User services (not system) are deliberate: the dashboard needs the graphical session belonging to
# the logged-in user. Lingering is enabled so they still start when the Pi boots to the desktop.
#
# Usage:  ./install-pi.sh [--interval 5min] [--branch main] [--uninstall]
set -euo pipefail

REPO_PATH="${FAMILYHUB_REPO:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
RUNTIME_DIR="${FAMILYHUB_RUNTIME:-$HOME/.local/share/familyhub}"
BRANCH="main"
INTERVAL="5min"
UNINSTALL=0

while [ $# -gt 0 ]; do
  case "$1" in
    --interval)  INTERVAL="$2"; shift 2 ;;
    --branch)    BRANCH="$2"; shift 2 ;;
    --uninstall) UNINSTALL=1; shift ;;
    *) echo "Unknown option: $1" >&2; exit 1 ;;
  esac
done

UNIT_DIR="$HOME/.config/systemd/user"

if [ "$UNINSTALL" = "1" ]; then
  systemctl --user disable --now familyhub-update.timer 2>/dev/null || true
  systemctl --user disable --now familyhub.service 2>/dev/null || true
  rm -f "$UNIT_DIR"/familyhub.service "$UNIT_DIR"/familyhub-update.service "$UNIT_DIR"/familyhub-update.timer
  systemctl --user daemon-reload
  echo "Removed FamilyHub services. Build and logs under $RUNTIME_DIR were left in place."
  exit 0
fi

# --- Preflight ----------------------------------------------------------------------------------
command -v git >/dev/null    || { echo "git not found. sudo apt install git" >&2; exit 1; }
command -v dotnet >/dev/null || { echo "dotnet not found. Install the .NET 10 SDK (arm64) first." >&2; exit 1; }
command -v flock >/dev/null  || { echo "flock not found. sudo apt install util-linux" >&2; exit 1; }

ARCH="$(uname -m)"
if [ "$ARCH" != "aarch64" ] && [ "$ARCH" != "x86_64" ]; then
  echo "WARNING: architecture '$ARCH' detected." >&2
  echo "  .NET requires 64-bit ARM (aarch64). A 32-bit Pi OS or an ARMv6 board such as the" >&2
  echo "  original Pi Zero / Zero W cannot run this app at all. See scripts/README.md." >&2
  read -r -p "Continue anyway? [y/N] " reply
  [ "$reply" = "y" ] || [ "$reply" = "Y" ] || exit 1
fi

TOTAL_MB=$(( $(grep MemTotal /proc/meminfo | awk '{print $2}') / 1024 ))
if [ "$TOTAL_MB" -lt 900 ]; then
  echo "WARNING: only ${TOTAL_MB}MB RAM. Avalonia plus ASP.NET is tight below ~1GB;" >&2
  echo "  expect slow first builds. Consider building on a faster machine (see README)." >&2
fi

UPDATE_SH="$REPO_PATH/scripts/pi/familyhub-update.sh"
[ -f "$UPDATE_SH" ] || { echo "Missing $UPDATE_SH" >&2; exit 1; }
chmod +x "$UPDATE_SH"

mkdir -p "$UNIT_DIR" "$RUNTIME_DIR/logs"

# --- Units --------------------------------------------------------------------------------------
cat > "$UNIT_DIR/familyhub.service" <<EOF
[Unit]
Description=FamilyHub dashboard
After=graphical-session.target
PartOf=graphical-session.target

[Service]
Type=simple
WorkingDirectory=$RUNTIME_DIR/current
ExecStart=$RUNTIME_DIR/current/FamilyHub.App
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1
# Falls back to the desktop session's display when not already set.
Environment=DISPLAY=:0
Restart=always
RestartSec=5

[Install]
WantedBy=graphical-session.target
EOF

cat > "$UNIT_DIR/familyhub-update.service" <<EOF
[Unit]
Description=FamilyHub update check
After=network-online.target
Wants=network-online.target

[Service]
Type=oneshot
Environment=FAMILYHUB_REPO=$REPO_PATH
Environment=FAMILYHUB_BRANCH=$BRANCH
Environment=FAMILYHUB_RUNTIME=$RUNTIME_DIR
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1
ExecStart=$UPDATE_SH
# Exit code 10 means "updated"; restart the dashboard onto the new build.
ExecStartPost=/bin/sh -c 'test \$SERVICE_RESULT = success || true'
SuccessExitStatus=0 10
ExecStopPost=/bin/sh -c 'if [ "\$EXIT_STATUS" = "10" ]; then systemctl --user restart familyhub.service; fi'
EOF

cat > "$UNIT_DIR/familyhub-update.timer" <<EOF
[Unit]
Description=Check GitHub for FamilyHub updates

[Timer]
# Once shortly after boot, then on the interval.
OnBootSec=1min
OnUnitActiveSec=$INTERVAL
Persistent=true

[Install]
WantedBy=timers.target
EOF

systemctl --user daemon-reload

# --- First build --------------------------------------------------------------------------------
echo "Running the first build (this can take several minutes on a Pi)..."
set +e
"$UPDATE_SH" --force
CODE=$?
set -e
if [ "$CODE" = "1" ]; then
  echo "First build failed. See $RUNTIME_DIR/logs/update.log" >&2
  exit 1
fi

systemctl --user enable --now familyhub-update.timer
systemctl --user enable --now familyhub.service

# Survive reboots even when nobody has logged in yet.
loginctl enable-linger "$USER" 2>/dev/null || \
  echo "Note: could not enable linger. Run: sudo loginctl enable-linger $USER"

cat <<EOF

Installed.
  Repo:     $REPO_PATH
  Branch:   $BRANCH
  Runtime:  $RUNTIME_DIR/current
  Interval: $INTERVAL
  Logs:     $RUNTIME_DIR/logs/update.log

  systemctl --user status familyhub.service
  systemctl --user list-timers familyhub-update.timer
  journalctl --user -u familyhub.service -f
EOF
