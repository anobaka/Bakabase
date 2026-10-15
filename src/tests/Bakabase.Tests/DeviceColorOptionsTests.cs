using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Configurations;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Service.Controllers;
using Bakabase.Service.Models.Input;
using Bakabase.Tests.Notices;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Tests;

[TestClass]
public sealed class DeviceColorOptionsTests
{
    [TestMethod]
    public async Task A_device_color_is_shared_with_readers_without_overwriting_other_preferences()
    {
        var options = new StubOptions<UIOptions>(new() { IsMenuCollapsed = true });
        var controller = Controller(options);
        Assert.AreEqual(0, (await controller.SetDeviceColor("owner-a", new() { Color = "#AABBCC" })).Code);
        Assert.AreEqual(0, (await controller.SetDeviceColor("owner-b", new() { Color = "#123456" })).Code);
        await controller.PatchUIOptions(new UIOptionsPatchRequestModel { ShowResourceUsage = false });
        var read = (await Controller(options).GetUIOptions()).Data!;
        Assert.AreEqual("#aabbcc", read.DeviceColors["owner-a"]);
        Assert.AreEqual("#123456", read.DeviceColors["owner-b"]);
        Assert.IsTrue(read.IsMenuCollapsed);
        Assert.IsFalse(read.ShowResourceUsage);
        await controller.SetDeviceColor("owner-a", new() { Color = null });
        Assert.IsFalse(options.Value.DeviceColors.ContainsKey("owner-a"));
        Assert.AreEqual("#123456", options.Value.DeviceColors["owner-b"]);
    }

    [TestMethod]
    public async Task Invalid_values_do_not_change_saved_preferences()
    {
        var options = new StubOptions<UIOptions>(new());
        foreach (var color in new[] { "red", "#fff", "#12345678", "#GG0000", "url(https://example.test)" })
            Assert.AreNotEqual(0, (await Controller(options).SetDeviceColor("owner-a", new() { Color = color })).Code);
        foreach (var id in new[] { "", "../owner", "owner\n", new string('a', 129) })
            Assert.AreNotEqual(0, (await Controller(options).SetDeviceColor(id, new() { Color = "#123456" })).Code);
        Assert.AreEqual(0, options.SaveCount);
    }

    [TestMethod]
    public async Task Storage_is_bounded_but_existing_colors_can_still_be_changed_and_reset()
    {
        var options = new StubOptions<UIOptions>(new()
        {
            DeviceColors = Enumerable.Range(0, 512).ToDictionary(i => "owner-" + i, _ => "#123456")
        });
        var controller = Controller(options);
        Assert.AreNotEqual(0, (await controller.SetDeviceColor("extra", new() { Color = "#aabbcc" })).Code);
        Assert.AreEqual(512, options.Value.DeviceColors.Count);
        Assert.AreEqual(0, (await controller.SetDeviceColor("owner-0", new() { Color = "#aabbcc" })).Code);
        Assert.AreEqual(0, (await controller.SetDeviceColor("owner-1", new() { Color = null })).Code);
        Assert.AreEqual(0, (await controller.SetDeviceColor("extra", new() { Color = "#aabbcc" })).Code);
    }

    [TestMethod]
    public void Existing_options_default_to_automatic_colors()
    {
        var options = JsonConvert.DeserializeObject<UIOptions>("""{"IsMenuCollapsed":true}""")!;
        Assert.AreEqual(0, options.DeviceColors.Count);
    }

    private static OptionsController Controller(StubOptions<UIOptions> options)
    {
        var services = new ServiceCollection().AddSingleton<IBOptionsManagerInternal>(options).BuildServiceProvider();
        return new OptionsController(null!, null!, new BakabaseOptionsManagerPool(services), null!, null!, null!, null!, null!);
    }
}
