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

    public static ResourceProfileDbModel ToDbModel(this ResourceProfile model, string? searchJson)
    {
        return new ResourceProfileDbModel
        {
            Id = model.Id,
            Name = model.Name,
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
