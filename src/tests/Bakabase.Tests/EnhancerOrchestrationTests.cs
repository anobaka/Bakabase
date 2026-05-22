using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Enhancer.Components.Enhancers.Regex;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Tests;

/// <summary>
/// End-to-end coverage for resource-enhancement orchestration: EnhancerService runs the
/// (network-free) Regex enhancer, then ApplyEnhancementsToResources writes the extracted
/// value onto the property bound by the resource's profile. Covers the apply-when-bound
/// path, the skip-when-no-binding path, and the skip-when-no-data path.
/// </summary>
[TestClass]
public sealed class EnhancerOrchestrationTests
{
    private const string CaptureRegex = @"\[(?<studio>[^\]]+)\]";

    private string _testRoot = null!;
    private IServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _testRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"EnhancerOrchestrationTests.{DateTime.Now:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testRoot))
        {
            try { Directory.Delete(_testRoot, true); } catch { }
        }
    }

    private async Task<int> SeedResource(string dirName)
    {
        Directory.CreateDirectory(Path.Combine(_testRoot, dirName));
        await _sp.GetRequiredService<IPathMarkService>().Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 100
        });
        await _sp.GetRequiredService<ResourceSyncService>().SyncResources(
            ResourceSource.PathMark, null, null, new PauseToken(), CancellationToken.None);
        return (await _sp.GetRequiredService<IResourceService>().GetAll()).Single().Id;
    }

    private async Task<int> AddMultiChoiceProperty(string name)
    {
        var property = await _sp.GetRequiredService<ICustomPropertyService>()
            .Add(new CustomPropertyAddOrPutDto { Name = name, Type = PropertyType.MultipleChoice });
        return property.Id;
    }

    /// <summary>Adds a catch-all profile whose Regex-enhancer config binds the "studio" capture group to a property.</summary>
    private Task AddProfileBindingCaptureToProperty(int propertyId)
        => _sp.GetRequiredService<IResourceProfileService>().Add(
            "enhance", "{}", null,
            new ResourceProfileEnhancerOptions
            {
                Enhancers =
                [
                    new EnhancerFullOptions
                    {
                        EnhancerId = (int)EnhancerId.Regex,
                        TargetOptions =
                        [
                            new EnhancerTargetFullOptions
                            {
                                Target = (int)RegexEnhancerTarget.CaptureGroups,
                                DynamicTarget = "studio",
                                PropertyPool = PropertyPool.Custom,
                                PropertyId = propertyId
                            }
                        ]
                    }
                ]
            },
            null, null, null, 100);

    private async Task BuildProfileIndex()
        => await _sp.GetRequiredService<IResourceProfileIndexService>().RebuildAsync(null, CancellationToken.None);

    private Task RunRegexEnhancer(int resourceId)
        => _sp.GetRequiredService<IEnhancerService>().EnhanceResourceWithOptions(
            resourceId,
            [new EnhancerFullOptions { EnhancerId = (int)EnhancerId.Regex, Expressions = [CaptureRegex] }],
            CancellationToken.None);

    private async Task<int> PropertyValueCount(int resourceId, int propertyId)
        => (await _sp.GetRequiredService<ICustomPropertyValueService>()
            .GetAllDbModels(x => x.ResourceId == resourceId && x.PropertyId == propertyId)).Count;

    [TestMethod]
    public async Task RegexCapture_WritesValueToBoundCustomProperty()
    {
        var resourceId = await SeedResource("[Toei] My Show");
        var propertyId = await AddMultiChoiceProperty("Studio");
        await AddProfileBindingCaptureToProperty(propertyId);
        await BuildProfileIndex();

        await RunRegexEnhancer(resourceId);

        // The "studio" capture ("Toei") must have been applied to the bound property.
        Assert.AreEqual(1, await PropertyValueCount(resourceId, propertyId));
    }

    [TestMethod]
    public async Task NoProfileBinding_WritesNothing()
    {
        var resourceId = await SeedResource("[Toei] My Show");
        var propertyId = await AddMultiChoiceProperty("Studio");
        // No profile -> the enhancer still runs, but there is no target binding to apply to.
        await BuildProfileIndex();

        await RunRegexEnhancer(resourceId);

        Assert.AreEqual(0, await PropertyValueCount(resourceId, propertyId));
    }

    [TestMethod]
    public async Task RegexNoMatch_WritesNothing()
    {
        // Filename has no "[...]" segment, so the regex captures nothing.
        var resourceId = await SeedResource("plain show");
        var propertyId = await AddMultiChoiceProperty("Studio");
        await AddProfileBindingCaptureToProperty(propertyId);
        await BuildProfileIndex();

        await RunRegexEnhancer(resourceId);

        Assert.AreEqual(0, await PropertyValueCount(resourceId, propertyId));
    }
}
