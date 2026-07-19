# Implementation plan

1. **Phase 1 — shell:** layered projects, core records, DI/MVVM, full-screen Avalonia shell, four mock screens, navigation, interaction-aware rotation, and unit tests.
2. **Phase 2 — local domain:** EF Core SQLite migrations, validated settings, editable people/aliases, classification and override persistence, and holiday rules.
3. **Phase 3 — weather:** Open-Meteo provider, geocoding, cache/fallback, seven-day details, and stale-data indicators.
4. **Phase 4 — Google:** secure token store, installed-app/device OAuth, multi-account calendar selection, incremental sync, recurrence, and resilient caching.
5. **Phase 5 — schools:** source configuration, reviewed imports, official Harnett/Ascend workflows, and yearly update checks.
6. **Phase 6 — LAN:** Kestrel on configurable port 5643, responsive dashboard, health/API/SignalR, authentication, privacy mode, rate limits, and constrained CORS.
7. **Phase 7 — Pi:** X11 and framebuffer/DRM launch paths, systemd, display power/rotation, ARM64 publish and kiosk scripts.
8. **Phase 8 — hardening:** full test matrix, accessibility/security review, backup/restore, deployment and operations documentation.

## Decisions

- Target .NET 10 LTS and Avalonia 12; keep package versions centrally managed.
- Keep Core dependency-free and isolate replaceable weather, school, token, and calendar integrations behind interfaces.
- Use CommunityToolkit source-generated MVVM and Avalonia compiled bindings.
- Keep screen rotation independent of Avalonia so timing behavior is deterministic and unit-testable.
- Never store tokens in configuration; use a permission-restricted token-store implementation in Infrastructure.
- Treat SQLite as the offline source of truth for cached provider data; provider failures must not clear the last successful snapshot.
- Host Kestrel in-process initially, with boundaries that permit a separate service if display/backend constraints require it.

## Configurable values

Home location, timezone, units, refresh intervals, rotation/inactivity timing, transitions, brightness, kiosk lock, font scale, web bindings/port/CORS, privacy mode, school sources/tracks, holiday layers, Google credential-file path, selected calendars, and all people/aliases/classification rules.
