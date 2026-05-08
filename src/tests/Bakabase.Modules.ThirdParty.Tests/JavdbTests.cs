using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Modules.ThirdParty.ThirdParties.Javdb;
using Bakabase.Modules.ThirdParty.ThirdParties.Javdb.Models;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.Tests
{
    [TestClass]
    public class JavdbTests
    {
        // Integration tests — hit live Javdb. Ignored by default; remove [Ignore] to
        // run manually after a parser change. Replace the [DataRow] numbers with codes
        // you want to verify (e.g. ones a user reported a parsing issue with).
        [DataTestMethod]
        [DataRow("SSIS-001")]
        [DataRow("PRED-100")]
        [Ignore("Manual integration test against live Javdb.")]
        public async Task SearchAndParseVideo_ReturnsPopulatedDetail(string number)
        {
            var client = BuildClient();

            var detail = await client.SearchAndParseVideo(number);

            Console.WriteLine(JsonConvert.SerializeObject(detail, Formatting.Indented));

            Assert.IsNotNull(detail, $"Expected non-null detail for {number}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(detail.Title), $"Expected non-empty Title for {number}");
            AssertParsedFieldsAreNotDuplicated(detail, number);
        }

        [TestMethod]
        [Ignore("Manual integration test against live Javdb.")]
        public async Task SearchAndParseVideo_NonExistentId_ReturnsNull()
        {
            var client = BuildClient();

            var detail = await client.SearchAndParseVideo("ZZZ-99999");

            Assert.IsNull(detail);
        }

        internal static void AssertParsedFieldsAreNotDuplicated(JavdbVideoDetail detail, string number)
        {
            ParsingDuplicationAssert.NotDuplicated(detail.Series, nameof(detail.Series), number);
            ParsingDuplicationAssert.NotDuplicated(detail.Title, nameof(detail.Title), number);
            ParsingDuplicationAssert.NotDuplicated(detail.OriginalTitle, nameof(detail.OriginalTitle), number);
            ParsingDuplicationAssert.NotDuplicated(detail.Studio, nameof(detail.Studio), number);
            ParsingDuplicationAssert.NotDuplicated(detail.Publisher, nameof(detail.Publisher), number);
        }

        private static JavdbClient BuildClient()
        {
            var di = new ServiceCollection();
            di.AddLogging();
            di.AddHttpClient(InternalOptions.HttpClientNames.Default,
                c => c.DefaultRequestHeaders.Add("User-Agent", InternalOptions.DefaultHttpUserAgent));
            di.AddSingleton<JavdbClient>();

            return di.BuildServiceProvider().GetRequiredService<JavdbClient>();
        }
    }
}
