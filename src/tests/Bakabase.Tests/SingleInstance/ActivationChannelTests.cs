using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rules = Bakabase.Infrastructures.Components.App.SingleInstance.DataDirectoryIdentity.PathRules;

namespace Bakabase.Tests.SingleInstance;

[TestClass]
public class ActivationChannelTests
{
    private static string UniqueDir() =>
        DataDirectoryIdentity.Normalize(Path.Combine(Path.GetTempPath(), "bakabase-chan-" + Guid.NewGuid().ToString("N")));

    [TestMethod]
    public void The_name_follows_the_data_directory()
    {
        var one = ActivationChannel.GetName("/data/one", "alice", Rules.CaseSensitive);
        Assert.AreEqual(one, ActivationChannel.GetName("/data/one", "alice", Rules.CaseSensitive));
        Assert.AreNotEqual(one, ActivationChannel.GetName("/data/two", "alice", Rules.CaseSensitive),
            "instances on different data directories must never answer for each other");
        StringAssert.StartsWith(one, "Bakabase-");
        StringAssert.EndsWith(one, DataDirectoryIdentity.Hash("/data/one"));
    }

    [TestMethod]
    public void The_name_is_per_user()
    {
        // Windows pipe names are machine-wide: without the user, a second account's instance on
        // its own (different) directory could still collide — and on the same shared directory
        // it would talk to a window on someone else's desktop.
        var alice = ActivationChannel.GetName(@"C:\DATA", @"PC\alice", Rules.Windows);
        var bob = ActivationChannel.GetName(@"C:\DATA", @"PC\bob", Rules.Windows);
        Assert.AreNotEqual(alice, bob);

        Assert.AreEqual(alice, ActivationChannel.GetName(@"C:\DATA", @"pc\ALICE", Rules.Windows),
            "Windows account names ignore case");
        Assert.AreNotEqual(ActivationChannel.GetName("/data", "alice", Rules.CaseSensitive),
            ActivationChannel.GetName("/data", "Alice", Rules.CaseSensitive));
    }

    [TestMethod]
    public void Spellings_of_one_directory_share_a_channel()
    {
        var root = OperatingSystem.IsWindows() ? @"C:\" : "/";
        var a = DataDirectoryIdentity.Normalize(
            Path.Combine(root, "Users", "Me", "AppData", "Local", "Bakabase.AppData") + Path.DirectorySeparatorChar,
            Rules.Windows, false);
        var b = DataDirectoryIdentity.Normalize(
            Path.Combine(root, "users", "me", "appdata", "local", "bakabase.appdata"), Rules.Windows, false);

        Assert.AreEqual(ActivationChannel.GetName(a, "u", Rules.Windows), ActivationChannel.GetName(b, "u", Rules.Windows));
    }

    [TestMethod]
    public void The_name_fits_a_unix_socket_path()
    {
        // The socket is {per-user directory}/{name}; sun_path holds 104 bytes on macOS.
        var name = ActivationChannel.GetName(UniqueDir());
        Assert.IsTrue(name.Length <= 40, name);
        Assert.IsTrue(name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'), name);
    }

    [TestMethod]
    public void The_socket_lives_where_the_system_puts_this_users_files_not_where_TMPDIR_says()
    {
        var name = ActivationChannel.GetName(UniqueDir());
        var endpoint = ActivationChannel.GetEndpoint(name);
        if (OperatingSystem.IsWindows())
        {
            Assert.AreEqual(name, endpoint, "Windows pipe names are not paths");
            return;
        }

        var directory = ActivationChannel.GetSocketDirectory();
        Assert.IsNotNull(directory);
        Assert.AreEqual(Path.Combine(directory, name), endpoint);
        Assert.IsTrue(Encoding.UTF8.GetByteCount(endpoint) < ActivationChannel.MaxSocketPathBytes, endpoint);

        if (OperatingSystem.IsMacOS())
        {
            // What the system assigns this user — the answer getconf prints whatever TMPDIR is.
            var getconf = new ProcessStartInfo("/usr/bin/getconf", "DARWIN_USER_TEMP_DIR")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            getconf.Environment["TMPDIR"] = "/nonexistent-tmpdir/";
            using var process = Process.Start(getconf)!;
            var expected = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            Assert.AreEqual(Path.TrimEndingDirectorySeparator(expected), directory);
        }
        else if (OperatingSystem.IsLinux())
        {
            var uid = ActivationChannel.ReadLinuxEffectiveUid();
            Assert.IsNotNull(uid);
            Assert.IsTrue(directory == $"/run/user/{uid}" || directory == $"/tmp/bakabase-{uid}", directory);
        }
    }

    [TestMethod]
    public void Without_a_usable_socket_directory_the_plain_name_is_used()
    {
        // .NET's own placement ($TMPDIR/CoreFxPipe_{name}): still works between launches that
        // share a TMPDIR, which is every launch there was before.
        var name = ActivationChannel.GetName(UniqueDir());
        Assert.AreEqual(name, ActivationChannel.GetEndpoint(name, null));
        Assert.AreEqual(name, ActivationChannel.GetEndpoint(name, "/" + new string('x', 90)),
            "a path longer than a socket address holds is not used");
        Assert.AreEqual(Path.Combine("/short", name), ActivationChannel.GetEndpoint(name, "/short"));
    }

    [TestMethod]
    public void On_linux_the_runtime_directory_comes_first_and_a_private_tmp_directory_second()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Unix permissions.");

        var root = Path.Combine(Path.GetTempPath(), "bakabase-sockdir-" + Guid.NewGuid().ToString("N"));
        var run = Path.Combine(root, "run");
        var tmp = Path.Combine(root, "tmp");
        Directory.CreateDirectory(run);
        Directory.CreateDirectory(tmp);
        const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        try
        {
            Assert.IsNull(ActivationChannel.ResolveUnixSocketDirectory(null, run, tmp), "no uid, no directory");

            // No runtime directory: one of our own in the shared temp directory, private.
            var shared = ActivationChannel.ResolveUnixSocketDirectory(4242, run, tmp);
            Assert.AreEqual(Path.Combine(tmp, "bakabase-4242"), shared);
            Assert.AreEqual(Private, File.GetUnixFileMode(shared!));

            // Found again, and made private again if something loosened it.
            File.SetUnixFileMode(shared!, Private | UnixFileMode.GroupRead | UnixFileMode.OtherWrite);
            Assert.AreEqual(shared, ActivationChannel.ResolveUnixSocketDirectory(4242, run, tmp));
            Assert.AreEqual(Private, File.GetUnixFileMode(shared!));

            // A link or a file in its place is someone else's doing: not used.
            Directory.CreateSymbolicLink(Path.Combine(tmp, "bakabase-4343"), run);
            Assert.IsNull(ActivationChannel.ResolveUnixSocketDirectory(4343, run, tmp));
            File.WriteAllText(Path.Combine(tmp, "bakabase-4444"), "x");
            Assert.IsNull(ActivationChannel.ResolveUnixSocketDirectory(4444, run, tmp));

            // logind's directory for the uid wins whenever it exists.
            Directory.CreateDirectory(Path.Combine(run, "4242"));
            Assert.AreEqual(Path.Combine(run, "4242"), ActivationChannel.ResolveUnixSocketDirectory(4242, run, tmp));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    [TestMethod]
    public void The_effective_uid_is_the_second_number_of_the_Uid_line()
    {
        Assert.AreEqual(1001u, ActivationChannel.ParseEffectiveUid(
            ["Name:\tbakabase", "Uid:\t1000\t1001\t1000\t1000", "Gid:\t100\t100\t100\t100"]));
        Assert.IsNull(ActivationChannel.ParseEffectiveUid(["Name:\tbakabase"]));
        Assert.IsNull(ActivationChannel.ParseEffectiveUid(["Uid:\tnope"]));
        Assert.AreEqual(4294967294u, ActivationChannel.ParseEffectiveUid(["Uid:\t4294967294\t4294967294"]),
            "uids are unsigned; a large one is still a uid");
    }

    [TestMethod]
    public async Task A_message_reaches_the_server_on_the_same_channel()
    {
        var name = ActivationChannel.GetName(UniqueDir());
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = ActivationServer.Start(name, m => received.TrySetResult(m));

        Assert.IsTrue(await ActivationChannel.TrySendAsync(name, ActivationChannel.ShowMessage, TimeSpan.FromSeconds(5)));
        Assert.AreEqual(ActivationChannel.ShowMessage,
            await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        // And again: the server goes back to listening after each message.
        var second = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        received = second;
        Assert.IsTrue(await ActivationChannel.TrySendAsync(name, "SHOW", TimeSpan.FromSeconds(5)));
        Assert.AreEqual("SHOW", await second.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    public async Task Nobody_listening_is_a_failed_send_not_a_hang()
    {
        var started = DateTime.UtcNow;
        Assert.IsFalse(await ActivationChannel.TrySendAsync(ActivationChannel.GetName(UniqueDir()), "SHOW",
            TimeSpan.FromMilliseconds(500)));
        Assert.IsTrue(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task A_socket_path_the_platform_cannot_use_is_a_failed_send_not_a_crash()
    {
        // Past sun_path .NET throws ArgumentOutOfRangeException; a long TMPDIR used to produce
        // one, and it escaped the refused launch's hand-off as an unhandled exception.
        Assert.IsFalse(await ActivationChannel.TrySendAsync(new string('x', 200), "SHOW",
            TimeSpan.FromMilliseconds(500)));
    }

    [TestMethod]
    public void The_retry_delay_doubles_and_caps()
    {
        Assert.AreEqual(TimeSpan.Zero, ActivationServer.RetryDelay(0));
        Assert.AreEqual(TimeSpan.FromMilliseconds(250), ActivationServer.RetryDelay(1));
        Assert.AreEqual(TimeSpan.FromMilliseconds(500), ActivationServer.RetryDelay(2));
        Assert.AreEqual(TimeSpan.FromMilliseconds(1000), ActivationServer.RetryDelay(3));
        Assert.AreEqual(TimeSpan.FromSeconds(16), ActivationServer.RetryDelay(7));
        Assert.AreEqual(TimeSpan.FromSeconds(30), ActivationServer.RetryDelay(8));
        Assert.AreEqual(TimeSpan.FromSeconds(30), ActivationServer.RetryDelay(int.MaxValue));
    }

    [TestMethod]
    public async Task Failures_back_off_and_a_success_resets_the_backoff()
    {
        var script = new Queue<Func<string?>>(new Func<string?>[]
        {
            () => throw new IOException("busy"),
            () => throw new IOException("busy"),
            () => throw new IOException("busy"),
            () => "SHOW",
            () => throw new UnauthorizedAccessException("denied"),
        });
        var delays = new List<TimeSpan>();
        var messages = new List<string>();
        using var cts = new CancellationTokenSource();

        await ActivationServer.RunAsync(
            _ =>
            {
                if (script.Count == 0)
                {
                    cts.Cancel();
                    throw new OperationCanceledException(cts.Token);
                }

                return Task.FromResult(script.Dequeue()());
            },
            messages.Add,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            (_, _) => { },
            cts.Token).WaitAsync(TimeSpan.FromSeconds(10));

        CollectionAssert.AreEqual(new[] {"SHOW"}, messages);
        CollectionAssert.AreEqual(new[]
        {
            TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1000),
            TimeSpan.FromMilliseconds(250),
        }, delays);
    }

    [TestMethod]
    public async Task A_channel_that_cannot_be_created_does_not_spin()
    {
        // The Windows failure this replaces: the name was taken (there, by another account's
        // instance), creating the server threw every time, and the loop retried at once, for
        // ever. Here the name is taken by a first server in this process, which makes the
        // second server's every attempt throw the same way on every platform.
        var name = ActivationChannel.GetName(UniqueDir());
        using var first = ActivationServer.Start(name, _ => { });
        await Task.Delay(200);

        var failures = 0;
        using var second = ActivationServer.Start(name, _ => { }, (_, n) => Interlocked.Exchange(ref failures, n));
        await Task.Delay(TimeSpan.FromSeconds(2));

        // 250 + 500 + 1000 ms fit in two seconds: three or four attempts, not thousands.
        Assert.IsTrue(failures is >= 1 and <= 5, $"{failures} failures in 2s");
    }
}
