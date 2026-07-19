# FamilyHub Calendar

Raspberry Pi family calendar and weather dashboard. Phase 1 provides the layered solution, domain seed data, an Avalonia kiosk shell, four rotating mock screens, interaction pause, swipe/keyboard navigation, DI, provider contracts, a health/API web foundation, and rotation tests.

## Prerequisites

- .NET 10 SDK (latest patch)
- Windows, macOS, Linux, or Raspberry Pi OS 64-bit

## Run

```bash
dotnet restore FamilyHub.slnx
dotnet build FamilyHub.slnx -c Release
dotnet test FamilyHub.slnx -c Release
dotnet run --project src/FamilyHub.App/FamilyHub.App.csproj
```

Copy `appsettings.example.json` to `appsettings.json` for local configuration. Never commit OAuth credentials or tokens.

## Architecture

- `Core`: domain models and default person profiles
- `Application`: use cases and rotation state machine
- `Infrastructure`: replaceable provider/token contracts (implementations begin in Phase 2/3)
- `App`: Avalonia MVVM kiosk UI
- `Web`: ASP.NET Core endpoint foundation
- `Shared`: cross-process configuration/DTOs

## Configuration and secrets

Home coordinates, timezone, units, refresh/rotation intervals, transition, bindings, CORS origins, OAuth credential path, and kiosk behavior are configurable. Google client credentials and refresh tokens belong under a permission-restricted `secrets/` directory and are excluded from Git.
