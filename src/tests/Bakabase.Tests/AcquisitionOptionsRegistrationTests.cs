using System.Reflection;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Service.Components;
using Bakabase.Service.Components.Acquisition;
using Bakabase.TestKit.Implementations;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Configuration.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bakabase.Tests;

[TestClass]
public class AcquisitionOptionsRegistrationTests
{
    [TestMethod]
    public void ApplicationHostRegistersAcquisitionOptionsForHostedServices()
    {
        var assemblies = new InspectableHost().ConfigurationAssemblies;

        // The test assembly directly references Acquisition, unlike the desktop entry point.
        // Discovery alone could therefore pass even when the production scan list omits it.
        CollectionAssert.Contains(assemblies, typeof(AcquisitionOptions).Assembly);

        var registrations = new ConfigurationRegistrations();
        foreach (var assembly in assemblies)
        {
            registrations.AddApplicationPart(assembly);
        }

        var describer = registrations.DiscoverAllOptionsDescribers(Path.GetTempPath())
            .Single(d => d.OptionsType == typeof(AcquisitionOptions));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{describer.OptionsKey}:Concurrency"] = "3"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        registrations.Configure(services, configuration, describer);
        services.AddHostedService<AcquisitionInboxWatcher>();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IBOptions<AcquisitionOptions>>();

        Assert.AreEqual(3, options.Value.Concurrency);
        Assert.AreSame(options, provider.GetRequiredService<IBOptionsManager<AcquisitionOptions>>());
        Assert.IsInstanceOfType<AcquisitionInboxWatcher>(provider.GetServices<IHostedService>().Single());
    }

    private sealed class InspectableHost() : BakabaseHost(new TestGuiAdapter(), new TestSystemService())
    {
        public Assembly[] ConfigurationAssemblies => AssembliesForGlobalConfigurationRegistrationsScanning;
    }
}
