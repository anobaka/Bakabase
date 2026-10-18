using System;
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
public sealed class MediaLibraryOrderOptionsTests
{
    [TestMethod]
    public async Task Shared_order_survives_json_roundtrip_and_unrelated_option_patches()
    {
        var manager = new StubOptions<UIOptions>(new UIOptions { IsMenuCollapsed = true, ShowResourceUsage = false });
        var controller = Controller(manager);
        var order = new UIMediaLibraryOrder { Keys = ["bakabase:library:[\"local\",\"epoch\",\"2\"]", "bakabase:library:[\"remote\",\"epoch\",\"2\"]"] };

        Assert.AreEqual(0, (await controller.PatchUIOptions(new UIOptionsPatchRequestModel { MediaLibraryOrder = order })).Code);
        await controller.PatchUIOptions(new UIOptionsPatchRequestModel { HideResourceCovers = true });
        var restored = JsonConvert.DeserializeObject<UIOptions>(JsonConvert.SerializeObject((await Controller(manager).GetUIOptions()).Data))!;
        CollectionAssert.AreEqual(order.Keys, restored.MediaLibraryOrder!.Keys);
        Assert.IsTrue(restored.IsMenuCollapsed);
        Assert.IsFalse(restored.ShowResourceUsage);
        Assert.IsTrue(restored.HideResourceCovers);
    }

    [TestMethod]
    public async Task Invalid_order_is_rejected_before_saving_any_other_options()
    {
        string[][] invalid = [["same", "same"], [""], ["bad\nkey"], [new string('x', 513)], Enumerable.Range(0, 10001).Select(i => i.ToString()).ToArray()];

        foreach (var keys in invalid)
        {
            var manager = new StubOptions<UIOptions>(new UIOptions());
            var response = await Controller(manager).PatchUIOptions(new UIOptionsPatchRequestModel
            {
                MediaLibraryOrder = new UIMediaLibraryOrder { Keys = keys.ToList() }, IsMenuCollapsed = true
            });
            Assert.AreNotEqual(0, response.Code);
            Assert.AreEqual(0, manager.SaveCount);
            Assert.IsFalse(manager.Value.IsMenuCollapsed);
        }
    }

    [TestMethod]
    public async Task Empty_order_restores_the_unconfigured_default()
    {
        var manager = new StubOptions<UIOptions>(new UIOptions
        {
            MediaLibraryOrder = new UIMediaLibraryOrder { Keys = ["library"] },
            IsMenuCollapsed = true
        });

        var response = await Controller(manager).PatchUIOptions(new UIOptionsPatchRequestModel
        {
            MediaLibraryOrder = new UIMediaLibraryOrder()
        });

        Assert.AreEqual(0, response.Code);
        Assert.IsNull(manager.Value.MediaLibraryOrder);
        Assert.IsTrue(manager.Value.IsMenuCollapsed);
    }

    [TestMethod]
    public void Older_options_keep_default_catalog_order_and_empty_order_can_reset_preferences()
    {
        Assert.IsNull(JsonConvert.DeserializeObject<UIOptions>("{}")!.MediaLibraryOrder);
        Assert.IsNull(new UIMediaLibraryOrder().Validate());
        Assert.IsNotNull(new UIMediaLibraryOrder { Keys = null! }.Validate());
    }

    private static OptionsController Controller(StubOptions<UIOptions> options)
    {
        var services = new ServiceCollection().AddSingleton<IBOptionsManagerInternal>(options).BuildServiceProvider();
        return new OptionsController(null!, null!, new BakabaseOptionsManagerPool(services), null!, null!, null!, null!, null!);
    }
}
