using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Modules.ThirdParty.Bangumi;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.Tests
{
    [TestClass]
    public class Bangumi
    {
        [TestMethod]
        public async Task TestSearchAndParseFirst()
        {
            var di = new ServiceCollection();
            di.AddLogging();
            di.AddHttpClient();
            di.AddSingleton<BangumiClient>();


            var services = di.BuildServiceProvider();
            var client = services.GetRequiredService<BangumiClient>();
            var keyword = "ïr°©•‹•Ω•√§»•Ì•∑•¢’Z§«•«•Ï§ÎÎO§Œ•¢©`•Í•„§µ§Û";
            var detail = await client.SearchAndParseFirst(keyword);
            Console.WriteLine(JsonConvert.SerializeObject(detail));
        }
    }
}