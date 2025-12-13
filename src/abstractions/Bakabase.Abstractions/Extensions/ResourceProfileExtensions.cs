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
            EnhancerSettingsJson = model.EnhancerSettings != null
                ? JsonConvert.SerializeObject(model.EnhancerSettings)
                : null,
            PlayableFileSettingsJson = model.PlayableFileSettings != null
                ? JsonConvert.SerializeObject(model.PlayableFileSettings)
                : null,
            PlayerSettingsJson = model.PlayerSettings != null
                ? JsonConvert.SerializeObject(model.PlayerSettings)
                : null,
            Priority = model.Priority,
            CreateDt = model.CreateDt,
            UpdateDt = model.UpdateDt
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
            CreateDt = dbModel.CreateDt,
            UpdateDt = dbModel.UpdateDt,
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
                domain.EnhancerSettings =
                    JsonConvert.DeserializeObject<EnhancerSettings>(dbModel.EnhancerSettingsJson);
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
                domain.PlayableFileSettings =
                    JsonConvert.DeserializeObject<PlayableFileSettings>(dbModel.PlayableFileSettingsJson);
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
                domain.PlayerSettings =
                    JsonConvert.DeserializeObject<PlayerSettings>(dbModel.PlayerSettingsJson);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        return domain;
    }
}
