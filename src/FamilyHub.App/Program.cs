using Avalonia;
using FamilyHub.Application;
using FamilyHub.App.ViewModels;
using FamilyHub.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FamilyHub.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Avalonia.Threading;
using FamilyHub.Core;

namespace FamilyHub.App;

internal static class Program
{
    public static IHost Services { get; private set; } = null!;

    [STAThread]
    public static void Main(string[] args)
    {
        Services = Host.CreateDefaultBuilder(args).ConfigureServices(services =>
        {
            services.AddSingleton<IRotationController, RotationController>();
            // One instance shared by the dashboard and the companion API, so events saved from the
            // phone and events created here land in the same store.
            services.AddSingleton<CompanionControlState>();
            services.AddSingleton<DashboardFeed>();
            services.AddHttpClient<IWeatherProvider, OpenMeteoWeatherProvider>()
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseProxy = false });
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<MainWindow>();
            services.AddHostedService<LocalApiHostedService>();
        }).Build();
        Services.Start();
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        finally { Services.StopAsync().GetAwaiter().GetResult(); Services.Dispose(); }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect().WithInterFont().LogToTrace();
}

internal sealed class LocalApiHostedService : IHostedService
{
    private readonly IRotationController _rotation;
    private readonly CompanionControlState _controlState;
    private readonly DashboardFeed _feed;
    private WebApplication? _app;
    public LocalApiHostedService(IRotationController rotation, CompanionControlState controlState, DashboardFeed feed)
        => (_rotation, _controlState, _feed) = (rotation, controlState, feed);
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://0.0.0.0:5643");
        var controlState = _controlState;
        controlState.ScreenChanged += (_, screen) => Dispatcher.UIThread.Post(() =>
        {
            if (Enum.TryParse<DashboardPage>(screen, true, out var page)) _rotation.Navigate(page);
        });
        controlState.EventSaved += (_, item) => Dispatcher.UIThread.Post(() =>
            Program.Services.Services.GetRequiredService<MainWindowViewModel>().AddCompanionEvent(item));
        controlState.RotationChanged += (_, settings) => Dispatcher.UIThread.Post(() =>
            Program.Services.Services.GetRequiredService<MainWindowViewModel>().ApplyRotationSettings(settings));
        builder.Services.AddSingleton(controlState);
        builder.Services.AddSingleton(_feed);
        builder.Services.AddFamilyHubWeb();
        _app = builder.Build();
        _app.MapFamilyHub();
        _app.MapFamilyHubDashboard();
        await _app.StartAsync(cancellationToken);
    }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_app is not null) await _app.StopAsync(cancellationToken);
    }
}
