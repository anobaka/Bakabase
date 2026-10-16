using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.Property.Models.View;
using Bakabase.Service.Extensions;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bootstrap.Models.ResponseModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Bakabase.Service.Components.Federation;

/// <summary>Read-only library data used to author a profile on another device. No options or player APIs.</summary>
public sealed class ResourceProfileSourceReader(IPropertyService properties, IPropertyLocalizer localizer,
    IResourceProfileSourceEvaluator evaluator, IResourceService resources, INodeIdentityProvider identity)
{
    public const int MaximumProperties = 4096;
    public const int MaximumResponseBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerSettings Json = new()
        { ContractResolver = new CamelCasePropertyNamesContractResolver(), MaxDepth = 64 };

    public async Task<ResourceProfileSourceInfo> InfoAsync(CancellationToken ct)
    {
        var node = await identity.GetAsync(ct);
        return new ResourceProfileSourceInfo(node.NodeId, node.Name, node.LibraryEpoch);
    }

    public async Task<ListResponse<PropertyViewModel>> PropertiesAsync(CancellationToken ct)
    {
        var all = await properties.GetProperties(PropertyPool.All, includeDeprecated: true);
        ct.ThrowIfCancellationRequested();
        var searchable = all.Where(p => PropertySystem.Property.TryGetSearchHandler(p.Type) != null).ToArray();
        if (searchable.Length > MaximumProperties)
            throw new FederationQueryException("ProfileMetadataTooLarge", 422);
        var result = new ListResponse<PropertyViewModel>(searchable.Select(ToSearchProperty));
        CheckBudget(result);
        return result;
    }

    public async Task<SingletonResponse<PropertyViewModel>> FilterValueAsync(ResourceSearchFilterInputModel input,
        CancellationToken ct)
    {
        if (input.PropertyPool is not (PropertyPool.Internal or PropertyPool.Reserved or PropertyPool.Custom) ||
            input.PropertyId is not > 0 || input.Operation == null)
            throw new FederationQueryException("InvalidProfileFilter", 422);
        var property = await properties.GetProperty(input.PropertyPool.Value, input.PropertyId.Value);
        var handler = PropertySystem.Property.TryGetSearchHandler(property.Type);
        if (handler?.SearchOperations.TryGetValue(input.Operation.Value, out var options) != true)
            throw new FederationQueryException("InvalidProfileFilter", 422);
        ct.ThrowIfCancellationRequested();
        if (options?.ConvertProperty != null) property = options.ConvertProperty(property);
        var result = new SingletonResponse<PropertyViewModel>(ToSearchProperty(property));
        CheckBudget(result);
        return result;
    }

    public async Task<SearchResponse<ResourceProfilePreviewResource>> PreviewAsync(ResourceSearchInputModel input,
        bool includePaths, CancellationToken ct)
    {
        input.StandardPageable();
        if (input.PageSize > 100 || input.Page > 1_000_000)
            throw new FederationQueryException("InvalidProfilePreview", 422);
        var search = await evaluator.ParseSearchAsync(JsonConvert.SerializeObject(input.ToDbModel()), ct);
        search.PageIndex = input.Page;
        search.PageSize = input.PageSize;
        var result = await resources.Search(search, ResourceAdditionalItem.None);
        ct.ThrowIfCancellationRequested();
        var preview = new SearchResponse<ResourceProfilePreviewResource>(result.Data.Select(r =>
            new ResourceProfilePreviewResource(r.Id, includePaths ? r.Path : null, r.FileName)),
            result.TotalCount, input.Page, input.PageSize);
        CheckBudget(preview);
        return preview;
    }

    private PropertyViewModel ToSearchProperty(Bakabase.Abstractions.Models.Domain.Property property)
    {
        var view = property.ToViewModel(localizer);
        SanitizeProperty(view);
        return view;
    }

    private static void SanitizeProperty(PropertyViewModel view)
    {
        // Only the choice vocabulary belongs to library search. Defaults, attachment settings and any
        // future property configuration are intentionally absent; this is not definitions synchronization.
        if (view.Options != null)
        {
            var options = JObject.FromObject(view.Options, JsonSerializer.Create(Json));
            foreach (var key in options.Properties().Select(x => x.Name).ToArray())
                if (key is not ("choices" or "tags" or "data" or "ignoreCase" or "valueIsSingleton"))
                    options.Remove(key);
            view.Options = options.Count == 0 ? null : options;
        }
    }

    public async Task<SingletonResponse<ResourceSearchViewModel>> ResolveSearchAsync(ResourceSearchInputModel input,
        CancellationToken ct)
    {
        // Validate before hydrating: the ordinary view conversion deliberately tolerates deleted filters,
        // while a remote profile must never silently become broader than the rule the user saved.
        await evaluator.ParseSearchAsync(JsonConvert.SerializeObject(input.ToDbModel()), ct);
        var views = await new[] { input.ToDbModel() }.ToViewModels(properties, localizer, resources);
        ct.ThrowIfCancellationRequested();
        SanitizeGroup(views[0].Group);
        var result = new SingletonResponse<ResourceSearchViewModel>(views[0]);
        CheckBudget(result);
        return result;
    }

    private static void SanitizeGroup(ResourceSearchFilterGroupViewModel? group)
    {
        if (group == null) return;
        foreach (var filter in group.Filters ?? [])
        {
            if (filter.Property != null) SanitizeProperty(filter.Property);
            if (filter.ValueProperty != null) SanitizeProperty(filter.ValueProperty);
        }
        foreach (var child in group.Groups ?? []) SanitizeGroup(child);
    }

    public static void CheckBudget(object value)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(value, Json)) > MaximumResponseBytes)
            throw new FederationQueryException("ProfileMetadataTooLarge", 422);
    }
}
