using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Modules.ThirdParty.ThirdParties.Bangumi;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.Tests
{
    [TestClass]
    [Ignore("Manual integration test against live Bangumi.")]
    public class BangumiTests
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
            var keyword = "�r���ܥ��äȥ������Z�ǥǥ���O�Υ��`��㤵��";
            var detail = await client.SearchAndParseFirst(keyword);
            Console.WriteLine(JsonConvert.SerializeObject(detail));
        }
    }
}