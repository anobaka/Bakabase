using System;
using System.Diagnostics;
using Bakabase.Abstractions.Components.App;

namespace Bakabase.Tests;

/// <summary>
/// The argument a restarting process hands its replacement, and the wait that argument buys.
/// Without it the replacement raced the single-instance guard against its own dying
/// predecessor and lost, leaving the user with no app running at all.
/// </summary>
[TestClass]
public class RestartHandoffTests
{
    [TestMethod]
    public void Argument_RoundTrips()
    {
        Assert.AreEqual(4242, RestartHandoff.TryReadPredecessorPid([RestartHandoff.FormatArgument(4242)]));
    }

    [TestMethod]
    public void Pid_IsFoundAmongOtherArguments()
    {
        string[] args = ["--some-flag", RestartHandoff.FormatArgument(17), "positional"];

        Assert.AreEqual(17, RestartHandoff.TryReadPredecessorPid(args));
    }

    [TestMethod]
    public void OrdinaryLaunch_HasNoPid()
    {
        Assert.IsNull(RestartHandoff.TryReadPredecessorPid([]));
        Assert.IsNull(RestartHandoff.TryReadPredecessorPid(null));
        Assert.IsNull(RestartHandoff.TryReadPredecessorPid(["--restart-after-pid"]));
    }

    [TestMethod]
    public void UnusableValues_AreIgnored()
    {
        // Rather than throwing on the way into a launch that is otherwise fine: a pid we
        // cannot read is a pid we cannot wait for, which is the same as not being told one.
        Assert.IsNull(RestartHandoff.TryReadPredecessorPid(["--restart-after-pid=lolwut"]));
        Assert.IsNull(RestartHandoff.TryReadPredecessorPid(["--restart-after-pid=0"]));
        Assert.IsNull(RestartHandoff.TryReadPredecessorPid(["--restart-after-pid=-1"]));
    }

    [TestMethod]
    public void OrdinaryLaunch_DoesNotWait()
    {
        var stopwatch = Stopwatch.StartNew();

        Assert.IsNull(RestartHandoff.WaitForPredecessor([], TimeSpan.FromSeconds(5)));
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"waited {stopwatch.Elapsed}");
    }

    [TestMethod]
    public void WaitIsBounded()
    {
        // Naming a process that never exits — this one — stands in for a predecessor wedged
        // open by a long shutdown. Starting late beats not starting at all.
        var stopwatch = Stopwatch.StartNew();

        var message = RestartHandoff.WaitForPredecessor(
            [RestartHandoff.FormatArgument(Environment.ProcessId)],
            TimeSpan.FromMilliseconds(300));

        Assert.IsNotNull(message);
        StringAssert.Contains(message, Environment.ProcessId.ToString());
        Assert.IsTrue(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(250), $"returned after {stopwatch.Elapsed}");
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"waited {stopwatch.Elapsed}");
    }
}
