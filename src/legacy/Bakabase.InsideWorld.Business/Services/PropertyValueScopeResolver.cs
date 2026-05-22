using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Services;

/// <summary>
/// Resolves a resource property's per-scope values to the single effective value. Owns where the
/// global scope priority comes from (ResourceOptions.PropertyValueScopePriority), so callers never
/// pass priority configuration. The per-resource preference wins, the configured global priority is
/// the fallback, empty scopes are skipped. C# counterpart of the frontend core/models/Resource.ts
/// resolver — keep the two in sync.
/// </summary>
public class PropertyValueScopeResolver(IBOptions<ResourceOptions> resourceOptions) : IPropertyValueScopeResolver
{
    public Resource.Property.PropertyValue? Resolve(Resource resource, PropertyPool pool, int propertyId)
    {
        var values = resource.Properties?
            .GetValueOrDefault((int)pool)?
            .GetValueOrDefault(propertyId)?
            .Values;
        if (values is not { Count: > 0 })
        {
            return null;
        }

        var preference = resource.ScopePreferences?
            .FirstOrDefault(p => p.PropertyPool == pool && p.PropertyId == propertyId);
        var effectivePriority = BuildEffectiveScopePriority(
            NormalizeGlobalPriority(resourceOptions.Value.PropertyValueScopePriority), preference);

        foreach (var scope in effectivePriority)
        {
            var value = values.FirstOrDefault(v => v.Scope == (int)scope);
            if (value != null && IsNonEmptyValue(value.AliasAppliedBizValue ?? value.BizValue))
            {
                return value;
            }
        }

        return null;
    }

    // Whether a resolved value actually holds content. A plain null/falsy check is wrong here:
    // an empty string or collection is "empty", but 0 and false are valid values.
    private static bool IsNonEmptyValue(object? value)
    {
        switch (value)
        {
            case null:
                return false;
            case string s:
                return !string.IsNullOrWhiteSpace(s);
            case IEnumerable enumerable:
                foreach (var _ in enumerable)
                {
                    return true;
                }

                return false;
            default:
                return true;
        }
    }

    // Expands the configured global priority into a full ordered scope list: every scope appears
    // exactly once; scopes missing from the configuration are appended, Manual first. An empty
    // configuration yields every scope in declaration order. Mirrors the frontend valueScopePriority.
    private static List<PropertyValueScope> NormalizeGlobalPriority(IEnumerable<PropertyValueScope>? configuredPriority)
    {
        var allScopes = Enum.GetValues<PropertyValueScope>();
        var ordered = configuredPriority?.ToList() is { Count: > 0 } configured ? configured : allScopes.ToList();

        foreach (var scope in allScopes)
        {
            if (ordered.Contains(scope))
            {
                continue;
            }

            if (scope == PropertyValueScope.Manual)
            {
                ordered.Insert(0, scope);
            }
            else
            {
                ordered.Add(scope);
            }
        }

        return ordered;
    }

    // Builds the effective scope chain for one property. When a per-resource preference carries
    // priorities it fully replaces the global priority; an entry with FallbackOnEmpty == false
    // truncates the chain there. Mirrors the frontend buildEffectiveScopePriority.
    private static IReadOnlyList<PropertyValueScope> BuildEffectiveScopePriority(
        IReadOnlyList<PropertyValueScope> globalPriority, PropertyValueScopePreference? preference)
    {
        var priorities = preference?.Priorities;
        if (priorities is not { Length: > 0 })
        {
            return globalPriority;
        }

        var chain = new List<PropertyValueScope>(priorities.Length);
        foreach (var p in priorities)
        {
            chain.Add(p.Scope);
            if (!p.FallbackOnEmpty)
            {
                break;
            }
        }

        return chain;
    }
}
