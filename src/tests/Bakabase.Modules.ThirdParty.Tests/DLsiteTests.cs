using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Extensions;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Components.Http;
using Bakabase.Modules.ThirdParty.ThirdParties.DLsite;
using Bakabase.Service.Extensions;
using Bootstrap.Components.Configuration;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.Tests
{
    [TestClass]
    public class DLsiteTests
    {
        [TestMethod]
        [Ignore("Manual integration test against live DLsite. The DI wiring references the abstract " +
                "BakabaseOptionsBasedThirdPartyHttpMessageHandler base after the July-2025 refactor and " +
                "additionally requires real account cookies. Run manually after restoring local DI + cookies.")]
        public async Task TestParse()
        {
            var di = new ServiceCollection();
            di.AddLogging();
            di.AddSingleton<ThirdPartyHttpRequestLogger>();
            var networkOptions = new MemoryOptionsManager<NetworkOptions>();
            await networkOptions.SaveAsync(new NetworkOptions
                { Proxy = new NetworkOptions.ProxyModel { Mode = NetworkOptions.ProxyMode.UseSystem } });
            di.AddSingleton<IBOptions<NetworkOptions>>(networkOptions);
            di.AddSingleton<BakabaseWebProxy>();
            di.AddBakabaseHttpClient<BakabaseOptionsBasedThirdPartyHttpMessageHandler<DLsiteOptions>>(InternalOptions
                .HttpClientNames.DLsite);
            di.AddSingleton<DLsiteClient>();

            var services = di.BuildServiceProvider();
            var client = services.GetRequiredService<DLsiteClient>();
            var id = "RJ01248749";
            var detail = await client.ParseWorkDetailById(id);
            Console.WriteLine(JsonConvert.SerializeObject(detail));
        }
    }
}