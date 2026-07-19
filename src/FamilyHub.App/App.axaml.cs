using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Styling;

namespace FamilyHub.App;

public sealed partial class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public void ApplyAutomaticTheme(DateTimeOffset now) => RequestedThemeVariant = now.Hour is >= 18 or < 6 ? ThemeVariant.Dark : ThemeVariant.Light;
    public override void OnFrameworkInitializationCompleted()
    {
        ApplyAutomaticTheme(DateTimeOffset.Now);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = Program.Services.Services.GetRequiredService<MainWindow>();
        base.OnFrameworkInitializationCompleted();
    }
}
