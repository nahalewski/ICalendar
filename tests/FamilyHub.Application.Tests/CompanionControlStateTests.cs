using FamilyHub.Contracts;
using FamilyHub.Web;
using Xunit;

namespace FamilyHub.Application.Tests;

public sealed class CompanionControlStateTests : IDisposable
{
    private readonly string _storePath = Path.Combine(Path.GetTempPath(), $"familyhub-test-{Guid.NewGuid():N}.json");

    private static CompanionEventDto Event(string title, Guid? id = null) =>
        new(id, title, DateTimeOffset.Now, DateTimeOffset.Now.AddHours(1), false, "Home", ["Dad"]);

    [Fact]
    public void EventsSurviveARestart()
    {
        var saved = new CompanionControlState(_storePath).SaveEvent(Event("Dentist"));

        // A second instance stands in for the next launch of the dashboard.
        var reloaded = new CompanionControlState(_storePath);
        var restored = Assert.Single(reloaded.Events);
        Assert.Equal("Dentist", restored.Title);
        Assert.Equal(saved.Id, restored.Id);
    }

    [Fact]
    public void SaveAssignsAnIdWhenThePhoneOmitsOne()
    {
        var saved = new CompanionControlState(_storePath).SaveEvent(Event("Recital"));
        Assert.NotNull(saved.Id);
        Assert.NotEqual(Guid.Empty, saved.Id!.Value);
    }

    [Fact]
    public void ResavingTheSameIdReplacesRatherThanDuplicates()
    {
        var state = new CompanionControlState(_storePath);
        var first = state.SaveEvent(Event("Recital"));
        state.SaveEvent(Event("Recital (moved)", first.Id));

        var only = Assert.Single(state.Events);
        Assert.Equal("Recital (moved)", only.Title);
    }

    [Fact]
    public void RotationSettingsSurviveARestart()
    {
        Assert.True(new CompanionControlState(_storePath).SetRotation(new RotationSettingsDto(45, 20, false)));

        var reloaded = new CompanionControlState(_storePath).Rotation;
        Assert.Equal(45, reloaded.RotationSeconds);
        Assert.Equal(20, reloaded.ResumeAfterInactivitySeconds);
        Assert.False(reloaded.RotationEnabled);
    }

    [Fact]
    public void RotationSettingsOutsideTheAllowedRangeAreRejected()
    {
        var state = new CompanionControlState(_storePath);
        Assert.False(state.SetRotation(new RotationSettingsDto(5, 20, true)));
        Assert.False(state.SetRotation(new RotationSettingsDto(90, 99999, true)));
        Assert.Equal(90, state.Rotation.RotationSeconds);
    }

    [Fact]
    public void ACorruptStoreDoesNotPreventStartup()
    {
        File.WriteAllText(_storePath, "{ this is not json");
        var state = new CompanionControlState(_storePath);
        Assert.Empty(state.Events);
    }

    public void Dispose()
    {
        if (File.Exists(_storePath)) File.Delete(_storePath);
    }
}
