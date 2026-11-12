using System.Globalization;
using System.Text.Json;
using Bakabase.Service.Components.CloudDownloads.Rclone;
using Bootstrap.Models.ResponseModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class RcloneAuthorizationSerializationTests
{
    [DataTestMethod]
    [DataRow(0)]
    [DataRow(8)]
    [DataRow(-7)]
    public void HttpAuthorizationExpiryPreservesItsInstantForBrowsersInOtherTimezones(int sourceOffsetHours)
    {
        var expiresAt = new DateTimeOffset(2026, 10, 20, 13, 9, 10, 681, TimeSpan.Zero)
            .ToOffset(TimeSpan.FromHours(sourceOffsetHours));
        var response = new SingletonResponse<RcloneAuthorizationSessionViewModel>(new RcloneAuthorizationSessionViewModel
        {
            Id = "session", AccountId = "account", Status = "awaitingInput", ExpiresAt = expiresAt,
            Browser = new() {Status = "awaitingCallback", ExpiresAt = expiresAt}
        });
        // The actual MVC formatter uses Json.NET and this display date format, which intentionally
        // omits offsets elsewhere. A System.Text.Json-only test would miss this regression.
        var settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
                {NamingStrategy = new CamelCaseNamingStrategy {ProcessDictionaryKeys = false}},
            DateFormatString = "yyyy-MM-dd HH:mm:ss.fff",
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };
        using var wire = JsonDocument.Parse(JsonConvert.SerializeObject(response, settings));
        var data = wire.RootElement.GetProperty("data");
        foreach (var encoded in new[] {data.GetProperty("expiresAt").GetString()!, data.GetProperty("browser").GetProperty("expiresAt").GetString()!})
        {
            StringAssert.Contains(encoded, "T");
            StringAssert.Matches(encoded, new System.Text.RegularExpressions.Regex(@"(?:Z|[+-]\d{2}:\d{2})$"));
            var instant = DateTimeOffset.Parse(encoded, CultureInfo.InvariantCulture, DateTimeStyles.None);
            Assert.AreEqual(expiresAt.UtcDateTime, instant.UtcDateTime);
            var browserNow = new DateTimeOffset(2026, 10, 20, 21, 8, 10, 681, TimeSpan.FromHours(8));
            Assert.AreEqual(TimeSpan.FromMinutes(1), instant - browserNow,
                "An Asia/Shanghai browser must not treat the UTC timestamp as a local time eight hours earlier.");
        }
        var roundTrip = JsonConvert.DeserializeObject<SingletonResponse<RcloneAuthorizationSessionViewModel>>(wire.RootElement.GetRawText(), settings)!;
        Assert.AreEqual(expiresAt.UtcDateTime, roundTrip.Data!.ExpiresAt.UtcDateTime);
        Assert.AreEqual(expiresAt.UtcDateTime, roundTrip.Data.Browser!.ExpiresAt.UtcDateTime);
    }
}
