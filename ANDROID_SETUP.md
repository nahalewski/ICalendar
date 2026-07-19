# FamilyHub Android companion

The Android companion controls the Pi over its local Kestrel API. The default server is `http://familyhub.local:5643`; change it in Settings when mDNS is unavailable.

## Build and install

```powershell
dotnet workload install android
dotnet restore FamilyHub.slnx --configfile NuGet.Config
dotnet build src/FamilyHub.Mobile.Android/FamilyHub.Mobile.Android.csproj -c Debug
adb connect 192.168.1.148:34553
adb install -r src/FamilyHub.Mobile.Android/bin/Debug/net10.0-android/com.familyhub.calendar-Signed.apk
```

Wireless debugging must be enabled on the phone and this computer must be paired/authorized. The connect port can change when wireless debugging is restarted.

## Google Calendar behavior

FamilyHub should create a dedicated secondary calendar through the Google Calendar API after explicit OAuth consent. That calendar appears in the user's Google Calendar list and syncs to the Android Google Calendar app, where it can be shown or hidden. A private `familyhub.local` ICS URL cannot be used as a Google-hosted subscription because Google's servers cannot reach LAN-only addresses.

OAuth client IDs, client secrets, tokens, and the target Google account are not embedded in the APK. Authorization is initiated and stored securely by the Pi service.
