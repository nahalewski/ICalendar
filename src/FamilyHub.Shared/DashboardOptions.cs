namespace FamilyHub.Configuration;

public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";
    public int RotationSeconds { get; set; } = 90;
    public int ResumeAfterInactivitySeconds { get; set; } = 30;
    public int TransitionMilliseconds { get; set; } = 500;
    public string Transition { get; set; } = "Crossfade";
    public bool KioskMode { get; set; } = true;
}
