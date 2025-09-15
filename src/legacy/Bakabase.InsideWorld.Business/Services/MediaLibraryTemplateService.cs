using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tracing;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Sharable;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector.Infrastructures;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Business.Models.Domain;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Presets.Abstractions;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Tasks;
using Bootstrap.Extensions;
using Bootstrap.Models;
using DotNext.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace Bakabase.InsideWorld.Business.Services;

public class MediaLibraryTemplateService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, MediaLibraryTemplateDbModel, int> orm,
    IStandardValueService standardValueService,
    ICategoryService categoryService,
    ISpecialTextService specialTextService,
    IExtensionGroupService extensionGroupService,
    IPropertyService propertyService,
    ICustomPropertyService customPropertyService,
    IMediaLibraryService mediaLibraryService,
    IBOptions<EnhancerOptions> enhancerOptions,
    IBakabaseLocalizer localizer,
    IEnumerable<IEnhancer> enhancers,
    IPropertyLocalizer propertyLocalizer,
    IEnhancerDescriptors enhancerDescriptors,
    BakaTracingContext tracingContext,
    IServiceProvider serviceProvider)
    : ScopedService(serviceProvider), IMediaLibraryTemplateService
    where TDbContext : DbContext
{
    protected IEnhancerService EnhancerService => GetRequiredService<IEnhancerService>();
    protected IPresetsService BuiltinMediaLibraryTemplateService => GetRequiredService<IPresetsService>();
    protected IResourceService ResourceService => GetRequiredService<IResourceService>();
    protected IMediaLibraryV2Service MediaLibraryV2Service => GetRequiredService<IMediaLibraryV2Service>();

    protected ConcurrentDictionary<EnhancerId, IEnhancer> EnhancerMap =
        new(enhancers.ToDictionary(d => (EnhancerId)d.Id, d => d));

    protected async Task Populate(MediaLibraryTemplate[] templates, MediaLibraryTemplateAdditionalItem additionalItems)
    {
        var allRequiredTemplates = templates.ToList();

        if (additionalItems.HasFlag(MediaLibraryTemplateAdditionalItem.ChildTemplate) &&
            templates.Any(x => x.ChildTemplateId.HasValue))
        {
            var allDbModels = await orm.GetAll();
            var currentIds = templates.Select(t => t.Id).ToHashSet();
            var missingTemplates = allDbModels.Where(x => !currentIds.Contains(x.Id)).ToList();
            allRequiredTemplates.AddRange(missingTemplates.Select(mt => mt.ToDomainModel()));
        }

        var templateMap = allRequiredTemplates.ToDictionary(d => d.Id, d => d);
        foreach (var template in allRequiredTemplates)
        {
            template.Child = template.ChildTemplateId.HasValue
                ? templateMap.GetValueOrDefault(template.ChildTemplateId.Value)
                : null;
        }

        var propertyKeysMap = allRequiredTemplates.SelectMany(x => x.Properties ?? []).GroupBy(d => d.Pool)
            .ToDictionary(d => d.Key, d => d.Select(x => x.Id).ToHashSet());

        foreach (var template in allRequiredTemplates)
        {
            if (template.Enhancers != null)
            {
                foreach (var enhancer in template.Enhancers)
                {
                    if (enhancer.TargetOptions != null)
                    {
                        foreach (var to in enhancer.TargetOptions)
                        {
                            propertyKeysMap.GetOrAdd(to.PropertyPool, _ => []).Add(to.PropertyId);
                        }
                    }
                }
            }
        }

        var propertiesMap = (await propertyService.GetProperties((PropertyPool)propertyKeysMap.Keys.Sum(x => (int)x)))
            .ToMap();

        foreach (var template in allRequiredTemplates)
        {
            if (template.Properties != null)
            {
                foreach (var p in template.Properties)
                {
                    p.Property = propertiesMap.GetValueOrDefault(p.Pool)?.GetValueOrDefault(p.Id);
                }
            }

            if (template.Enhancers != null)
            {
                foreach (var enhancer in template.Enhancers)
                {
                    if (enhancer.TargetOptions != null)
                    {
                        foreach (var to in enhancer.TargetOptions)
                        {
                            to.Property = propertiesMap.GetValueOrDefault(to.PropertyPool)
                                ?.GetValueOrDefault(to.PropertyId);
                        }
                    }
                }
            }
        }

        var extensionGroupIds = allRequiredTemplates.SelectMany(t =>
            (t.PlayableFileLocator?.ExtensionGroupIds ?? []).Concat(
                t.ResourceFilters?.SelectMany(x => x.ExtensionGroupIds ?? []) ?? [])).ToHashSet();
        if (extensionGroupIds.Any())
        {
            var extensionGroups = (await extensionGroupService.GetAll()).ToDictionary(d => d.Id, d => d);
            foreach (var t in allRequiredTemplates)
            {
                if (t.PlayableFileLocator is { ExtensionGroupIds: not null })
                {
                    t.PlayableFileLocator.ExtensionGroups = t.PlayableFileLocator.ExtensionGroupIds
                        .Select(x => extensionGroups.GetValueOrDefault(x))
                        .OfType<ExtensionGroup>()
                        .ToList();
                }

                if (t.ResourceFilters != null)
                {
                    foreach (var rf in t.ResourceFilters)
                    {
                        if (rf.ExtensionGroupIds != null)
                        {
                            rf.ExtensionGroups = rf.ExtensionGroupIds
                                .Select(x => extensionGroups.GetValueOrDefault(x))
                                .OfType<ExtensionGroup>()
                                .ToList();
                        }
                    }
                }
            }
        }
    }

    public async Task GeneratePreview(int id)
    {
        var template = await Get(id);
        template.SamplePaths = template.SamplePaths?.Distinct().ToList();

        var rootPath = template.SamplePaths?.FirstOrDefault().StandardizePath();
        if (rootPath.IsNullOrEmpty())
        {
            throw new ArgumentNullException(nameof(template.SamplePaths));
        }

        var rootFilename = Path.GetFileName(rootPath);

        var subPaths = template.SamplePaths!.Skip(1).Select(x => x.StandardizePath()!).ToArray();
        if (!subPaths.Any())
        {
            throw new ArgumentNullException(nameof(subPaths));
        }

        #region Discover resources

        if (template.ResourceFilters == null)
        {
            throw new ArgumentNullException(nameof(template.ResourceFilters));
        }

        var resourcePathInfoMap = await template.ResourceFilters.Filter(rootPath, subPaths) ?? [];
        var resourcesMap = resourcePathInfoMap.ToDictionary(d => d.Key, d => new Resource
        {
            Path = d.Key
        });

        #endregion

        #region Get property values

        if (template.Properties?.Any() == true)
        {
            foreach (var rpi in resourcePathInfoMap.Values)
            {
                foreach (var p in template.Properties)
                {
                    if (p.Property == null)
                    {
                        continue;
                    }

                    var pvs = new List<string>();
                    if (p.ValueLocators != null)
                    {
                        foreach (var vs in p.ValueLocators.Select(vl => vl.ExtractValues(rootFilename, rpi))
                                     .OfType<string[]>())
                        {
                            pvs.AddRange(vs);
                        }
                    }

                    pvs = pvs.Distinct().ToList();
                    if (!pvs.Any())
                    {
                        var bv = await standardValueService.Convert(pvs, StandardValueType.ListString,
                            p.Property.Type.GetBizValueType());
                        if (bv != null)
                        {
                            var r = resourcesMap[rpi.Path];
                            r.Properties ??= [];
                            var rp = r.Properties.GetOrAdd((int)p.Pool, _ => []).GetOrAdd(p.Id,
                                _ => new Resource.Property(p.Property.Name, p.Property.Type,
                                    p.Property.Type.GetDbValueType(), p.Property.Type.GetBizValueType(), [], true));
                            rp.Values ??= [];
                            rp.Values.Add(new Resource.Property.PropertyValue((int)PropertyValueScope.Synchronization,
                                null, bv, bv));
                        }
                    }
                }
            }
        }

        #endregion

        #region Playable files

        var pathExtMap = subPaths.ToDictionary(d => d, Path.GetExtension);
        if (template.PlayableFileLocator != null)
        {
            var extensions = (template.PlayableFileLocator.ExtensionGroups?.SelectMany(x => x.Extensions) ?? [])
                .Concat(template.PlayableFileLocator.Extensions ?? []).ToHashSet();
            foreach (var rpi in resourcePathInfoMap.Values)
            {
                var playableFilePaths = rpi.InnerPaths.Where(ip => extensions.Contains(pathExtMap[ip]!)).ToList();
                if (playableFilePaths.Any())
                {
                    var r = resourcesMap[rpi.Path];
                    r.Cache ??= new ResourceCache
                    {
                        PlayableFilePaths = playableFilePaths
                    };
                }
            }
        }

        #endregion

        #region Enhancements

        if (template.Enhancers != null)
        {
            var optionsMap = template.Enhancers.Where(x => x.TargetOptions != null)
                .ToDictionary(d => d.EnhancerId, d => d.ToEnhancerFullOptions());
            foreach (var r in resourcesMap.Values)
            {
                await EnhancerService.Enhance(r, optionsMap);
            }
        }

        #endregion

        #region Display Name

        if (template.DisplayNameTemplate.IsNotEmpty())
        {
            var wrappers = (await specialTextService.GetAll(x => x.Type == SpecialTextType.Wrapper))
                .Select(x => (Left: x.Value1, Right: x.Value2!)).ToArray();
            foreach (var r in resourcesMap.Values)
            {
                r.DisplayName = ResourceService.BuildDisplayNameForResource(r, template.DisplayNameTemplate, wrappers);
            }
        }

        #endregion
    }

    public async Task<MediaLibraryTemplate> Get(int id,
        MediaLibraryTemplateAdditionalItem additionalItems = MediaLibraryTemplateAdditionalItem.None)
    {
        var dbData = await orm.GetByKey(id);
        var domainModel = dbData.ToDomainModel();
        await Populate([domainModel], additionalItems);
        return domainModel;
    }

    public async Task<MediaLibraryTemplate[]> GetByKeys(int[] ids,
        MediaLibraryTemplateAdditionalItem additionalItems = MediaLibraryTemplateAdditionalItem.None)
    {
        var dbData = await orm.GetByKeys(ids);
        var domainModels = dbData.Select(d => d.ToDomainModel()).ToArray();
        await Populate(domainModels, additionalItems);
        return domainModels;
    }

    public async Task<MediaLibraryTemplate[]> GetAll(
        MediaLibraryTemplateAdditionalItem additionalItems = MediaLibraryTemplateAdditionalItem.None)
    {
        var dbModels = (await orm.GetAll()).OrderByDescending(d => d.Id);
        var templates = dbModels.Select(x => x.ToDomainModel()).ToArray();
        await Populate(templates, additionalItems);
        return templates;
    }

    public async Task<MediaLibraryTemplate> Add(MediaLibraryTemplateAddInputModel model)
    {
        var dbModel = (await orm.Add(new MediaLibraryTemplateDbModel
            { Name = model.Name, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now })).Data!;

        return dbModel.ToDomainModel();
    }

    public async Task Put(int id, MediaLibraryTemplate template)
    {
        template.Id = id;

        // check enhancer dependencies circle
        if (template.Enhancers != null)
        {
            var visited = new HashSet<int>();
            var simpleDepsCopy = template.Enhancers.ToDictionary(a => a.EnhancerId, a => a.Requirements?.ToHashSet());
            while (simpleDepsCopy.Any())
            {
                var noDepEnhancers = simpleDepsCopy.Where(x => x.Value?.Any() != true).Select(x => x.Key).ToList();
                if (noDepEnhancers.Any())
                {
                    visited.AddAll(noDepEnhancers);
                    foreach (var n in noDepEnhancers)
                    {
                        simpleDepsCopy.Remove(n);
                        foreach (var s in simpleDepsCopy)
                        {
                            s.Value?.Remove(n);
                        }
                    }
                }
                else
                {
                    throw new Exception(localizer.Enhancer_CircularDependencyDetected(simpleDepsCopy.Keys
                        .Select(k => ((EnhancerId)k).ToString()).ToArray()));
                }
            }
        }

        await orm.Update(template.ToDbModel() with { UpdatedAt = DateTime.Now });
    }

    public async Task Delete(int id)
    {
        var mediaLibraries = await MediaLibraryV2Service.GetAll(x => x.TemplateId == id);
        if (mediaLibraries.Any())
        {
            throw new Exception(localizer
                .MediaLibraryTemplate_NotDeletableWhenUsingByMediaLibraries(mediaLibraries.Select(x => x.Name)));
        }

        await orm.RemoveByKey(id);
    }

    public async Task<string> GenerateShareCode(int id)
    {
        var template = await Get(id);
        return template.ToSharable().ToShareCode();
    }

    public async Task<int> Import(MediaLibraryTemplateImportInputModel model)
    {
        var shared = SharableMediaLibraryTemplate.FromShareCode(model.ShareCode);
        if (model.Name.IsNotEmpty())
        {
            shared.Name = model.Name;
        }

        var flat = shared.Flat();
        var uniqueCustomProperties = flat.ExtractUniqueCustomProperties();
        var uniqueExtensionGroups = flat.ExtractUniqueExtensionGroups();

        var propertyMap = (await propertyService.GetProperties(PropertyPool.Reserved | PropertyPool.Custom)).ToMap();
        var extensionGroupMap = (await extensionGroupService.GetAll()).ToDictionary(d => d.Id, d => d);

        if (model.AutomaticallyCreateMissingData)
        {
            for (var index = 0; index < uniqueExtensionGroups.Count; index++)
            {
                if (model.ExtensionGroupConversionsMap?.ContainsKey(index) != true)
                {
                    var ueg = uniqueExtensionGroups[index];
                    var candidate =
                        extensionGroupMap.Values.FirstOrDefault(x => ExtensionGroup.BizComparer.Equals(x, ueg[0]));
                    if (candidate != null)
                    {
                        model.ExtensionGroupConversionsMap ??= [];
                        model.ExtensionGroupConversionsMap[index] =
                            new MediaLibraryTemplateImportInputModel.TExtensionGroupConversion
                                { ToExtensionGroupId = candidate.Id };
                    }
                }
            }

            var missingExtensionGroups = uniqueExtensionGroups
                .Where((g, i) => model.ExtensionGroupConversionsMap?.ContainsKey(i) != true).ToList();
            if (missingExtensionGroups?.Any() == true)
            {
                var newExtensionGroups = await extensionGroupService.AddRange(missingExtensionGroups
                    .Select(g => new ExtensionGroupAddInputModel(g[0].Name, g[0].Extensions)).ToArray());
                foreach (var neg in newExtensionGroups)
                {
                    extensionGroupMap.TryAdd(neg.Id, neg);
                }

                var newIdx = 0;
                for (var i = 0; i < uniqueExtensionGroups.Count; i++)
                {
                    if (model.ExtensionGroupConversionsMap?.ContainsKey(i) != true)
                    {
                        model.ExtensionGroupConversionsMap ??= [];
                        model.ExtensionGroupConversionsMap[i] =
                            new MediaLibraryTemplateImportInputModel.TExtensionGroupConversion
                                { ToExtensionGroupId = newExtensionGroups[newIdx++].Id };
                    }
                }
            }

            for (var index = 0; index < uniqueCustomProperties.Count; index++)
            {
                if (model.CustomPropertyConversionsMap?.ContainsKey(index) != true)
                {
                    var ucp = uniqueCustomProperties[index][0];
                    var candidate =
                        propertyMap.Where(pm => pm.Key == PropertyPool.Custom)
                            .SelectMany(p => p.Value.Values)
                            .FirstOrDefault(x => x.Name == ucp.Name && x.Type == ucp.Type);
                    if (candidate != null)
                    {
                        model.CustomPropertyConversionsMap ??= [];
                        model.CustomPropertyConversionsMap[index] =
                            new MediaLibraryTemplateImportInputModel.TCustomPropertyConversion()
                                { ToPropertyId = candidate.Id, ToPropertyPool = candidate.Pool };
                    }
                }
            }

            var missingCustomProperties = uniqueCustomProperties
                .Where((p, i) => model.CustomPropertyConversionsMap?.ContainsKey(i) != true).ToList();
            if (missingCustomProperties?.Any() == true)
            {
                var newProperties = await customPropertyService.AddRange(missingCustomProperties.Select(p =>
                    new CustomPropertyAddOrPutDto
                    {
                        Name = p[0].Name,
                        Type = p[0].Type
                    }).ToArray());
                foreach (var np in newProperties)
                {
                    propertyMap.GetOrAdd(PropertyPool.Custom, _ => []).TryAdd(np.Id, np.ToProperty());
                }

                var newIdx = 0;
                for (var i = 0; i < uniqueCustomProperties.Count; i++)
                {
                    if (model.CustomPropertyConversionsMap?.ContainsKey(i) != true)
                    {
                        model.CustomPropertyConversionsMap ??= [];
                        model.CustomPropertyConversionsMap[i] =
                            new MediaLibraryTemplateImportInputModel.TCustomPropertyConversion()
                                { ToPropertyId = newProperties[newIdx++].Id, ToPropertyPool = PropertyPool.Custom };
                    }
                }
            }
        }

        if (uniqueCustomProperties.Any())
        {
            for (var index = 0; index < uniqueCustomProperties.Count; index++)
            {
                var ucps = uniqueCustomProperties[index];
                var sampleProperty = ucps[0];
                var conversion = model.CustomPropertyConversionsMap?.GetValueOrDefault(index);
                if (conversion == null)
                {
                    throw new Exception($"Conversion is not set for unknown property {sampleProperty}");
                }

                foreach (var ucp in ucps)
                {
                    ucp.Id = conversion.ToPropertyId;
                }
            }
        }

        if (uniqueExtensionGroups.Any())
        {
            for (var index = 0; index < uniqueExtensionGroups.Count; index++)
            {
                var uegs = uniqueExtensionGroups[index];
                var sampleGroup = uegs[0];
                var conversion = model.ExtensionGroupConversionsMap?.GetValueOrDefault(index);
                if (conversion == null)
                {
                    throw new Exception($"Conversion is not set for unknown extension group {sampleGroup}");
                }

                foreach (var ueg in uegs)
                {
                    ueg.Id = conversion.ToExtensionGroupId;
                }
            }
        }

        var templates = flat
            .Select(f => f.ToDomainModel(extensionGroupMap, propertyMap) with
            {
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            })
            .ToArray();
        var dbModels = templates.Select(t => t.ToDbModel()).ToList();
        var addedDbModels = (await orm.AddRange(dbModels)).Data!;
        if (addedDbModels.Count > 1)
        {
            var parent = addedDbModels[0]!;
            for (var i = 1; i < addedDbModels.Count; i++)
            {
                if (parent != null)
                {
                    addedDbModels[i - 1].ChildTemplateId = parent.Id;
                }
            }

            await orm.UpdateRange(addedDbModels);
        }

        return addedDbModels[0].Id;
    }

    public async Task<MediaLibraryTemplateImportConfigurationViewModel> GetImportConfiguration(string shareCode)
    {
        var shared = SharableMediaLibraryTemplate.FromShareCode(shareCode);
        var flat = shared.Flat();

        var uniqueProperties = flat.ExtractUniqueCustomProperties();
        var uniqueExtensionGroups = flat.ExtractUniqueExtensionGroups();

        return new MediaLibraryTemplateImportConfigurationViewModel
        {
            UniqueCustomProperties = uniqueProperties.Select(p => p.First()).ToList(),
            UniqueExtensionGroups = uniqueExtensionGroups.Select(g => g.First()).ToList()
        };
    }

    public async Task<byte[]> AppendShareCodeToPng(int id, byte[] png)
    {
        var text = await GenerateShareCode(id);

        using var image = Image.Load<Rgb24>(png);

        image.Metadata.ExifProfile ??= new();
        image.Metadata.ExifProfile.SetValue(ExifTag.UserComment, text);

        using var ms = new MemoryStream();
        await image.SaveAsPngAsync(ms);
        return ms.ToArray();
    }

    public async Task AddByMediaLibraryV1(int v1Id, int pcIdx, string templateName)
    {
        var ml = (await mediaLibraryService.Get(v1Id, MediaLibraryAdditionalItem.None))!;
        var category = await categoryService.Get(ml.CategoryId,
            CategoryAdditionalItem.CustomProperties | CategoryAdditionalItem.EnhancerOptions);
        var playableFilesSelector =
            (await categoryService.GetFirstComponent<IPlayableFileSelector>(category.Id,
                ComponentType.PlayableFileSelector)).Data;
        var template = await Add(new MediaLibraryTemplateAddInputModel(templateName));
        template.InitFromMediaLibraryV1(ml, pcIdx, category, playableFilesSelector, enhancerOptions);
        await Put(template.Id, template);
    }

    public async Task Duplicate(int id)
    {
        var original = await Get(id);
        var @new = await Add(new MediaLibraryTemplateAddInputModel($"{original.Name}-{DateTime.Now:yyyyMMddHHmmss}"));
        await Put(@new.Id, original with
        {
            Name = @new.Name,
            CreatedAt = @new.CreatedAt,
        });
    }

    public async Task Validate(int id, MediaLibraryTemplateValidationInputModel model, CancellationToken ct)
    {
        var template = await Get(id, MediaLibraryTemplateAdditionalItem.ChildTemplate);
        tracingContext.AddTrace(LogLevel.Information, localizer.Init(),
            (localizer.MediaLibraryTemplate_Id(), template.Id),
            (localizer.MediaLibraryTemplate_Name(), template.Name));

        tracingContext.AddTrace(LogLevel.Information, localizer.ResourceDiscovery(), localizer.Searching());
        var limit = Math.Max(1, model.LimitResourcesCount ?? 3);
        var rootPath = model.RootPath.StandardizePath()!;
        var tmpResources = await template.DiscoverResources(rootPath, null, null, PauseToken.None, ct, 1);
        tracingContext.AddTrace(LogLevel.Information, localizer.ResourceDiscovery(),
            (localizer.Found(), tmpResources.Count));
        var resources = tmpResources.SelectMany(r => r.Flatten(0, null)).Where(x =>
            model.ResourceKeyword.IsNullOrEmpty() ||
            x.FileName.Contains(model.ResourceKeyword, StringComparison.OrdinalIgnoreCase)).Take(limit).ToList();
        tracingContext.AddTrace(LogLevel.Information, localizer.PickResourcesToValidate(),
        [
            (localizer.Count(), resources.Count),
            (localizer.Keyword(), model.ResourceKeyword ?? localizer.NotSet()),
            ..resources.Select(r => (localizer.Resource(), r.Path))
        ]);
        var playableFilesExtensions =
            (template.PlayableFileLocator?.ExtensionGroups?.SelectMany(x => x.Extensions ?? []) ?? [])
            .Concat(template.PlayableFileLocator?.Extensions ?? [])
            .ToHashSet();
        foreach (var resource in resources)
        {
            tracingContext.AddTrace(LogLevel.Information,
                localizer.BuildingData(),
                (localizer.Resource(), resource.Path));
            {
                var properties = new List<(PropertyPool Pool, PropertyType Type, string Name, object? BizValue)>();
                if (resource.Properties != null)
                {
                    foreach (var (pool, pvs) in resource.Properties)
                    {
                        foreach (var (pId, pv) in pvs)
                        {
                            var syncScopeValue = pv.Values
                                ?.FirstOrDefault(v => v.Scope == (int)PropertyValueScope.Synchronization)?.BizValue;
                            if (syncScopeValue != null)
                            {
                                properties.Add(
                                    ((PropertyPool)pool, pv.Type, pv.Name ?? localizer.Unknown(),
                                        syncScopeValue.SerializeBizValueAsStandardValue(pv.Type)));
                            }
                        }
                    }
                }

                if (properties.Any())
                {
                    var context = properties.Select(p => (
                        $"[{propertyLocalizer.PropertyPoolName(p.Pool)}:{propertyLocalizer.PropertyTypeName(p.Type)}]{p.Name}",
                        (object?)p.BizValue.SerializeBizValueAsStandardValue(p.Type))).ToArray();
                    tracingContext.AddTrace(LogLevel.Information,
                        localizer.PropertyValuesGeneratedOnSynchronization(), context);
                }
                else
                {
                    tracingContext.AddTrace(LogLevel.Information,
                        localizer.NoPropertyValuesGeneratedOnSynchronization());
                }
            }

            {
                if (playableFilesExtensions.Any())
                {
                    var fileCount = template.PlayableFileLocator?.MaxFileCount ?? int.MaxValue;
                    var files = resource.IsFile
                        ? [resource.Path]
                        : Directory.GetFiles(resource.Path, "*", SearchOption.AllDirectories);
                    var playableFiles = files.Where(f => playableFilesExtensions.Contains(Path.GetExtension(f)))
                        .Take(fileCount)
                        .ToArray();
                    var samples = playableFiles.Take(3)
                        .Select(f =>
                        {
                            var stdPath = f.StandardizePath()!;
                            return resource.IsFile ? Path.GetFileName(stdPath) : stdPath.Replace(resource.Path, string.Empty);
                        }).ToList();
                    if (samples.Any())
                    {
                        var samplesText =
                            $"{string.Join(", ", samples)}{(samples.Count < playableFiles.Length ? $" and {playableFiles.Length - samples.Count} more" : "")}"
                                .Trim();
                        tracingContext.AddTrace(LogLevel.Information, localizer.FoundPlayableFiles(),
                            (localizer.PlayableFiles(), samplesText));
                    }
                    else
                    {
                        tracingContext.AddTrace(LogLevel.Warning, localizer.NoPlayableFiles(),
                            localizer.PlayableFiles());
                    }

                }
                else
                {
                    tracingContext.AddTrace(LogLevel.Warning, localizer.NoPlayableFiles(),
                        localizer.NoExtensionsConfigured());
                }
            }

            {
                if (template.Enhancers != null && template.Enhancers.Any())
                {
                    // Build requirement map to resolve execution order
                    var requirementMap = template.Enhancers.ToDictionary(
                        e => (EnhancerId)e.EnhancerId,
                        e => (e.Requirements?.Select(x => (EnhancerId)x).ToHashSet()) ?? new HashSet<EnhancerId>()
                    );

                    var optionsMapAll = template.Enhancers
                        .ToDictionary(d => d.EnhancerId, d => d.ToEnhancerFullOptions());

                    var remaining = requirementMap.Keys.ToHashSet();

                    while (remaining.Any())
                    {
                        var ready = remaining.Where(eid => requirementMap[eid].Count == 0).ToArray();
                        if (!ready.Any())
                        {
                            tracingContext.AddTrace(LogLevel.Error,
                                localizer.Enhancer_CircularDependencyDetected(remaining.Select(k => k.ToString())
                                    .ToArray()));
                            return;
                        }

                        foreach (var enhancerId in ready)
                        {
                            tracingContext.AddTrace(LogLevel.Information, localizer.StartEnhancing(),
                                (localizer.Enhancer(), enhancerId.ToString()));

                            // Apply this enhancer only, in order
                            var singleOptions = new Dictionary<int, EnhancerFullOptions>
                            {
                                [(int)enhancerId] = optionsMapAll[(int)enhancerId]
                            };

                            Exception? err = null;
                            try
                            {
                                await EnhancerService.Enhance(resource, singleOptions);
                            }
                            catch (Exception ex)
                            {
                                err = ex;
                            }

                            if (err == null)
                            {
                                var properties =
                                    new List<(PropertyPool Pool, PropertyType Type, string Name, object? BizValue)>();
                                if (resource.Properties != null)
                                {
                                    var ed = enhancerDescriptors[(int)enhancerId];
                                    foreach (var (pool, pvs) in resource.Properties)
                                    {
                                        foreach (var (pId, pv) in pvs)
                                        {
                                            var enhancedValue = pv.Values
                                                ?.FirstOrDefault(v => v.Scope == ed.PropertyValueScope)?.BizValue;
                                            if (enhancedValue != null)
                                            {
                                                properties.Add(
                                                    ((PropertyPool)pool, pv.Type, pv.Name ?? localizer.Unknown(),
                                                        enhancedValue));
                                            }
                                        }
                                    }
                                }

                                if (properties.Any())
                                {
                                    var context = properties.Select(p => (
                                        $"[{propertyLocalizer.PropertyPoolName(p.Pool)}:{propertyLocalizer.PropertyTypeName(p.Type)}]{p.Name}",
                                        (object?)p.BizValue.SerializeBizValueAsStandardValue(p.Type))).ToArray();
                                    tracingContext.AddTrace(LogLevel.Information, localizer.EnhancementCompleted(),
                                        context);
                                }
                                else
                                {
                                    tracingContext.AddTrace(LogLevel.Warning,
                                        localizer.EnhancementCompleted(),
                                        localizer.NoPropertyValuesGeneratedByEnhancer());
                                }
                            }
                            else
                            {
                                tracingContext.AddTrace(LogLevel.Error, localizer.Failed(), err.Message);
                            }

                            // Mark as done and remove from others' requirements
                            remaining.Remove(enhancerId);
                            foreach (var kv in requirementMap.Values)
                            {
                                kv.Remove(enhancerId);
                            }
                        }
                    }
                }
                else
                {
                    tracingContext.AddTrace(LogLevel.Information, localizer.NoEnhancerConfigured());
                }
            }
            {
                if (template.DisplayNameTemplate.IsNotEmpty())
                {
                    var wrappers = (await specialTextService.GetAll(x => x.Type == SpecialTextType.Wrapper))
                        .Select(x => (Left: x.Value1, Right: x.Value2!)).ToArray();
                    foreach (var r in resources)
                    {
                        r.DisplayName =
                            ResourceService.BuildDisplayNameForResource(r, template.DisplayNameTemplate, wrappers);
                        tracingContext.AddTrace(LogLevel.Information, localizer.ResourceDisplayName(),
                            (localizer.DisplayName(), r.DisplayName));
                    }
                }
            }
        }

        tracingContext.AddTrace(LogLevel.Information, localizer.Complete());
    }
}