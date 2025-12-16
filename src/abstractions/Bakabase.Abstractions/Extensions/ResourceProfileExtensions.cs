using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Newtonsoft.Json;

namespace Bakabase.Abstractions.Extensions;

public static class ResourceProfileExtensions
{
    public static ResourceProfileDbModel ToDbModel(this ResourceProfile model)
    {
        return new ResourceProfileDbModel
        {
            Id = model.Id,
            Name = model.Name,
            SearchCriteriaJson = JsonConvert.SerializeObject(model.SearchCriteria),
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
            Priority = model.Priority,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    public static ResourceProfile ToDomainModel(this ResourceProfileDbModel dbModel)
    {
        var domain = new ResourceProfile
        {
            Id = dbModel.Id,
            Name = dbModel.Name,
            NameTemplate = dbModel.NameTemplate,
            Priority = dbModel.Priority,
            CreatedAt = dbModel.CreatedAt,
            UpdatedAt = dbModel.UpdatedAt,
            SearchCriteria = new SearchCriteria()
        };

        if (!string.IsNullOrEmpty(dbModel.SearchCriteriaJson))
        {
            try
            {
                domain.SearchCriteria =
                    JsonConvert.DeserializeObject<SearchCriteria>(dbModel.SearchCriteriaJson) ?? new SearchCriteria();
            }
            catch (Exception)
            {
                // ignored
            }
        }

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

        return domain;
    }
}
