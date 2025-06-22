using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
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
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Presets.Abstractions;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Microsoft.EntityFrameworkCore;
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
    IServiceProvider serviceProvider)
    : ScopedService(serviceProvider), IMediaLibraryTemplateService
    where TDbContext : DbContext
{
    protected IEnhancerService EnhancerService => GetRequiredService<IEnhancerService>();
    protected IPresetsService BuiltinMediaLibraryTemplateService => GetRequiredService<IPresetsService>();
    protected IResourceService ResourceService => GetRequiredService<IResourceService>();

    protected async Task Populate(Abstractions.Models.Domain.MediaLibraryTemplate[] templates)
    {
        var propertyKeysMap = templates.SelectMany(x => x.Properties ?? []).GroupBy(d => d.Pool)
            .ToDictionary(d => d.Key, d => d.Select(x => x.Id).ToHashSet());

        var propertiesMap = (await propertyService.GetProperties((PropertyPool)propertyKeysMap.Keys.Sum(x => (int)x)))
            .ToMap();

        foreach (var template in templates)
        {
            if (template.Properties != null)
            {
                foreach (var p in template.Properties)
                {
                    p.Property = propertiesMap.GetValueOrDefault(p.Pool)?.GetValueOrDefault(p.Id);
                }
            }
        }

        var extensionGroupIds = templates.SelectMany(t =>
            (t.PlayableFileLocator?.ExtensionGroupIds ?? []).Concat(
                t.ResourceFilters?.SelectMany(x => x.ExtensionGroupIds ?? []) ?? [])).ToHashSet();
        if (extensionGroupIds.Any())
        {
            var extensionGroups = (await extensionGroupService.GetAll()).ToDictionary(d => d.Id, d => d);
            foreach (var t in templates)
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
                        foreach (var vs in p.ValueLocators.Select(vl => vl.LocateValues(rootFilename, rpi))
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
                            var rp = r.Properties.GetOrAdd((int)p.Pool, () => []).GetOrAdd(p.Id,
                                () => new Resource.Property(p.Property.Name, p.Property.Type,
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

    public async Task<MediaLibraryTemplate> Get(int id)
    {
        var dbData = await orm.GetByKey(id);
        var domainModel = dbData.ToDomainModel();
        await Populate([domainModel]);
        return domainModel;
    }

    public async Task<MediaLibraryTemplate[]> GetByKeys(int[] ids)
    {
        var dbData = await orm.GetByKeys(ids);
        var domainModels = dbData.Select(d => d.ToDomainModel()).ToArray();
        await Populate(domainModels);
        return domainModels;
    }

    public async Task<MediaLibraryTemplate[]> GetAll()
    {
        var templates = (await orm.GetAll()).OrderByDescending(d => d.Id);
        var domainModels = templates.Select(x => x.ToDomainModel()).ToArray();
        await Populate(domainModels);
        return domainModels;
    }

    public async Task<MediaLibraryTemplate> Add(MediaLibraryTemplateAddInputModel model)
    {
        var dbModel = (await orm.Add(new MediaLibraryTemplateDbModel
            {Name = model.Name, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now})).Data!;

        return dbModel.ToDomainModel();
    }

    public async Task Put(int id, MediaLibraryTemplate template)
    {
        template.Id = id;
        await orm.Update(template.ToDbModel() with { UpdatedAt = DateTime.Now });
    }

    public async Task Delete(int id)
    {
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
                                {ToExtensionGroupId = candidate.Id};
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
                                {ToExtensionGroupId = newExtensionGroups[newIdx++].Id};
                    }
                }
            }

            for (var index = 0; index < uniqueCustomProperties.Count; index++)
            {
                if (model.CustomPropertyConversionsMap?.ContainsKey(index) != true)
                {
                    var ucp = uniqueCustomProperties[index][0];
                    var candidate =
                        propertyMap.SelectMany(p => p.Value.Values)
                            .FirstOrDefault(x => x.Name == ucp.Name && x.Type == ucp.Type);
                    if (candidate != null)
                    {
                        model.CustomPropertyConversionsMap ??= [];
                        model.CustomPropertyConversionsMap[index] =
                            new MediaLibraryTemplateImportInputModel.TCustomPropertyConversion()
                                {ToPropertyId = candidate.Id, ToPropertyPool = candidate.Pool};
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
                    propertyMap.GetOrAdd(PropertyPool.Custom, () => []).TryAdd(np.Id, np.ToProperty());
                }

                var newIdx = 0;
                for (var i = 0; i < uniqueCustomProperties.Count; i++)
                {
                    if (model.CustomPropertyConversionsMap?.ContainsKey(i) != true)
                    {
                        model.CustomPropertyConversionsMap ??= [];
                        model.CustomPropertyConversionsMap[i] =
                            new MediaLibraryTemplateImportInputModel.TCustomPropertyConversion()
                                {ToPropertyId = newProperties[newIdx++].Id, ToPropertyPool = PropertyPool.Custom};
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
}