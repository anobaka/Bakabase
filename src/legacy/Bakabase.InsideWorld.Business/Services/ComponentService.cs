using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Component;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Resource.Components;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Extensions;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using ComponentDescriptor = Bakabase.Abstractions.Models.Domain.ComponentDescriptor;

namespace Bakabase.InsideWorld.Business.Services;

[Obsolete]
public class ComponentService : BootstrapService
{
    private static readonly Dictionary<Type, ComponentAttribute> StaticComponentTypeAttributesMap;
    private static readonly Dictionary<string, Type> StaticComponentTypeMap;
    private static readonly ComponentDescriptor[] StaticComponentDescriptors;
    private static readonly Dictionary<string, ComponentDescriptor> StaticComponentDescriptorsMap;

    private static readonly ConcurrentDictionary<string, IComponentDataVersionGenerator>
        EnhancerTypeDataVersionGeneratorsMap = new();

    static ComponentService()
    {
        StaticComponentTypeAttributesMap = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a =>
            {
                try
                {
                    return a.GetTypes();
                }
                catch
                {
                    return [];
                }
            })
            .ToDictionary(a => a, a => a.GetCustomAttribute<ComponentAttribute>()).Where(a => a.Value != null)
            .ToDictionary(a => a.Key, a => a.Value);
        StaticComponentTypeMap =
            StaticComponentTypeAttributesMap.Keys.ToDictionary(a => a.AssemblyQualifiedName, a => a);
        StaticComponentDescriptors =
            StaticComponentTypeAttributesMap.Select(a =>
            {
                var cd = a.Value.ToDescriptor(a.Key);
                cd.Type = a.Value.OptionsType == null
                    ? ComponentDescriptorType.Fixed
                    : ComponentDescriptorType.Configurable;
                return cd;
            }).ToArray();
        StaticComponentDescriptorsMap = StaticComponentDescriptors.GroupBy(a => a.AssemblyQualifiedTypeName)
            .ToDictionary(a => a.Key, a => a.FirstOrDefault());
    }

    public ComponentService(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    private ComponentOptionsService OptionsService => GetRequiredService<ComponentOptionsService>();
    private ComponentOptionsService ComponentOptionsService => GetRequiredService<ComponentOptionsService>();
    private ICategoryService ResourceCategoryService => GetRequiredService<ICategoryService>();
    private CategoryComponentService CategoryComponentService => GetRequiredService<CategoryComponentService>();
    private IStringLocalizer<SharedResource> Localizer => GetRequiredService<IStringLocalizer<SharedResource>>();

    private IComponentDataVersionGenerator GetDataVersionGenerator(string assemblyQualifiedTypeName)
    {
        var g = EnhancerTypeDataVersionGeneratorsMap.GetOrAdd(assemblyQualifiedTypeName, () =>
        {
            IComponentDataVersionGenerator g = null;
            if (StaticComponentTypeMap.TryGetValue(assemblyQualifiedTypeName, out var t))
                if (StaticComponentTypeAttributesMap.TryGetValue(t, out var ca))
                {
                    if (ca.DataVersionGeneratorType == null) return DefaultComponentDataVersionGenerator.Instance;

                    return GetRequiredService(ca.DataVersionGeneratorType) as IComponentDataVersionGenerator;
                }

            return UnknownComponentDataVersionGenerator.Instance;
        });
        return g;
    }

    private async Task<ComponentDescriptor> WithDataVersionAsync(ComponentDescriptor cd)
    {
        var g = GetDataVersionGenerator(cd.AssemblyQualifiedTypeName);
        var dv = await g.GetVersionAsync(cd);
        return cd with
        {
            DataVersion = dv
        };
    }

    public async Task<ComponentDescriptor> GetDescriptor(string key)
    {
        return (await GetDescriptors(new[] {key})).FirstOrDefault();
    }

    public async Task<ComponentDescriptor[]> GetDescriptors(ComponentType? componentType = null)
    {
        var options =
            await OptionsService.GetAll(a => !componentType.HasValue || a.ComponentType == componentType.Value);
        var list = StaticComponentDescriptors
            .Where(a => !componentType.HasValue || componentType.Value == a.ComponentType).Select(a => a with { })
            .ToList();
        var keys = options.Select(a => a.Id.ToString()).Concat(list.Select(s => s.AssemblyQualifiedTypeName))
            .ToArray();
        return await GetDescriptors(keys);
    }

    public async Task<ComponentDescriptor[]> GetDescriptors(string[] keys)
    {
        if (keys.Any(k => k.IsNullOrEmpty())) throw new Exception(Localizer[SharedResource.Component_KeyCanNotBeEmpty]);

        var list = new List<ComponentDescriptor>();
        var typedKeys = keys.Select(a => (int.TryParse(a, out var id) ? id : (object) a)!).ToArray();
        var optionIds = typedKeys.Where(a => a is int).Cast<int>().Distinct().ToArray();
        var optionsMap =
            (await OptionsService.GetAll(a => optionIds.Contains(a.Id))).ToDictionary(a => a.Id, a => a);

        foreach (var key in typedKeys)
            if (key is int k)
            {
                if (optionsMap.TryGetValue(k, out var o))
                    list.Add(StaticComponentDescriptorsMap.TryGetValue(o.ComponentAssemblyQualifiedTypeName,
                        out var cd)
                        ? cd.WithOptions(o, Localizer)
                        : o.ToInvalidDescriptor(Localizer));
                else
                    list.Add(new ComponentDescriptor
                    {
                        OptionsId = k,
                        Message = Localizer[SharedResource.Component_OptionsWithIdAreNotFound, k]
                    });
            }
            else
            {
                var typeKey = key.ToString()!;
                list.Add(StaticComponentDescriptorsMap.TryGetValue(typeKey, out var cd)
                    ? await WithDataVersionAsync(cd)
                    : new ComponentDescriptor
                    {
                        AssemblyQualifiedTypeName = typeKey,
                        Message = Localizer[SharedResource.TypeIsNotFound, typeKey]
                    });
            }

        var tasks = list.Select(WithDataVersionAsync).ToArray();
        await Task.WhenAll(tasks);

        var descriptors = tasks.Select(a => a.Result).ToArray();

        return descriptors;
    }

    public async Task<ComponentDescriptor> GetDescriptorDto(string key,
        ComponentDescriptorAdditionalItem additionalItems = ComponentDescriptorAdditionalItem.None)
    {
        return (await GetDescriptorDtoList(new[] {key}, additionalItems)).FirstOrDefault();
    }

    public async Task<ComponentDescriptor[]> GetDescriptorDtoList(ComponentType? componentType = null,
        ComponentDescriptorAdditionalItem additionalItems = ComponentDescriptorAdditionalItem.None)
    {
        var ds = await GetDescriptors(componentType);
        return await ToDtoList(ds, additionalItems);
    }

    public async Task<ComponentDescriptor[]> GetDescriptorDtoList(string[] keys,
        ComponentDescriptorAdditionalItem additionalItems = ComponentDescriptorAdditionalItem.None)
    {
        var ds = await GetDescriptors(keys);
        return await ToDtoList(ds, additionalItems);
    }

    private async Task<ComponentDescriptor[]> ToDtoList(ComponentDescriptor[] descriptors,
        ComponentDescriptorAdditionalItem additionalItems = ComponentDescriptorAdditionalItem.None)
    {
        var dtoList = descriptors.Select(d => d.ToDto()).ToArray();

        var keys = dtoList.Select(a => a.Id).ToArray();

        if (additionalItems != ComponentDescriptorAdditionalItem.None)
            foreach (var additionalItem in SpecificEnumUtils<ComponentDescriptorAdditionalItem>.Values)
                if (additionalItems.HasFlag(additionalItem))
                    switch (additionalItem)
                    {
                        case ComponentDescriptorAdditionalItem.None:
                            break;
                        case ComponentDescriptorAdditionalItem.AssociatedCategories:
                        {
                            var mappings =
                                await CategoryComponentService.GetAll(b => keys.Contains(b.ComponentKey));
                            var componentKeyCategoryIdMap = mappings.GroupBy(x => x.ComponentKey)
                                .ToDictionary(x => x.Key, a => a.Select(b => b.CategoryId).Distinct().ToArray());
                            var categoryIds = mappings.Select(a => a.CategoryId).Distinct().ToArray();
                            var categories =
                                await ResourceCategoryService.GetAll(a => categoryIds.Contains(a.Id));
                            var categoryMap = categories.ToDictionary(x => x.Id, x => x);
                            foreach (var d in dtoList.Where(x => x.Id.IsNotEmpty()))
                                if (componentKeyCategoryIdMap.TryGetValue(d.Id, out var ids))
                                {
                                    var tCategories =
                                        ids.Select(id => categoryMap.TryGetValue(id, out var c) ? c : null)
                                            .Where(a => a != null).ToArray();
                                    d.AssociatedCategories = tCategories;
                                }

                            break;
                        }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

        return dtoList;
    }

    public async Task<Dictionary<IComponent, ComponentDescriptor>> CreateInstances(string[] componentKeys)
    {
        return await CreateInstances<IComponent>(componentKeys);
    }

    public async Task<Dictionary<TComponent, ComponentDescriptor>>
        CreateInstances<TComponent>(string[] componentKeys) where TComponent : class, IComponent
    {
        var cds = await GetDescriptors(componentKeys);
        var invalidCds = cds.Where(a => a.Type == ComponentDescriptorType.Invalid).ToArray();
        if (invalidCds.Any())
            throw new Exception(Localizer[SharedResource.Component_Invalid,
                string.Join(',', invalidCds.Select(c => c.Name))]);

        var instances = new Dictionary<TComponent, ComponentDescriptor>();
        foreach (var cd in cds)
        {
            var type = Type.GetType(cd.AssemblyQualifiedTypeName)!;

            // if options exists and constructor does not need it, just ignore it.
            var constructor = type.GetConstructors().FirstOrDefault()!;
            var parameters = constructor.GetParameters();
            var options = cd.OptionsType == null
                ? null
                : JsonConvert.DeserializeObject(cd.OptionsJson, cd.OptionsType);

            var parameterValues = parameters.Select(a =>
            {
                var pt = a.ParameterType;
                return pt == cd.OptionsType ? options : GetRequiredService(pt);
            }).ToArray();

            var instance = (Activator.CreateInstance(type, parameterValues) as TComponent)!;
            instances[instance] = cd;
        }

        return instances;
    }
}