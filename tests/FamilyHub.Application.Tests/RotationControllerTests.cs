using FamilyHub.Application;
using FamilyHub.Core;
using Xunit;

namespace FamilyHub.Application.Tests;

public sealed class RotationControllerTests
{
    [Fact] public void NextUsesRequiredOrder()
    {
        var sut = new RotationController();
        Assert.Equal(DashboardPage.Daily, sut.Current);
        sut.MoveNext(); Assert.Equal(DashboardPage.Weekly, sut.Current);
        sut.MoveNext(); Assert.Equal(DashboardPage.Monthly, sut.Current);
        sut.MoveNext(); Assert.Equal(DashboardPage.Weather, sut.Current);
        sut.MoveNext(); Assert.Equal(DashboardPage.Daily, sut.Current);
    }

    [Fact] public void InteractionPausesRotation()
    {
        var sut = new RotationController();
        var now = DateTimeOffset.UtcNow;
        sut.RecordInteraction(now);
        Assert.False(sut.TryRotate(now.AddSeconds(10), TimeSpan.Zero, TimeSpan.FromSeconds(30)));
    }

    [Fact] public void AutomaticRotationLeavesAuxiliaryPagesAloneWhileEditing()
    {
        var sut = new RotationController();
        var now = DateTimeOffset.UtcNow;
        sut.Navigate(DashboardPage.Settings);
        sut.RecordInteraction(now);
        // The dashboard's own 30s idle window has elapsed, but Settings holds the location field.
        Assert.False(sut.TryRotate(now.AddMinutes(2), TimeSpan.Zero, TimeSpan.FromSeconds(30)));
        Assert.Equal(DashboardPage.Settings, sut.Current);
        // Left untouched for long enough, the wall display still reclaims the screen.
        Assert.True(sut.TryRotate(now.AddMinutes(6), TimeSpan.Zero, TimeSpan.FromSeconds(30)));
        Assert.Equal(DashboardPage.Daily, sut.Current);
    }

    [Fact] public void AuxiliaryPagesReturnToPrimaryRotation()
    {
        var sut = new RotationController();
        sut.Navigate(DashboardPage.Family);
        sut.MoveNext();
        Assert.Equal(DashboardPage.Daily, sut.Current);
        sut.Navigate(DashboardPage.Agenda);
        sut.MovePrevious();
        Assert.Equal(DashboardPage.Weather, sut.Current);
    }
}
