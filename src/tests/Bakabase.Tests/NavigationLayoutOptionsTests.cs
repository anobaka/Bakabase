using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Configurations;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Service.Controllers;
using Bakabase.Tests.Notices;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Tests;

[TestClass]
public sealed class NavigationLayoutOptionsTests
{
    private static UINavigationLayout Layout() => new()
    {
        Items =
        [
            new() { Id = "menu:menu.resource", Hidden = true },
            new()
            {
                Id = "custom:my-tools", Kind = "directory", Name = "我的工具",
                Children = [new() { Id = "menu:menu.fileProcessor" }]
            }
        ],
        KnownCatalogIds = ["menu:menu.resource", "menu:menu.fileProcessor", "directory:menu.tools"],
        DirectoryAliases = new() { ["directory:menu.tools"] = "custom:my-tools" }
    };

    [TestMethod]
    public void Older_options_keep_the_existing_default_navigation()
    {
        var options = JsonConvert.DeserializeObject<UIOptions>("""{"IsMenuCollapsed":true,"ShowResourceUsage":false}""")!;
        Assert.IsNull(options.NavigationLayout);
        Assert.IsTrue(options.IsMenuCollapsed);
        Assert.IsFalse(options.ShowResourceUsage);
    }

    [TestMethod]
    public async Task Saving_navigation_keeps_other_options_and_is_shared_by_readers()
    {
        var manager = new StubOptions<UIOptions>(new UIOptions { IsMenuCollapsed = true, ShowResourceUsage = false });
        var writer = Controller(manager);
        var layout = Layout();
        Assert.IsNull(layout.Validate());
        Assert.AreEqual(0, (await writer.PatchUIOptions(new UIOptionsPatchRequestModel { NavigationLayout = layout })).Code);
        var first = (await writer.GetUIOptions()).Data!;
        var second = (await Controller(manager).GetUIOptions()).Data!;
        Assert.AreEqual(JsonConvert.SerializeObject(layout), JsonConvert.SerializeObject(first.NavigationLayout));
        Assert.AreEqual(JsonConvert.SerializeObject(first.NavigationLayout), JsonConvert.SerializeObject(second.NavigationLayout));
        Assert.IsTrue(first.IsMenuCollapsed);
        Assert.IsFalse(first.ShowResourceUsage);
        Assert.AreEqual(1, manager.SaveCount);
    }

    [TestMethod]
    public async Task Other_option_patches_do_not_clear_navigation()
    {
        var manager = new StubOptions<UIOptions>(new UIOptions { NavigationLayout = Layout() });
        var expected = JsonConvert.SerializeObject(manager.Value.NavigationLayout);
        await Controller(manager).PatchUIOptions(new UIOptionsPatchRequestModel { IsMenuCollapsed = true });
        Assert.AreEqual(expected, JsonConvert.SerializeObject(manager.Value.NavigationLayout));
    }

    [TestMethod]
    public void Unknown_menu_ids_survive_a_json_roundtrip()
    {
        var layout = Layout();
        layout.Items.Add(new UINavigationNode { Id = "menu:future.feature", Hidden = true });
        layout.KnownCatalogIds.Add("menu:future.feature");
        var restored = JsonConvert.DeserializeObject<UINavigationLayout>(JsonConvert.SerializeObject(layout))!;
        Assert.IsNull(restored.Validate());
        Assert.IsTrue(restored.Items.Last().Hidden);
        Assert.AreEqual("menu:future.feature", restored.Items.Last().Id);
    }

    [TestMethod]
    public async Task Invalid_layouts_are_rejected_without_partially_saving_other_options()
    {
        Action<UINavigationLayout>[] invalidChanges =
        [
            layout => layout.Version = 2,
            layout => layout.Items = null!,
            layout => layout.Items.Add(new UINavigationNode { Id = layout.Items[0].Id }),
            layout => layout.Items[0].Name = "Renamed fixed menu",
            layout => layout.Items[0].Kind = "anything",
            layout => layout.Items[0].Id = "bad\nID",
            layout => layout.Items[1].Name = " ",
            layout => layout.Items[1].Name = new string('x', 81),
            layout => layout.Items[1].Children!.Add(new UINavigationNode { Id = "nested-dir", Kind = "directory" }),
            layout => layout.Items[0].Children = [new() { Id = "child" }],
            layout => layout.KnownCatalogIds = Enumerable.Repeat("id", 2049).ToList(),
            layout => layout.DirectoryAliases = Enumerable.Range(0, 513).ToDictionary(i => "a" + i, i => "b" + i),
            layout => layout.Items = Enumerable.Range(0, 1025).Select(i => new UINavigationNode { Id = "id" + i }).ToList()
        ];
        foreach (var mutate in invalidChanges)
        {
            var layout = Layout();
            mutate(layout);
            var manager = new StubOptions<UIOptions>(new UIOptions());
            var response = await Controller(manager).PatchUIOptions(new UIOptionsPatchRequestModel
            {
                NavigationLayout = layout, IsMenuCollapsed = true
            });
            Assert.AreNotEqual(0, response.Code);
            Assert.AreEqual(0, manager.SaveCount);
            Assert.IsFalse(manager.Value.IsMenuCollapsed);
        }
    }

    private static OptionsController Controller(StubOptions<UIOptions> options)
    {
        var services = new ServiceCollection().AddSingleton<IBOptionsManagerInternal>(options).BuildServiceProvider();
        return new OptionsController(null!, null!, new BakabaseOptionsManagerPool(services), null!, null!, null!, null!, null!);
    }
}
