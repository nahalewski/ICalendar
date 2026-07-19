#!/usr/bin/env bash
# Checks GitHub for changes and rebuilds FamilyHub only if the build succeeds.
#
# One update cycle: fetch, compare, pull, publish to staging, and swap into place only after a
# clean build. A failed build leaves the running dashboard untouched — the wall display must never
# be left dead because someone pushed a broken commit.
#
# Exit codes: 0 up to date, 10 updated (caller should restart the app), 1 failed.
set -uo pipefail

REPO_PATH="${FAMILYHUB_REPO:-$HOME/ICalendar}"
BRANCH="${FAMILYHUB_BRANCH:-main}"
RUNTIME_DIR="${FAMILYHUB_RUNTIME:-$HOME/.local/share/familyhub}"
FORCE="${1:-}"

PROJECT="$REPO_PATH/src/FamilyHub.App/FamilyHub.App.csproj"
CURRENT="$RUNTIME_DIR/current"
STAGING="$RUNTIME_DIR/staging"
PREVIOUS="$RUNTIME_DIR/previous"
LOG_DIR="$RUNTIME_DIR/logs"
LOG_FILE="$LOG_DIR/update.log"
LOCK_FILE="$RUNTIME_DIR/.update.lock"

mkdir -p "$RUNTIME_DIR" "$LOG_DIR"

log() { printf '%s [%s] %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "${2:-INFO}" "$1" | tee -a "$LOG_FILE"; }

# Keep the log bounded on a device that runs for months.
if [ -f "$LOG_FILE" ] && [ "$(stat -c%s "$LOG_FILE" 2>/dev/null || echo 0)" -gt 1048576 ]; then
  tail -n 500 "$LOG_FILE" > "$LOG_FILE.tmp" && mv "$LOG_FILE.tmp" "$LOG_FILE"
fi

# A single lock stops the timer and a manual run from building over each other.
exec 9>"$LOCK_FILE"
if ! flock -n 9; then
  log 'Another update is already running; skipping this cycle.' WARN
  exit 0
fi

[ -f "$PROJECT" ] || { log "Project not found at $PROJECT" ERROR; exit 1; }
cd "$REPO_PATH" || { log "Cannot enter $REPO_PATH" ERROR; exit 1; }

# --- Is there anything new? --------------------------------------------------------------------
log "Checking origin/$BRANCH for changes"
if ! git fetch --quiet origin "$BRANCH" 2>>"$LOG_FILE"; then
  log 'Fetch failed (offline?); leaving the running build alone.' WARN
  exit 0
fi

LOCAL_SHA="$(git rev-parse HEAD)"
REMOTE_SHA="$(git rev-parse "origin/$BRANCH")"

if [ "$LOCAL_SHA" = "$REMOTE_SHA" ] && [ -f "$CURRENT/FamilyHub.App" ] && [ "$FORCE" != "--force" ]; then
  log "Up to date at ${LOCAL_SHA:0:7}"
  exit 0
fi

# --- Pull --------------------------------------------------------------------------------------
if [ "$LOCAL_SHA" != "$REMOTE_SHA" ]; then
  log "Update available: ${LOCAL_SHA:0:7} -> ${REMOTE_SHA:0:7}"
  if ! git pull --ff-only origin "$BRANCH" >>"$LOG_FILE" 2>&1; then
    # Refuse to clobber local commits or edits; a human should look at it.
    log 'Fast-forward pull failed. Local changes or diverged history — resolve manually.' ERROR
    exit 1
  fi
else
  log 'No new commits, but no usable build present; rebuilding.'
fi

# --- Build into staging, never over the running copy --------------------------------------------
rm -rf "$STAGING"
log 'Publishing (Release)'
if ! dotnet publish "$PROJECT" -c Release -o "$STAGING" --nologo >>"$LOG_FILE" 2>&1; then
  log 'Build FAILED. Keeping the previous build running.' ERROR
  tail -n 25 "$LOG_FILE" >&2
  exit 1
fi

# --- Swap in ------------------------------------------------------------------------------------
log 'Build succeeded; swapping into place'
rm -rf "$PREVIOUS"
[ -d "$CURRENT" ] && mv "$CURRENT" "$PREVIOUS"
mv "$STAGING" "$CURRENT"
chmod +x "$CURRENT/FamilyHub.App" 2>/dev/null || true

log "Updated to ${REMOTE_SHA:0:7}"
exit 10
