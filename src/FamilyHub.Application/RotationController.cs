using FamilyHub.Core;

namespace FamilyHub.Application;

public interface IRotationController
{
    DashboardPage Current { get; }
    event EventHandler<DashboardPage>? PageChanged;
    void Navigate(DashboardPage page);
    void MoveNext();
    void MovePrevious();
    void RecordInteraction(DateTimeOffset now);
    bool TryRotate(DateTimeOffset now, TimeSpan interval, TimeSpan resumeDelay);
}

public sealed class RotationController : IRotationController
{
    private static readonly DashboardPage[] RotatingPages =
        [DashboardPage.Daily, DashboardPage.Weekly, DashboardPage.Monthly, DashboardPage.Weather];
    // Auxiliary pages (Settings, Agenda, Family) hold data entry, so automatic rotation waits far
    // longer before reclaiming the screen. Explicit MoveNext/MovePrevious still leave immediately.
    private static readonly TimeSpan AuxiliaryResumeDelay = TimeSpan.FromMinutes(5);
    private DateTimeOffset _lastRotation = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastInteraction = DateTimeOffset.MinValue;
    public DashboardPage Current { get; private set; }
    public event EventHandler<DashboardPage>? PageChanged;

    public void Navigate(DashboardPage page)
    {
        Current = page;
        _lastRotation = DateTimeOffset.UtcNow;
        PageChanged?.Invoke(this, page);
    }

    public void MoveNext()
    {
        var index = Array.IndexOf(RotatingPages, Current);
        Navigate(RotatingPages[(index < 0 ? 0 : index + 1) % RotatingPages.Length]);
    }

    public void MovePrevious()
    {
        var index = Array.IndexOf(RotatingPages, Current);
        Navigate(RotatingPages[index <= 0 ? RotatingPages.Length - 1 : index - 1]);
    }
    public void RecordInteraction(DateTimeOffset now) => _lastInteraction = now;

    public bool TryRotate(DateTimeOffset now, TimeSpan interval, TimeSpan resumeDelay)
    {
        if (Array.IndexOf(RotatingPages, Current) < 0 && resumeDelay < AuxiliaryResumeDelay)
            resumeDelay = AuxiliaryResumeDelay;
        if (now - _lastInteraction < resumeDelay || now - _lastRotation < interval) return false;
        MoveNext();
        _lastRotation = now;
        return true;
    }
}
