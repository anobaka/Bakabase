using System.Collections.Generic;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

/// <summary>
/// The pre-8d755c2 AV-source-priority UI persisted <see cref="EnhancerTargetFullOptions"/>
/// rows even for unbound targets so the now-removed <c>PreferredSources</c> field could
/// survive a save round-trip. After the refactor those rows are zombies — they still
/// deserialize (Newtonsoft just drops the unknown <c>PreferredSources</c> property), and
/// they then trip <c>ApplyEnhancementsToResources</c>'s <c>switch (PropertyPool)</c>
/// default arm with <see cref="System.ArgumentOutOfRangeException"/>, taking the
/// Enhancement BTask down on every cycle.
/// </summary>
[TestClass]
public class ResourceProfileEnhancerOptionsTests
{
    [TestMethod]
    public void StripInvalidEnhancerTargetOptions_DropsUnboundEntries()
    {
        var options = new ResourceProfileEnhancerOptions
        {
            Enhancers = new List<EnhancerFullOptions>
            {
                new()
                {
                    EnhancerId = 8,
                    TargetOptions = new List<EnhancerTargetFullOptions>
                    {
                        // Bound — keep.
                        new() { Target = 1, PropertyPool = PropertyPool.Custom, PropertyId = 42 },
                        // Zombie: target set, no binding.
                        new() { Target = 2 },
                        // Pool set but PropertyId missing — also invalid.
                        new() { Target = 3, PropertyPool = PropertyPool.Reserved },
                        // PropertyId set but Pool missing — also invalid.
                        new() { Target = 4, PropertyId = 7 },
                    },
                },
            },
        };

        var dropped = options.StripInvalidEnhancerTargetOptions();

        dropped.Should().Be(3);
        options.Enhancers![0].TargetOptions.Should().HaveCount(1);
        options.Enhancers[0].TargetOptions![0].Target.Should().Be(1);
    }

    [TestMethod]
    public void StripInvalidEnhancerTargetOptions_HandlesNullCollections()
    {
        new ResourceProfileEnhancerOptions().StripInvalidEnhancerTargetOptions().Should().Be(0);

        new ResourceProfileEnhancerOptions
        {
            Enhancers = new List<EnhancerFullOptions> { new() { EnhancerId = 1 } },
        }.StripInvalidEnhancerTargetOptions().Should().Be(0);
    }

    [TestMethod]
    public void ToDomainModel_StripsZombiesFromDeserializedJson()
    {
        // JSON shaped as the pre-refactor persistence would have produced: a Target row with
        // no PropertyPool/PropertyId, just the (now ignored) PreferredSources field.
        const string json = """
            {
              "Enhancers": [
                {
                  "EnhancerId": 8,
                  "TargetOptions": [
                    { "Target": 1, "PropertyPool": 4, "PropertyId": 42 },
                    { "Target": 2, "PreferredSources": ["javbus"] }
                  ]
                }
              ]
            }
            """;

        var dbModel = new ResourceProfileDbModel
        {
            Id = 1,
            Name = "test",
            EnhancerSettingsJson = json,
        };

        var domain = dbModel.ToDomainModel(null);

        domain.EnhancerOptions.Should().NotBeNull();
        domain.EnhancerOptions!.Enhancers.Should().HaveCount(1);
        domain.EnhancerOptions.Enhancers![0].TargetOptions.Should().ContainSingle()
            .Which.Target.Should().Be(1);
    }
}
