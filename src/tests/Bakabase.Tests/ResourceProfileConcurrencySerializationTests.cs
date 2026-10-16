using System.Globalization;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Service.Controllers;
using Bakabase.Service.Models.View;
using Bakabase.TestKit.Utils;
using Bakabase.Tests.DataSync.CustomProperties;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.Tests;

[TestClass]
public sealed class ResourceProfileConcurrencySerializationTests
{
    private static JsonSerializerSettings HttpSettings() => NewtonsoftDefaults.App();

    private static JObject HttpJson(object value)
    {
        var json = JsonConvert.SerializeObject(value, HttpSettings());
        // The browser keeps timestamp fields as strings; do not let JToken parse
        // and reformat the token before constructing the next HTTP request.
        using var text = new StringReader(json);
        using var reader = new JsonTextReader(text) {DateParseHandling = DateParseHandling.None};
        return JObject.Load(reader);
    }

    private static T Request<T>(JObject body) => JsonConvert.DeserializeObject<T>(body.ToString(Formatting.None), HttpSettings())!;

    [TestMethod]
    public void Profile_concurrency_timestamp_preserves_every_tick_under_real_server_date_format()
    {
        var precise = new DateTime(2026, 10, 15, 1, 2, 3, DateTimeKind.Utc).AddTicks(1234567);
        foreach (var kind in new[] {DateTimeKind.Utc, DateTimeKind.Unspecified})
        {
            var value = DateTime.SpecifyKind(precise, kind);
            var wire = HttpJson(new ResourceProfileViewModel {Name = "Exact", CreatedAt = value, UpdatedAt = value});
            Assert.AreEqual("2026-10-15T01:02:03.1234567Z", wire["updatedAt"]!.Value<string>());
            Assert.AreEqual("2026-10-15 01:02:03.123", wire["createdAt"]!.Value<string>(),
                "Ordinary display dates must keep the existing application format.");
            var request = Request<ResourceScopeBindInput>(new JObject
                { ["scopeId"] = 1, ["expectedUpdatedAt"] = wire["updatedAt"]!.DeepClone() });
            Assert.AreEqual(value.Ticks, request.ExpectedUpdatedAt.Ticks);
            Assert.AreEqual(DateTimeKind.Utc, request.ExpectedUpdatedAt.Kind);
        }
    }

    [TestMethod]
    public async Task Http_create_bind_and_independent_rule_save_round_trip_exact_tokens_and_reject_stale_or_neighbor_ticks()
    {
        var services = await TestServiceBuilder.BuildServiceProvider();
        try
        {
            var profiles = services.GetRequiredService<IResourceProfileService>();
            var scopes = services.GetRequiredService<IResourceScopeService>();
            var localizer = services.GetRequiredService<IPropertyLocalizer>();
            var profileController = new ResourceProfileController(profiles, localizer);
            var scopeController = new ResourceScopeController(scopes, profiles,
                services.GetRequiredService<IPropertyService>(), localizer);
            var createdResponse = await profileController.Add(new() {Name = "HTTP rule"});
            var created = createdResponse.Data!;
            var createWire = HttpJson(createdResponse);
            var originalToken = createWire["data"]!["updatedAt"]!.DeepClone();
            var scope = await scopes.Add("All resources", null, null, null);
            var bindInput = Request<ResourceScopeBindInput>(new JObject
                { ["scopeId"] = scope.Id, ["expectedUpdatedAt"] = originalToken });
            Assert.AreEqual(created.UpdatedAt.Ticks, bindInput.ExpectedUpdatedAt.Ticks);

            var neighborToken = DateTime.SpecifyKind(created.UpdatedAt.AddTicks(1), DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
            var neighborInput = Request<ResourceScopeBindInput>(new JObject
                { ["scopeId"] = scope.Id, ["expectedUpdatedAt"] = neighborToken });
            Assert.AreEqual(409, ((ObjectResult)await scopeController.Bind(created.Id, neighborInput)).StatusCode,
                "A token differing by one tick must not be accepted as an entire-millisecond range.");

            var boundResult = await scopeController.Bind(created.Id, bindInput);
            Assert.IsInstanceOfType<OkObjectResult>(boundResult, "POST's token must work on the first PATCH without a reload.");
            var bound = (ResourceProfileViewModel)((OkObjectResult)boundResult).Value!;
            Assert.AreEqual(scope.Id, bound.ResourceScopeId);
            var stored = await services.GetRequiredService<BakabaseDbContext>().Set<ResourceProfileDbModel>()
                .AsNoTracking().SingleAsync(p => p.Id == created.Id);
            Assert.AreEqual(DateTimeKind.Unspecified, stored.UpdatedAt.Kind,
                "SQLite's zone-free UTC must not be interpreted as browser/server local time.");
            Assert.AreEqual(stored.UpdatedAt.Ticks, bound.UpdatedAt.Ticks);
            var boundWire = HttpJson(bound with {UpdatedAt = stored.UpdatedAt});
            var ruleInput = Request<ResourceProfileRuleInput>(new JObject
            {
                ["kind"] = (int)ResourceProfileRuleKind.OpeningMode,
                ["valueJson"] = "\"browser\"",
                ["expectedUpdatedAt"] = boundWire["updatedAt"]!.DeepClone()
            });
            var changedResult = await scopeController.Rule(created.Id, ruleInput);
            Assert.IsInstanceOfType<OkObjectResult>(changedResult);
            var changed = (ResourceProfileViewModel)((OkObjectResult)changedResult).Value!;
            Assert.AreEqual("browser", changed.OpeningMode);
            Assert.AreEqual(409, ((ObjectResult)await scopeController.Rule(created.Id,
                ruleInput with {ValueJson = "\"native\""})).StatusCode);
            Assert.AreEqual(409, ((ObjectResult)await scopeController.Bind(created.Id, bindInput)).StatusCode);
            Assert.AreEqual("browser", (await profiles.Get(created.Id))!.OpeningMode);

            var changedWire = HttpJson(changed);
            var latestInput = Request<ResourceProfileRuleInput>(new JObject
            {
                ["kind"] = (int)ResourceProfileRuleKind.OpeningMode,
                ["valueJson"] = "\"native\"",
                ["expectedUpdatedAt"] = changedWire["updatedAt"]!.DeepClone()
            });
            Assert.IsInstanceOfType<OkObjectResult>(await scopeController.Rule(created.Id, latestInput));
            Assert.AreEqual("native", (await profiles.Get(created.Id))!.OpeningMode);
        }
        finally
        {
            if (services is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
            else if (services is IDisposable disposable) disposable.Dispose();
        }
    }
}
