using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Extensions;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Implementations;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
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
        public async Task TestParse()
        {
            var di = new ServiceCollection();
            di.AddLogging();
            di.AddSingleton<ThirdPartyHttpRequestLogger>();
            var networkOptions = new MemoryOptionsManager<NetworkOptions>();
            await networkOptions.SaveAsync(new NetworkOptions
                {Proxy = new NetworkOptions.ProxyModel {Mode = NetworkOptions.ProxyMode.UseSystem}});
            di.AddSingleton<IBOptions<NetworkOptions>>(networkOptions);
            di.AddSingleton<BakabaseWebProxy>();
            di.AddBakabaseHttpClient<DLsiteThirdPartyHttpMessageHandler>(InternalOptions.HttpClientNames.DLsite);
            di.AddSingleton<DLsiteClient>();

            var services = di.BuildServiceProvider();
            var client = services.GetRequiredService<DLsiteClient>();
            var id = "RJ01248749";
            var detail = await client.ParseWorkDetailById(id);
            Console.WriteLine(JsonConvert.SerializeObject(detail));
        }
    }
}