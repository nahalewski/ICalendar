using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace FamilyHub.Mobile.Android;

[Activity(Label = "FamilyHub", Theme = "@style/AppTheme.NoActionBar", MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity;
