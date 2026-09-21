using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RCynQ.Service.Hosting;

namespace RCynQ.Service.Tests;

/// <summary>
/// Tests of <see cref="ParentProcessWatchdog"/>.
/// </summary>
public sealed class ParentProcessWatchdogTests
{
    /// <summary>
    /// Verifies that the service stops once the parent process is gone.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Stops_Application_When_Parent_Exits()
    {
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var alive = true;
        using var watchdog = new ParentProcessWatchdog(new ParentProcessOptions(1234), lifetime, NullLogger<ParentProcessWatchdog>.Instance, pid => pid == 1234 && alive, TimeSpan.FromMilliseconds(10));

        await watchdog.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        lifetime.DidNotReceive().StopApplication();

        alive = false;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (lifetime.ReceivedCalls().All(call => call.GetMethodInfo().Name != nameof(IHostApplicationLifetime.StopApplication)) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        lifetime.Received(1).StopApplication();
        await watchdog.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// Verifies the default check against a process that does not exist.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Missing_Parent_Stops_Application()
    {
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        using var watchdog = new ParentProcessWatchdog(new ParentProcessOptions(int.MaxValue), lifetime, NullLogger<ParentProcessWatchdog>.Instance);

        await watchdog.StartAsync(CancellationToken.None);
        await Task.Delay(200);

        lifetime.Received(1).StopApplication();
        await watchdog.StopAsync(CancellationToken.None);
    }
}
