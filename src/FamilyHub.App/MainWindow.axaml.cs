using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using FamilyHub.App.ViewModels;
using FamilyHub.Application;
using FamilyHub.Infrastructure;

namespace FamilyHub.App;

public sealed partial class MainWindow : Window
{
    private Avalonia.Point _pointerStart;
    public MainWindow() : this(new MainWindowViewModel(new RotationController(), new OpenMeteoWeatherProvider(new HttpClient()), new FamilyHub.Web.CompanionControlState(), new FamilyHub.Web.DashboardFeed())) { }
    public MainWindow(MainWindowViewModel viewModel) { InitializeComponent(); DataContext = viewModel; }
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e) { ViewModel.Interact(); _pointerStart = e.GetPosition(this); }
    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        ViewModel.Interact();
        if (ViewModel.IsTextEntryActive) return;
        if (e.Delta.Y < 0) ViewModel.NextCommand.Execute(null);
        else if (e.Delta.Y > 0) ViewModel.PreviousCommand.Execute(null);
        e.Handled = true;
    }
    private void OnCalendarWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        ViewModel.Interact();
        if (e.Delta.Y < 0) ViewModel.NextMonthCommand.Execute(null);
        else if (e.Delta.Y > 0) ViewModel.PreviousMonthCommand.Execute(null);
        e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var delta = e.GetPosition(this).X - _pointerStart.X;
        if (Math.Abs(delta) > 80 && !ViewModel.IsTextEntryActive)
        { if (delta < 0) ViewModel.NextCommand.Execute(null); else ViewModel.PreviousCommand.Execute(null); }
        base.OnPointerReleased(e);
    }
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        ViewModel.Interact();
        // Focus alone is not enough: the sidebar button keeps focus after navigating to Settings, so
        // the single-letter shortcuts below would swallow the first characters typed into a field.
        var isEditingText = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox
            || ViewModel.IsTextEntryActive;
        if (e.Key == Key.Escape)
        {
            // Escape backs out of data entry first; it only closes the app from a dashboard page.
            if (ViewModel.IsEventEditorOpen) ViewModel.CloseEventEditorCommand.Execute(null);
            else if (ViewModel.IsSettings) ViewModel.HomeCommand.Execute(null);
            else if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
            e.Handled = true;
        }
        else if (isEditingText) return;
        else if (e.Key == Key.Left) ViewModel.PreviousCommand.Execute(null);
        else if (e.Key == Key.Right) ViewModel.NextCommand.Execute(null);
        else if (e.Key == Key.Home || e.Key == Key.D) ViewModel.HomeCommand.Execute(null);
        else if (e.Key == Key.W) ViewModel.NavigateCommand.Execute("Weekly");
        else if (e.Key == Key.M) ViewModel.NavigateCommand.Execute("Monthly");
        else if (e.Key == Key.F) ViewModel.NavigateCommand.Execute("Weather");
    }
}
