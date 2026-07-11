using System.Threading;
using Windows.UI.Notifications.Management;

namespace Winotch.Tests;

public class LifecycleTests
{
    [Fact]
    public void TransitionSmokeTestRequiresAnExplicitLaunchArgument()
    {
        Assert.True(App.IsTransitionSmokeTest(["Winotch.exe", App.TransitionSmokeTestArgument]));
        Assert.True(App.IsTransitionSmokeTest(["Winotch.exe", "--TRANSITION-SMOKE-TEST"]));
        Assert.False(App.IsTransitionSmokeTest(["Winotch.exe"]));
        Assert.False(App.IsTransitionSmokeTest(["Winotch.exe", "--transition-smoke"]));
    }

    [Fact]
    public void ShelfSmokeTestRequiresAnExplicitLaunchArgument()
    {
        Assert.True(App.IsShelfSmokeTest(["Winotch.exe", App.ShelfSmokeTestArgument]));
        Assert.True(App.IsShelfSmokeTest(["Winotch.exe", "--SHELF-SMOKE-TEST"]));
        Assert.False(App.IsShelfSmokeTest(["Winotch.exe"]));
        Assert.False(App.IsShelfSmokeTest(["Winotch.exe", App.TransitionSmokeTestArgument]));
    }

    [Fact]
    public void SingleInstanceGuardRejectsSecondOwner()
    {
        var mutexName = $@"Local\Winotch.Tests.{Guid.NewGuid():N}";

        Assert.True(App.TryAcquireSingleInstance(mutexName, out var first));
        if (first is null)
        {
            throw new InvalidOperationException("Expected first mutex acquisition to return an owned mutex.");
        }

        using (first)
        {
            Assert.False(App.TryAcquireSingleInstance(mutexName, out var second));
            Assert.Null(second);

            first.ReleaseMutex();
        }

        Assert.True(App.TryAcquireSingleInstance(mutexName, out var reopened));
        if (reopened is null)
        {
            throw new InvalidOperationException("Expected released mutex to be acquired again.");
        }

        reopened.ReleaseMutex();
        reopened.Dispose();
    }

    [Theory]
    [InlineData(UserNotificationListenerAccessStatus.Allowed, true)]
    [InlineData(UserNotificationListenerAccessStatus.Denied, false)]
    [InlineData(UserNotificationListenerAccessStatus.Unspecified, false)]
    public void PassiveNotificationReadUsesExistingAccessOnly(UserNotificationListenerAccessStatus access, bool expected)
    {
        Assert.Equal(expected, NotificationService.CanReadNotificationHistory(access));
    }
}
