using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.Models.Constants;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.InsideWorld.Business.Components.Configurations;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Service.Components.Notices;
using Bakabase.Service.Controllers;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Bakabase.Tests.Notices;

/// <summary>
/// The server half of the startup notices: which notices an install has read, and whether a
/// fresh install still has to record the upgrade-only ones it shipped with.
/// </summary>
[TestClass]
public sealed class NoticeStateTests
{
    [TestMethod]
    public void Marking_read_adds_each_id_once_and_is_idempotent()
    {
        var notices = new UIOptions.UINoticeOptions();

        Assert.IsTrue(notices.MarkRead(["multi-device", "thin-client-discontinued", "multi-device"]));
        Assert.IsFalse(notices.MarkRead(["thin-client-discontinued"]), "A second window marking the same notice changes nothing.");
        Assert.IsFalse(notices.MarkRead(null));
        CollectionAssert.AreEqual(new[] {"multi-device", "thin-client-discontinued"}, notices.ReadIds);
    }

    [TestMethod]
    public void Marking_read_keeps_unknown_ids_and_ignores_what_cannot_be_an_id()
    {
        var notices = new UIOptions.UINoticeOptions();
        var tooLong = new string('x', UIOptions.UINoticeOptions.MaxIdLength + 1);

        notices.MarkRead([null, "", "   ", tooLong, "  from-a-newer-build  "]);

        // Unknown to this build, but a newer UI may have sent it: dropping it would bring the
        // notice back after a downgrade.
        CollectionAssert.AreEqual(new[] {"from-a-newer-build"}, notices.ReadIds);
    }

    [TestMethod]
    public void Marking_read_stops_growing_at_the_cap()
    {
        var notices = new UIOptions.UINoticeOptions();

        notices.MarkRead(Enumerable.Range(0, UIOptions.UINoticeOptions.MaxReadIds + 10).Select(i => $"n{i}"));

        Assert.AreEqual(UIOptions.UINoticeOptions.MaxReadIds, notices.ReadIds.Count);
    }

    [TestMethod]
    public void The_baseline_is_recorded_only_while_it_is_open()
    {
        var fresh = new UIOptions.UINoticeOptions {BaselinePending = true};

        Assert.IsTrue(fresh.CaptureBaseline(["thin-client-discontinued"]));
        Assert.IsFalse(fresh.BaselinePending);
        CollectionAssert.AreEqual(new[] {"thin-client-discontinued"}, fresh.ReadIds);

        // Closed now, so a later build cannot use it to hide the notices it adds.
        Assert.IsFalse(fresh.CaptureBaseline(["added-later"]));
        CollectionAssert.AreEqual(new[] {"thin-client-discontinued"}, fresh.ReadIds);

        var upgraded = new UIOptions.UINoticeOptions();
        Assert.IsFalse(upgraded.CaptureBaseline(["thin-client-discontinued"]));
        Assert.AreEqual(0, upgraded.ReadIds.Count);
    }

    [TestMethod]
    public void An_options_file_from_before_notices_reads_as_an_install_that_sees_them()
    {
        const string before = """{"UIOptions":{"StartupPage":1,"IsMenuCollapsed":true}}""";

        var deserialized = JsonConvert.DeserializeObject<UIOptions>("""{"StartupPage":1,"IsMenuCollapsed":true}""")!;
        var bound = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(before)))
            .Build()
            .GetSection(nameof(UIOptions))
            .Get<UIOptions>()!;

        foreach (var options in new[] {deserialized, bound})
        {
            Assert.IsNotNull(options.Notices);
            Assert.AreEqual(0, options.Notices.ReadIds.Count);
            Assert.IsFalse(options.Notices.BaselinePending);
        }
    }

    [TestMethod]
    public async Task The_read_endpoint_persists_once_and_answers_the_state()
    {
        var ui = new StubOptions<UIOptions>(new UIOptions());
        var controller = Controller(ui);

        var first = await controller.MarkNoticesRead(["multi-device", "not-in-this-build"]);
        var again = await controller.MarkNoticesRead(["multi-device"]);
        var empty = await controller.MarkNoticesRead(null);

        CollectionAssert.AreEqual(new[] {"multi-device", "not-in-this-build"}, first.Data!.ReadIds);
        CollectionAssert.AreEqual(new[] {"multi-device", "not-in-this-build"}, again.Data!.ReadIds);
        CollectionAssert.AreEqual(new[] {"multi-device", "not-in-this-build"}, empty.Data!.ReadIds);
        Assert.AreEqual(1, ui.SaveCount, "Nothing new to record means nothing to write or push to other windows.");
        CollectionAssert.AreEqual(new[] {"multi-device", "not-in-this-build"}, ui.Value.Notices.ReadIds);
    }

    [TestMethod]
    public async Task The_baseline_endpoint_only_acts_for_a_fresh_install()
    {
        var upgraded = new StubOptions<UIOptions>(new UIOptions());
        var ignored = await Controller(upgraded).CaptureNoticeBaseline(["thin-client-discontinued"]);

        Assert.AreEqual(0, ignored.Data!.ReadIds.Count);
        Assert.AreEqual(0, upgraded.SaveCount);

        var fresh = new StubOptions<UIOptions>(new UIOptions {Notices = new() {BaselinePending = true}});
        var controller = Controller(fresh);
        var captured = await controller.CaptureNoticeBaseline(["thin-client-discontinued"]);
        var repeated = await controller.CaptureNoticeBaseline(["added-later"]);

        Assert.IsFalse(captured.Data!.BaselinePending);
        CollectionAssert.AreEqual(new[] {"thin-client-discontinued"}, repeated.Data!.ReadIds);
        Assert.AreEqual(1, fresh.SaveCount);
        Assert.IsFalse(fresh.Value.Notices.BaselinePending);
    }

    [TestMethod]
    [DataRow(AppConstants.InitialVersion, true, DisplayName = "First start of a fresh install")]
    [DataRow("2.4.0-beta.150", false, DisplayName = "An install that already ran")]
    public async Task The_baseline_opens_only_on_a_fresh_install_first_start(string lastRunVersion, bool opens)
    {
        var ui = new StubOptions<UIOptions>(new UIOptions());
        var app = new StubOptions<AppOptions>(new AppOptions {Version = lastRunVersion});

        await new NoticeBaselineInitializer(app, ui, NullLogger<NoticeBaselineInitializer>.Instance)
            .StartAsync(CancellationToken.None);

        Assert.AreEqual(opens, ui.Value.Notices.BaselinePending);
        Assert.AreEqual(opens ? 1 : 0, ui.SaveCount);
    }

    [TestMethod]
    public async Task A_first_start_that_did_not_finish_leaves_an_open_baseline_as_it_is()
    {
        var ui = new StubOptions<UIOptions>(new UIOptions {Notices = new() {BaselinePending = true}});
        var app = new StubOptions<AppOptions>(new AppOptions {Version = AppConstants.InitialVersion});

        await new NoticeBaselineInitializer(app, ui, NullLogger<NoticeBaselineInitializer>.Instance)
            .StartAsync(CancellationToken.None);

        Assert.IsTrue(ui.Value.Notices.BaselinePending);
        Assert.AreEqual(0, ui.SaveCount);
    }

    private static OptionsController Controller(StubOptions<UIOptions> ui)
    {
        var services = new ServiceCollection().AddSingleton<IBOptionsManagerInternal>(ui).BuildServiceProvider();

        return new OptionsController(null!, null!, new BakabaseOptionsManagerPool(services), null!, null!, null!,
            null!);
    }
}
