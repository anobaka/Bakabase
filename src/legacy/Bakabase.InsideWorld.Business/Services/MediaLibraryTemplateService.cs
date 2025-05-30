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
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector.Infrastructures;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace Bakabase.InsideWorld.Business.Services;


public class MediaLibraryTemplateService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, MediaLibraryTemplateDbModel, int> orm,
    IStandardValueService standardValueService,
    IEnhancerService enhancerService,
    ICategoryService categoryService,
    ISpecialTextService specialTextService,
    IExtensionGroupService extensionGroupService,
    IPropertyService propertyService,
    ICustomPropertyService customPropertyService,
    IMediaLibraryService mediaLibraryService
)
    : IMediaLibraryTemplateService
    where TDbContext : DbContext
{
    internal record ResourcePathInfo(
        string Path,
        string RelativePath,
        string[] RelativePathSegments,
        string[] InsidePaths);

    protected async Task Populate(Abstractions.Models.Domain.MediaLibraryTemplate[] templates)
    {
        var propertyKeysMap = templates.SelectMany(x => x.Properties ?? []).GroupBy(d => d.Pool)
            .ToDictionary(d => d.Key, d => d.Select(x => x.Id).ToHashSet());

        var propertiesMap = (await propertyService.GetProperties((PropertyPool) propertyKeysMap.Keys.Sum(x => (int) x)))
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
                if (t.PlayableFileLocator is {ExtensionGroupIds: not null})
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

        var subPaths = template.SamplePaths!.Skip(1).Select(x => x.StandardizePath()!).ToList();
        if (!subPaths.Any())
        {
            throw new ArgumentNullException(nameof(subPaths));
        }

        #region Discover resources

        if (template.ResourceFilters == null)
        {
            throw new ArgumentNullException(nameof(template.ResourceFilters));
        }

        var subRelativePaths =
            subPaths.Select(x => x.Replace(rootPath, null).Trim(InternalOptions.DirSeparator)).ToList();
        var subPathRelativeSegments = subRelativePaths.Select(x => x.Split(InternalOptions.DirSeparator)).ToList();
        var resourcePathInfoMap = new Dictionary<string, ResourcePathInfo>();
        foreach (var rf in template.ResourceFilters)
        {
            switch (rf.Positioner)
            {
                case PathPositioner.Layer:
                {
                    if (rf.Layer.HasValue)
                    {
                        foreach (var segments in subPathRelativeSegments)
                        {
                            var len = rf.Layer.Value;
                            if (len >= 0 && len < segments.Length)
                            {
                                var relativeSegments = segments.Take(len).ToArray();
                                var relativePath = string.Join(InternalOptions.DirSeparator, relativeSegments);
                                var path = string.Join(InternalOptions.DirSeparator, rootPath, relativePath);
                                var insidePaths = subPaths.Where(x => x != path && x.StartsWith(path)).ToArray();
                                resourcePathInfoMap[path] =
                                    new ResourcePathInfo(path, relativePath, relativeSegments, insidePaths);
                            }
                        }
                    }

                    break;
                }
                case PathPositioner.Regex:
                {
                    if (rf.Regex.IsNotEmpty())
                    {
                        for (var i = 0; i < subRelativePaths.Count; i++)
                        {
                            var relativePath = subRelativePaths[i];
                            var match = Regex.Match(relativePath, rf.Regex);
                            if (match.Success)
                            {
                                var len = match.Value.Split(InternalOptions.DirSeparator,
                                    StringSplitOptions.RemoveEmptyEntries).Length;
                                var segments = subPathRelativeSegments[i];
                                var relativeSegments = segments.Take(len).ToArray();
                                var path = string.Join(InternalOptions.DirSeparator, rootPath, relativePath);
                                var insidePaths = subPaths.Where(x => x != path && x.StartsWith(path)).ToArray();
                                resourcePathInfoMap[path] =
                                    new ResourcePathInfo(path, relativePath, relativeSegments, insidePaths);
                            }
                        }
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

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
                        foreach (var vl in p.ValueLocators)
                        {
                            switch (vl.Positioner)
                            {
                                case PathPositioner.Layer:
                                {
                                    if (vl.Layer.HasValue)
                                    {
                                        switch (vl.Layer.Value)
                                        {
                                            case 0:
                                            {
                                                pvs.Add(rootFilename);
                                                break;
                                            }
                                            default:
                                            {
                                                if (vl.Layer > 0)
                                                {
                                                    if (vl.Layer <= rpi.RelativePathSegments.Length)
                                                    {
                                                        pvs.Add(rpi.RelativePathSegments[vl.Layer.Value - 1]);
                                                    }
                                                }
                                                else
                                                {
                                                    var len = Math.Abs(vl.Layer.Value);
                                                    if (len <= rpi.RelativePathSegments.Length)
                                                    {
                                                        pvs.Add(rpi.RelativePathSegments[^len]);
                                                    }
                                                }

                                                break;
                                            }
                                        }
                                    }

                                    break;
                                }
                                case PathPositioner.Regex:
                                {
                                    if (vl.Regex.IsNotEmpty())
                                    {
                                        var matches = Regex.Matches(rpi.RelativePath, vl.Regex);
                                        if (matches.Any())
                                        {
                                            var groups = matches
                                                .SelectMany(a => a.Groups.Values.Skip(1).Select(b => b.Value))
                                                .Where(x => x.IsNotEmpty())
                                                .ToHashSet();
                                            if (groups.Any())
                                            {
                                                pvs.AddRange(groups.ToArray());
                                            }
                                        }
                                    }

                                    break;
                                }
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
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
                            var rp = r.Properties.GetOrAdd((int) p.Pool, () => []).GetOrAdd(p.Id,
                                () => new Resource.Property(p.Property.Name, p.Property.Type,
                                    p.Property.Type.GetDbValueType(), p.Property.Type.GetBizValueType(), [], true));
                            rp.Values ??= [];
                            rp.Values.Add(new Resource.Property.PropertyValue((int) PropertyValueScope.Synchronization,
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
                var playableFilePaths = rpi.InsidePaths.Where(ip => extensions.Contains(pathExtMap[ip]!)).ToList();
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
                await enhancerService.Enhance(r, optionsMap);
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
                r.DisplayName = categoryService.BuildDisplayNameForResource(r, template.DisplayNameTemplate, wrappers);
            }
        }

        #endregion
    }

    public async Task<Abstractions.Models.Domain.MediaLibraryTemplate> Get(int id)
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

    public async Task<Abstractions.Models.Domain.MediaLibraryTemplate[]> GetAll()
    {
        var templates = await orm.GetAll();
        var domainModels = templates.Select(x => x.ToDomainModel()).ToArray();
        await Populate(domainModels);
        return domainModels;
    }

    public async Task<MediaLibraryTemplate> Add(MediaLibraryTemplateAddInputModel model)
    {
        var dbModel = (await orm.Add(new MediaLibraryTemplateDbModel {Name = model.Name,})).Data!;
        return dbModel.ToDomainModel();
    }

    public async Task Put(int id, Abstractions.Models.Domain.MediaLibraryTemplate template)
    {
        template.Id = id;
        await orm.Update(template.ToDbModel());
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

    public async Task Import(MediaLibraryTemplateImportInputModel model)
    {
        var err = await Import(model, false);
        if (err != null)
        {
            throw new Exception("Import failed because validation was unsuccessful.");
        }
    }

    public async Task<MediaLibraryTemplateValidationViewModel?> Validate(string shareCode)
    {
        var shared = SharableMediaLibraryTemplate.FromShareCode(shareCode);
        var flat = shared.Flat();

        var refProperties = flat.ExtractProperties();
        var unhandledProperties = refProperties.Where(r => r.Pool == PropertyPool.Custom).ToList();
        if (refProperties.Any())
        {
            var localPropertiesMap = (await propertyService.GetProperties(PropertyPool.All)).ToMap();
            foreach (var p in refProperties)
            {
                if (p.Pool != PropertyPool.Custom)
                {
                    var rp = localPropertiesMap.GetValueOrDefault(p.Pool)?.GetValueOrDefault(p.Id);
                    if (rp == null)
                    {
                        throw new Exception(
                            $"{p.Pool} property with id:{p.Id} is not found. The template is not compatible with current version of application.");
                    }
                }
            }
        }

        var unhandledExtensionGroups = flat.ExtractExtensionGroups();

        if (unhandledExtensionGroups.Any() || unhandledProperties.Any())
        {
            return new MediaLibraryTemplateValidationViewModel
            {
                UnhandledProperties = unhandledProperties,
                UnhandledExtensionGroups = unhandledExtensionGroups
            };
        }

        return null;
    }

    protected async Task<MediaLibraryTemplateValidationViewModel?> Import(MediaLibraryTemplateImportInputModel model,
        bool validateOnly)
    {
        var shared = SharableMediaLibraryTemplate.FromShareCode(model.ShareCode);
        var flat = shared.Flat();


        var customPropertyIdConversion =
            model.CustomPropertyConversionsMap?.ToDictionary(d => d.Key,
                d => (d.Value.ToPropertyPool, d.Value.ToPropertyId));
        var propertyMap = (await propertyService.GetProperties(PropertyPool.All)).ToMap();
        var extensionGroupMap = (await extensionGroupService.GetAll()).ToDictionary(d => d.Id, d => d);
        var extensionGroupConversionsMap = model.ExtensionGroupConversionsMap?.ToDictionary(
            d => d.Key,
            d => extensionGroupMap[d.Value.ToExtensionGroupId]);

        var templates = flat
            .Select(f => f.ToDomainModel(customPropertyIdConversion, extensionGroupConversionsMap, propertyMap))
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

        return null;
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

    public async Task ImportFromMediaLibraryV1(int v1Id, int pcIdx, string templateName)
    {
        var ml = (await mediaLibraryService.Get(v1Id, MediaLibraryAdditionalItem.None))!;
        var category = await categoryService.Get(ml.CategoryId,
            CategoryAdditionalItem.CustomProperties | CategoryAdditionalItem.EnhancerOptions);
        var playableFilesSelector =
            (await categoryService.GetFirstComponent<IPlayableFileSelector>(category.Id,
                ComponentType.PlayableFileSelector)).Data;
        var template = await Add(new MediaLibraryTemplateAddInputModel(templateName));
        template.InitFromMediaLibraryV1(ml, pcIdx, category, playableFilesSelector);
        await Put(template.Id, template);
    }
}