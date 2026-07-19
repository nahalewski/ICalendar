using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FamilyHub.Mobile.ViewModels;
using FamilyHub.Mobile.Views;

namespace FamilyHub.Mobile;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IActivityApplicationLifetime activity)
            activity.MainViewFactory = () => new MainView { DataContext = new MainViewModel() };
        base.OnFrameworkInitializationCompleted();
    }
}
