using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Components.PathLocator;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Extensions;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Db;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain.Shared;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Input;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.View;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Services;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Microsoft.EntityFrameworkCore;
using Namotion.Reflection;
using NPOI.SS.Formula.Functions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png.Chunks;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace Bakabase.Modules.MediaLibraryTemplate.Services;


public class MediaLibraryTemplateService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, MediaLibraryTemplateDbModel, int> orm,
    IStandardValueService standardValueService,
    IEnhancerService enhancerService,
    ICategoryService categoryService,
    ISpecialTextService specialTextService,
    IExtensionGroupService extensionGroupService,
    IPropertyService propertyService,
    ICustomPropertyService customPropertyService
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
                        .ToArray();
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
                                .ToArray();
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
            var optionsMap = template.Enhancers.Where(x => x.Options != null)
                .ToDictionary(d => d.EnhancerId, d => d.Options!);
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

    public async Task<Abstractions.Models.Domain.MediaLibraryTemplate[]> GetAll()
    {
        var templates = await orm.GetAll();
        var domainModels = templates.Select(x => x.ToDomainModel()).ToArray();
        await Populate(domainModels);
        return domainModels;
    }

    public async Task Add(MediaLibraryTemplateAddInputModel model)
    {
        await orm.Add(new MediaLibraryTemplateDbModel {Name = model.Name,});
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
        return template.ToShared().ToSharedText();
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
        return await Import(new MediaLibraryTemplateImportInputModel {ShareCode = shareCode}, true);
    }

    protected async Task<MediaLibraryTemplateValidationViewModel?> Import(MediaLibraryTemplateImportInputModel model, bool validateOnly)
    {
        var shared = SharedMediaLibraryTemplate.FromSharedText(model.ShareCode);
        var flat = shared.Flat();

        #region Validation

        var refProperties = flat.ExtractProperties();
        var missingProperties = new List<Bakabase.Abstractions.Models.Domain.Property>();
        var customPropertyIdConversion = new Dictionary<int, (PropertyPool Pool, int Id)>();
        var unhandledProperties = new List<Bakabase.Abstractions.Models.Domain.Property>();
        if (refProperties.Any())
        {
            var localPropertiesMap = (await propertyService.GetProperties(PropertyPool.All)).ToMap();
            foreach (var p in refProperties)
            {
                if (p.Pool == PropertyPool.Custom)
                {
                    var conversion = model.CustomPropertyConversionsMap?.GetValueOrDefault(p.Id);
                    if (conversion != null)
                    {
                        if (conversion is {ToPropertyId: not null, ToPropertyPool: not null})
                        {
                            customPropertyIdConversion[p.Id] = (conversion.ToPropertyPool.Value,
                                conversion.ToPropertyId.Value);
                            continue;
                        }

                        if (conversion.AutoBinding == true)
                        {
                            var lp = localPropertiesMap.GetValueOrDefault(p.Pool)?.Values
                                .FirstOrDefault(x => x.Name == p.Name && x.Type == p.Type);
                            if (lp != null)
                            {
                                customPropertyIdConversion[p.Id] = (lp.Pool, lp.Id);
                            }
                            else
                            {
                                missingProperties.Add(p);
                            }

                            continue;
                        }
                    }
                }
                else
                {
                    var rp = localPropertiesMap.GetValueOrDefault(p.Pool)?.GetValueOrDefault(p.Id);
                    if (rp == null)
                    {
                        throw new Exception(
                            $"{p.Pool} property with id:{p.Id} is not found. The template is not compatible with current version of application.");
                    }
                }

                unhandledProperties.Add(p);
            }


        }

        var refExtensionGroups = flat.ExtractExtensionGroups();
        var extensionGroupConversionsMap = new Dictionary<int, int>();
        var missingExtensionGroups = new List<ExtensionGroup>();
        var unhandledExtensionGroups = new List<ExtensionGroup>();
        if (refExtensionGroups.Any())
        {
            var extensionGroups = await extensionGroupService.GetAll();
            foreach (var rg in refExtensionGroups)
            {
                var conversion = model.ExtensionGroupConversionsMap?.GetValueOrDefault(rg.Id);
                if (conversion != null)
                {
                    if (conversion.ToExtensionGroupId.HasValue)
                    {
                        extensionGroupConversionsMap[rg.Id] = conversion.ToExtensionGroupId.Value;
                        continue;
                    }

                    if (conversion.AutoBinding.HasValue)
                    {
                        var leg =
                            extensionGroups.FirstOrDefault(x =>
                                x.Name == rg.Name && x.Extensions.SetEquals(rg.Extensions));
                        if (leg != null)
                        {
                            extensionGroupConversionsMap[rg.Id] = leg.Id;
                        }
                        else
                        {
                            missingExtensionGroups.Add(rg);
                        }

                        continue;
                    }
                }

                unhandledExtensionGroups.Add(rg);
            }
        }

        if (unhandledExtensionGroups.Any() || unhandledProperties.Any())
        {
            return new MediaLibraryTemplateValidationViewModel
            {
                UnhandledProperties = unhandledProperties,
                UnhandledExtensionGroups = unhandledExtensionGroups
            };
        }

        if (validateOnly)
        {
            return null;
        }

        #endregion

        #region Prepare missing parts

        var addedProperties = await customPropertyService.AddRange(missingProperties.Select(p =>
            new CustomPropertyAddOrPutDto
            {
                Name = p.Name,
                Type = p.Type,
                Options = p.Options.SerializeAsCustomPropertyOptions(true)
            }).ToArray());

        foreach (var p in missingProperties)
        {
            customPropertyIdConversion[p.Id] = addedProperties.First(x => x.Type == p.Type && x.Name == p.Name).Id;
        }

        var addedExtensionGroups = await extensionGroupService.AddRange(missingExtensionGroups.Select(x =>
            new ExtensionGroupAddInputModel(x.Name, x.Extensions)).ToArray());
        foreach (var meg in missingExtensionGroups)
        {
            extensionGroupConversionsMap[meg.Id] = addedExtensionGroups
                .First(x => x.Name == meg.Name && x.Extensions?.SetEquals(meg.Extensions ?? []) == true).Id;
        }

        #endregion

        var templates = flat.Select(f => f.ToDomainModel(customPropertyIdConversion, extensionGroupConversionsMap))
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
}