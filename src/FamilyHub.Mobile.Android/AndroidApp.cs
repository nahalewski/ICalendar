using Android.App;
using Android.Runtime;
using Avalonia.Android;

namespace FamilyHub.Mobile.Android;

[Application(UsesCleartextTraffic = true, Label = "FamilyHub")]
public sealed class AndroidApp : AvaloniaAndroidApplication<FamilyHub.Mobile.App>
{
    public AndroidApp(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer) { }
}
