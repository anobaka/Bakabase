using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Newtonsoft.Json;

namespace Bakabase.Abstractions.Extensions;

public static class ResourceProfileExtensions
{
    /// <summary>
    /// Normalizes a single extension to ensure it starts with exactly one dot.
    /// </summary>
    public static string NormalizeExtension(string ext)
    {
        if (string.IsNullOrWhiteSpace(ext))
        {
            return ext;
        }

        return $".{ext.TrimStart('.')}";
    }

    /// <summary>
    /// Normalizes extensions to ensure they all start with a single dot.
    /// This is important because Path.GetExtension() returns extensions with a leading dot (e.g., ".mp4").
    /// </summary>
    public static HashSet<string>? NormalizeExtensions(this HashSet<string>? extensions)
    {
        if (extensions == null || extensions.Count == 0)
        {
            return extensions;
        }

        return extensions
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(NormalizeExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes extensions to ensure they all start with a single dot.
    /// This is important because Path.GetExtension() returns extensions with a leading dot (e.g., ".mp4").
    /// </summary>
    public static void NormalizeExtensions(this ResourceProfilePlayableFileOptions? options)
    {
        if (options?.Extensions == null || options.Extensions.Count == 0)
        {
            return;
        }

        options.Extensions = options.Extensions
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(NormalizeExtension)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Drops <see cref="EnhancerTargetFullOptions"/> entries that have no property binding
    /// (<c>PropertyPool == 0 || PropertyId == 0</c>).
    ///
    /// These entries are legacy zombies from the pre-8d755c2 AV-source-priority layout, where
    /// the panel persisted a target row solely to carry a <c>PreferredSources</c> preference
    /// even before any property was bound to it. The field is now gone (moved to the global
    /// AV sources options), but the entries themselves survived deserialization — only the
    /// removed field is silently ignored by Newtonsoft, not the whole record.
    ///
    /// Left in place, they used to trip
    /// <see cref="Models.Domain.EnhancerFullOptions.TargetOptions"/> handling inside
    /// <c>ApplyEnhancementsToResources</c>; that path now skips unbound entries itself,
    /// so this strip is about keeping stored options clean rather than about crash safety.
    ///
    /// A <b>dynamic</b> target is exempt: its name (a regex capture group, an AI field)
    /// is configuration in its own right, entered before any property is bound to it —
    /// dropping it would erase what the user just typed on the way back out of the DB.
    ///
    /// Returns the number of entries dropped (for diagnostics; callers may log).
    /// </summary>
    public static int StripInvalidEnhancerTargetOptions(this ResourceProfileEnhancerOptions options)
    {
        if (options.Enhancers == null) return 0;
        var dropped = 0;
        foreach (var enhancer in options.Enhancers)
        {
            if (enhancer.TargetOptions == null) continue;
            var before = enhancer.TargetOptions.Count;
            enhancer.TargetOptions = enhancer.TargetOptions
                .Where(t => !string.IsNullOrEmpty(t.DynamicTarget) ||
                            ((int)t.PropertyPool > 0 && t.PropertyId > 0))
                .ToList();
            dropped += before - enhancer.TargetOptions.Count;
        }
        return dropped;
    }

    /// <summary>Each option block independently follows ordinary profile priority; an empty player list explicitly uses the system default.</summary>
    public static ResourceProfileOpeningOptions ResolveOpeningOptions(this IEnumerable<ResourceProfile> profiles)
    {
        var ordered = profiles.OrderByDescending(p => p.Priority).ThenBy(p => p.Id).ToArray();
        return new ResourceProfileOpeningOptions
        {
            ProfileIds = ordered.Select(p => p.Id).ToArray(),
            OpeningMode = ordered.FirstOrDefault(p => p.OpeningMode != null)?.OpeningMode,
            PlayerOptions = ordered.FirstOrDefault(p => p.PlayerOptions != null)?.PlayerOptions
        };
    }

    public static ResourceProfileDbModel ToDbModel(this ResourceProfile model, string? searchJson)
    {
        return new ResourceProfileDbModel
        {
            Id = model.Id,
            Name = model.Name,
            SourceDeviceId = model.SourceDeviceId,
            SourceLibraryEpoch = model.SourceLibraryEpoch,
            OpeningMode = model.OpeningMode,
            SearchJson = searchJson,
            NameTemplate = model.NameTemplate,
            EnhancerSettingsJson = model.EnhancerOptions != null
                ? JsonConvert.SerializeObject(model.EnhancerOptions)
                : null,
            PlayableFileSettingsJson = model.PlayableFileOptions != null
                ? JsonConvert.SerializeObject(model.PlayableFileOptions)
                : null,
            PlayerSettingsJson = model.PlayerOptions != null
                ? JsonConvert.SerializeObject(model.PlayerOptions)
                : null,
            PropertiesJson = model.PropertyOptions != null
                ? JsonConvert.SerializeObject(model.PropertyOptions)
                : null,
            Priority = model.Priority,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    public static ResourceProfile ToDomainModel(this ResourceProfileDbModel dbModel, ResourceSearch? search)
    {
        var domain = new ResourceProfile
        {
            Id = dbModel.Id,
            Name = dbModel.Name,
            SourceDeviceId = dbModel.SourceDeviceId,
            SourceLibraryEpoch = dbModel.SourceLibraryEpoch,
            OpeningMode = dbModel.OpeningMode,
            SearchJson = dbModel.SearchJson,
            NameTemplate = dbModel.NameTemplate,
            Priority = dbModel.Priority,
            CreatedAt = dbModel.CreatedAt,
            UpdatedAt = dbModel.UpdatedAt,
            Search = search ?? new ResourceSearch()
        };

        if (!string.IsNullOrEmpty(dbModel.EnhancerSettingsJson))
        {
            try
            {
                domain.EnhancerOptions =
                    JsonConvert.DeserializeObject<ResourceProfileEnhancerOptions>(dbModel.EnhancerSettingsJson);
                domain.EnhancerOptions?.StripInvalidEnhancerTargetOptions();
            }
            catch (Exception)
            {
                // ignored
            }
        }

        if (!string.IsNullOrEmpty(dbModel.PlayableFileSettingsJson))
        {
            try
            {
                domain.PlayableFileOptions =
                    JsonConvert.DeserializeObject<ResourceProfilePlayableFileOptions>(dbModel.PlayableFileSettingsJson);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        if (!string.IsNullOrEmpty(dbModel.PlayerSettingsJson))
        {
            try
            {
                domain.PlayerOptions =
                    JsonConvert.DeserializeObject<ResourceProfilePlayerOptions>(dbModel.PlayerSettingsJson);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        if (!string.IsNullOrEmpty(dbModel.PropertiesJson))
        {
            try
            {
                domain.PropertyOptions =
                    JsonConvert.DeserializeObject<ResourceProfilePropertyOptions>(dbModel.PropertiesJson);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        return domain;
    }
}
