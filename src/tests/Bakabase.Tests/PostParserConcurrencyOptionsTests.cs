using Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;
using Bakabase.InsideWorld.Business.Components.Configurations;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Service.Controllers;
using Bootstrap.Models.Constants;
using Newtonsoft.Json;
using Bakabase.Tests.Notices;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class PostParserConcurrencyOptionsTests
{
    [TestMethod]
    public void Existing_options_receive_conservative_concurrency_defaults()
    {
        var options = JsonConvert.DeserializeObject<ThirdPartyOptions>("""{"AutomaticallyParsingPosts":true}""")!;
        Assert.AreEqual(10, options.PostParserMaxConcurrency);
        Assert.AreEqual(1, options.PostParserAiMaxConcurrency);
        Assert.IsTrue(options.AutomaticallyParsingPosts);
    }

    [TestMethod]
    public async Task Partial_concurrency_update_preserves_other_limits_and_auto_start_preference()
    {
        var manager = new StubOptions<ThirdPartyOptions>(new()
        {
            AutomaticallyParsingPosts = true, PostParserMaxConcurrency = 5, PostParserAiMaxConcurrency = 2
        });
        using var services = new ServiceCollection().BuildServiceProvider();
        var pool = new BakabaseOptionsManagerPool(services);
        pool.AllOptionsManagers[typeof(ThirdPartyOptions)] = manager;
        var controller = new OptionsController(null!, null!, pool, null!, null!, null!, null!);

        var response = await controller.PatchThirdPartyOptions(new() {PostParserMaxConcurrency = 3});
        Assert.AreEqual((int)ResponseCode.Success, response.Code);
        Assert.AreEqual(3, manager.Value.PostParserMaxConcurrency);
        Assert.AreEqual(2, manager.Value.PostParserAiMaxConcurrency);
        Assert.IsTrue(manager.Value.AutomaticallyParsingPosts);

        response = await controller.PatchThirdPartyOptions(new() {PostParserAiMaxConcurrency = 1});
        Assert.AreEqual((int)ResponseCode.Success, response.Code);
        Assert.AreEqual(3, manager.Value.PostParserMaxConcurrency);
        Assert.AreEqual(1, manager.Value.PostParserAiMaxConcurrency);
        Assert.AreEqual(2, manager.SaveCount);
    }

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(10, 0)]
    [DataRow(-1, 1)]
    [DataRow(10, -1)]
    public async Task Nonpositive_limits_reject_patch_and_put_before_persisting(int total, int ai)
    {
        // No options manager is supplied: invalid requests must return before any save or mutation.
        var controller = new OptionsController(null!, null!, null!, null!, null!, null!, null!);
        var patch = await controller.PatchThirdPartyOptions(new ThirdPartyOptionsPatchInput
        {
            PostParserMaxConcurrency = total, PostParserAiMaxConcurrency = ai, AutomaticallyParsingPosts = true
        });
        var put = await controller.PutThirdPartyOptions(new ThirdPartyOptions
        {
            PostParserMaxConcurrency = total, PostParserAiMaxConcurrency = ai
        });
        Assert.AreEqual((int)ResponseCode.InvalidPayloadOrOperation, patch.Code);
        Assert.AreEqual((int)ResponseCode.InvalidPayloadOrOperation, put.Code);
    }
}
