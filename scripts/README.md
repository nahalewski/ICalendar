# Auto-update and startup scripts

Keeps the FamilyHub dashboard tracking `origin/main`: check GitHub on an interval, pull, rebuild,
and restart — and start automatically on boot.

## The rule these scripts follow

**A failed build never takes the display down.** Every update publishes into a `staging` directory
and is only swapped into `current` after a clean build. If a commit doesn't compile, the previous
build keeps running and the failure is logged. The wall display is the family's calendar; it should
not go blank because of a bad push.

The previous build is kept in `previous/` so a bad-but-compiling change can be rolled back by hand.

Exit codes are shared by both platforms: `0` up to date, `10` updated, `1` failed.

---

## Windows

```powershell
cd "C:\Users\COUGAR-PC\Documents\Calendar Project\scripts\windows"
powershell -ExecutionPolicy Bypass -File .\install-windows.ps1
Start-ScheduledTask -TaskName "FamilyHub Dashboard"     # start now, no reboot needed
```

Registers a Scheduled Task that runs `familyhub-agent.ps1` **at logon**. The agent updates once so a
reboot lands on the newest commit, launches the dashboard, then every 5 minutes checks GitHub and
restarts the app when an update builds cleanly. It also relaunches the dashboard if it exits for any
other reason.

Logon rather than boot is deliberate — the dashboard needs an interactive desktop to draw on, which
a SYSTEM service at boot does not have.

| | |
|---|---|
| Runtime build | `%LOCALAPPDATA%\FamilyHub\runtime\current` |
| Logs | `%LOCALAPPDATA%\FamilyHub\logs\{agent,update}.log` |
| Change interval | `install-windows.ps1 -CheckEveryMinutes 15` |
| Track a branch | `install-windows.ps1 -Branch dev` |
| Update once, by hand | `.\familyhub-update.ps1 -Force` |
| Remove | `.\install-windows.ps1 -Uninstall` |

---

## Raspberry Pi

```bash
git clone https://github.com/nahalewski/ICalendar.git ~/ICalendar
cd ~/ICalendar/scripts/pi
chmod +x *.sh
./install-pi.sh                      # or: ./install-pi.sh --interval 15min --branch dev
```

Installs three **user** systemd units — user, not system, because the dashboard needs the graphical
session belonging to the logged-in user:

| Unit | Role |
|---|---|
| `familyhub.service` | The dashboard. `Restart=always`, so a crash comes straight back. |
| `familyhub-update.service` | One update cycle. On exit code 10 it restarts the dashboard. |
| `familyhub-update.timer` | Fires 1 minute after boot, then on the interval. |

`loginctl enable-linger` is set so the services survive reboots before anyone logs in.

```bash
systemctl --user status familyhub.service
systemctl --user list-timers familyhub-update.timer
journalctl --user -u familyhub.service -f
tail -f ~/.local/share/familyhub/logs/update.log
./install-pi.sh --uninstall
```

### Which Pi actually works

**.NET requires 64-bit ARM.** This matters more than it sounds:

| Board | Works? |
|---|---|
| Pi Zero W / Zero (original), Pi 1, Pi 2 v1 | **No.** ARMv6/ARMv7 — .NET will not run at all. |
| Pi Zero 2 W | Technically yes on 64-bit Pi OS, but 512 MB RAM is very tight. Build elsewhere. |
| Pi 3 | Workable on 64-bit Pi OS. Builds are slow. |
| **Pi 4 / Pi 5 (2 GB+)** | **Recommended.** |

If you meant a **Pi Zero W**, it cannot run this app — that is a hardware limit, not a
configuration problem. A Pi Zero **2** W will, but compiling on 512 MB is painful; use the
cross-build below and let the Pi only run the result.

Check what you have: `uname -m` must print `aarch64`. If it prints `armv6l` or `armv7l` you are on
32-bit or unsupported hardware. The installer warns and asks before continuing.

---

## Pi Zero W: browser kiosk instead

A Pi Zero W **cannot run the app** — it is ARMv6 and .NET has no runtime for that architecture. It
can still be the display. The PC runs FamilyHub and already serves a browser dashboard on port 5643;
the Zero W just shows it in Chromium.

```bash
sudo apt install -y chromium-browser unclutter
cd ~/ICalendar/scripts/pi-kiosk && chmod +x install-kiosk.sh
./install-kiosk.sh --host 192.168.1.50          # the PC running FamilyHub
```

The Pi must boot to the desktop: `sudo raspi-config` → System Options → Boot / Auto Login →
**Desktop Autologin**.

| | |
|---|---|
| Rotate the display | `--rotate right` (also `left`, `inverted`) |
| Different port | `--port 5643` |
| Start without rebooting | `~/.local/bin/familyhub-kiosk.sh` |
| Quit the kiosk | `Alt+F4`, or `pkill -f chromium.*kiosk` over ssh |
| Remove | `./install-kiosk.sh --uninstall` |

What the launcher handles for you: screen blanking and DPMS off, pointer hidden, Chromium's
"didn't shut down correctly" bubble cleared after a power cut, and a retry loop so the Pi can boot
before the PC does. GPU rasterisation is deliberately **off** — on ARMv6 with no usable compositing
it is slower than CPU rendering, not faster.

Honest expectations on a Zero W: roughly a minute from power-on to the dashboard, and page switches
take a beat. Fine for a calendar that changes by the minute. The dashboard is built for that budget
— no framework, no animations, one small fetch every 30 seconds, and the class-block progress bars
are computed in the browser so the ticking costs no network traffic at all.

**The PC's firewall must allow inbound TCP 5643.** If the Pi shows "Reconnecting to FamilyHub…",
that is the first thing to check:

```powershell
New-NetFirewallRule -DisplayName "FamilyHub 5643" -Direction Inbound -LocalPort 5643 -Protocol TCP -Action Allow
```

You can open the same dashboard from any phone or tablet on the network at `http://<pc>:5643/`.

---

### Low-memory Pis: build elsewhere

Publish self-contained on this PC and copy the output over, so the Pi never compiles:

```powershell
dotnet publish src\FamilyHub.App\FamilyHub.App.csproj -c Release -r linux-arm64 `
  --self-contained true -o publish\pi
scp -r publish\pi\* pi@familyhub.local:~/.local/share/familyhub/current/
```

Then use the timer for notification only, or drop it and update by hand.

---

## Local edits survive updates

Every update replaces the published output, so anything hand-edited there would be lost. The app
therefore prefers a copy in `%LOCALAPPDATA%\FamilyHub\` (Windows) or `~/.local/share/FamilyHub/`
(Pi) over the file shipped in the build.

To keep your own corrected school calendar across updates, copy it up one level:

```powershell
copy "$env:LOCALAPPDATA\FamilyHub\runtime\current\school-calendars.json" "$env:LOCALAPPDATA\FamilyHub\"
```

```bash
cp ~/.local/share/familyhub/current/school-calendars.json ~/.local/share/FamilyHub/
```

The same applies to `bell-schedules.json`. Saved weather location, companion events, and rotation
settings already live outside the build directory and are never touched by an update.

---

## Troubleshooting

**Nothing updates.** Check `update.log`. A failed `git fetch` is logged as a warning and skipped —
usually no network. Both scripts deliberately leave the running build alone when offline.

**"Fast-forward pull failed."** There are local commits or uncommitted edits in the clone. The
scripts refuse to discard them. Resolve by hand: `git status`, then commit, stash, or reset.

**Builds fail only on the Pi.** Almost always memory. Check `free -h`; add swap or cross-build.

**App won't start after an update.** Roll back to the previous build:

```bash
systemctl --user stop familyhub.service
rm -rf ~/.local/share/familyhub/current
mv ~/.local/share/familyhub/previous ~/.local/share/familyhub/current
systemctl --user start familyhub.service
```
