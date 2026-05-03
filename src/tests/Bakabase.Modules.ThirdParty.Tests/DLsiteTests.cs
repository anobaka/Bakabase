using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Modules.ThirdParty.ThirdParties.DLsite;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.Tests
{
    [TestClass]
    public class DLsiteTests
    {
        // Pure-function tests — safe to run in CI.
        [DataTestMethod]
        [DataRow("RJ01248749", "maniax")]
        [DataRow("RJ01348097", "maniax")]
        [DataRow("rj01348097", "maniax")] // case-insensitive
        [DataRow("BJ01345678", "books")]
        [DataRow("VJ006100", "pro")]
        [DataRow("XX12345678", "home")] // unknown prefix → fallback
        [DataRow("", "home")]
        [DataRow("R", "home")]
        public void GetCategoryByWorkId_RoutesByPrefix(string id, string expected)
        {
            Assert.AreEqual(expected, DLsiteClient.GetCategoryByWorkId(id));
        }

        // Integration tests — hit live DLsite. Ignored by default; remove [Ignore] to
        // run manually after a parser/auth change. The DI wiring intentionally bypasses
        // the production DLsiteHttpMessageHandler so the test does not require
        // ThirdPartyHttpRequestLogger / IThirdPartyCookieContainer / a real options
        // manager. The fix in ParseWorkDetailById handles redirects manually, so the
        // default HttpClient (AllowAutoRedirect=true) and the production handler
        // (AllowAutoRedirect=false) both produce the same result.
        [DataTestMethod]
        [DataRow("RJ01248749")]
        [DataRow("RJ01348097")]
        [Ignore("Manual integration test against live DLsite. Run locally to verify parser changes.")]
        public async Task ParseWorkDetailById_ReturnsPopulatedDetail(string id)
        {
            var client = BuildClient();

            var detail = await client.ParseWorkDetailById(id);

            Console.WriteLine(JsonConvert.SerializeObject(detail, Formatting.Indented));

            Assert.IsNotNull(detail, $"Expected non-null detail for {id}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(detail.Name), $"Expected non-empty Name for {id}");
            Assert.IsTrue((detail.CoverUrls?.Length ?? 0) > 0, $"Expected at least one cover URL for {id}");
        }

        [TestMethod]
        [Ignore("Manual integration test against live DLsite.")]
        public async Task ParseWorkDetailById_NonExistentId_ReturnsNull()
        {
            var client = BuildClient();

            var detail = await client.ParseWorkDetailById("RJ99999999");

            Assert.IsNull(detail);
        }

        private static DLsiteClient BuildClient()
        {
            var di = new ServiceCollection();
            di.AddLogging();
            di.AddHttpClient(InternalOptions.HttpClientNames.DLsite,
                c => c.DefaultRequestHeaders.Add("User-Agent", InternalOptions.DefaultHttpUserAgent));
            di.AddSingleton<DLsiteClient>();

            return di.BuildServiceProvider().GetRequiredService<DLsiteClient>();
        }
    }
}
