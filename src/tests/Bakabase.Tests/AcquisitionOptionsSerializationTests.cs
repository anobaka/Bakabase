using System.Text;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Service.Components.Acquisition.Planning;
using Bootstrap.Components.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionOptionsSerializationTests
{
    private static readonly string[] Priority = ["pikpak", "baidu", "http", "mega", "terabox"];

    [TestMethod]
    public async Task Saved_priority_survives_json_reads_and_unrelated_options_updates_without_default_prefixes()
    {
        var manager = new MemoryOptionsManager<AcquisitionOptions>();
        manager.Save(new() {SourceProviderPriority = Priority.ToList(), InboxDirectory = "/inbox", Concurrency = 3});

        for (var i = 0; i < 3; i++)
        {
            // JsonOptionsManager reads through JObject; mutation saves also clone through Newtonsoft.
            var restored = JObject.Parse(JsonConvert.SerializeObject(manager.Value)).ToObject<AcquisitionOptions>()!;
            CollectionAssert.AreEqual(Priority, restored.SourceProviderPriority);
            manager.Save(restored);
            await manager.SaveAsync(options => options.TryRecentPasswords = false);
            CollectionAssert.AreEqual(Priority, manager.Value.SourceProviderPriority);
            Assert.AreEqual("/inbox", manager.Value.InboxDirectory);
            Assert.AreEqual(3, manager.Value.Concurrency);
            Assert.IsFalse(manager.Value.TryRecentPasswords);
        }
    }

    [TestMethod]
    public void System_text_json_replaces_priority_even_with_population_enabled()
    {
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PreferredObjectCreationHandling = System.Text.Json.Serialization.JsonObjectCreationHandling.Populate
        };
        var json = System.Text.Json.JsonSerializer.Serialize(new AcquisitionOptions {SourceProviderPriority = Priority.ToList()});

        var restored = System.Text.Json.JsonSerializer.Deserialize<AcquisitionOptions>(json, options)!;

        CollectionAssert.AreEqual(Priority, restored.SourceProviderPriority);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Configuration_monitor_preserves_explicit_priority_including_empty_order(bool empty)
    {
        var expected = empty ? Array.Empty<string>() : Priority;
        var configured = new AcquisitionOptions
        {
            SourceProviderPriority = expected.ToList(), InboxDirectory = "/inbox", Concurrency = 4,
            DeliveryDirectoryTemplate = "{TaskId} - {Title}"
        };
        var json = JsonConvert.SerializeObject(configured);
        CollectionAssert.AreEqual(expected, JsonConvert.DeserializeObject<AcquisitionOptions>(json)!.SourceProviderPriority);
        CollectionAssert.AreEqual(expected, System.Text.Json.JsonSerializer.Deserialize<AcquisitionOptions>(json,
            System.Text.Json.JsonSerializerOptions.Web)!.SourceProviderPriority);
        using var services = Configure(json);
        var monitor = services.GetRequiredService<IOptionsMonitor<AcquisitionOptions>>();

        CollectionAssert.AreEqual(expected, monitor.CurrentValue.SourceProviderPriority);
        Assert.AreEqual("/inbox", monitor.CurrentValue.InboxDirectory);
        Assert.AreEqual(4, monitor.CurrentValue.Concurrency);
        Assert.AreEqual("{TaskId} - {Title}", monitor.CurrentValue.DeliveryDirectoryTemplate);
    }

    [TestMethod]
    public void Configuration_reload_replaces_the_previous_priority_without_growing_the_list()
    {
        var key = AcquisitionOptions.ConfigurationKey + ":" + nameof(AcquisitionOptions.SourceProviderPriority);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            Priority.Select((provider, index) => new KeyValuePair<string, string?>($"{key}:{index}", provider))).Build();
        using var services = Configure(configuration);
        var monitor = services.GetRequiredService<IOptionsMonitor<AcquisitionOptions>>();
        CollectionAssert.AreEqual(Priority, monitor.CurrentValue.SourceProviderPriority);

        string[] revised = ["http", "mega", "baidu", "pikpak", "terabox"];
        for (var i = 0; i < revised.Length; i++) configuration[$"{key}:{i}"] = revised[i];
        configuration.Reload();

        CollectionAssert.AreEqual(revised, monitor.CurrentValue.SourceProviderPriority);
        configuration.Reload();
        CollectionAssert.AreEqual(revised, monitor.CurrentValue.SourceProviderPriority);
    }

    [TestMethod]
    public void Legacy_options_without_priority_keep_defaults_in_both_serializers_and_configuration_binding()
    {
        const string legacy = """{"Concurrency":3}""";
        using var services = Configure(legacy);
        var expected = new AcquisitionOptions().SourceProviderPriority;

        foreach (var restored in new[]
        {
            JsonConvert.DeserializeObject<AcquisitionOptions>(legacy)!,
            System.Text.Json.JsonSerializer.Deserialize<AcquisitionOptions>(legacy)!,
            services.GetRequiredService<IOptionsMonitor<AcquisitionOptions>>().CurrentValue
        })
        {
            CollectionAssert.AreEqual(expected, restored.SourceProviderPriority);
            Assert.AreEqual(3, restored.Concurrency);
        }
    }

    private static ServiceProvider Configure(string optionsJson)
    {
        var json = $$"""{"{{AcquisitionOptions.ConfigurationKey}}":{{optionsJson}}}""";
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        return Configure(configuration);
    }

    private static ServiceProvider Configure(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOptions<AcquisitionOptions>().Bind(configuration.GetSection(AcquisitionOptions.ConfigurationKey));
        services.AddAcquisitionPlanning();
        return services.BuildServiceProvider();
    }
}
