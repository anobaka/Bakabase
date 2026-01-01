using Bakabase.Abstractions.Models.Domain;
using Newtonsoft.Json;

namespace Bakabase.Abstractions.Extensions;

public static class EnhancementRecordExtensions
{
    public static Bakabase.Abstractions.Models.Domain.EnhancementRecord ToDomainModel(
        this Bakabase.Abstractions.Models.Db.EnhancementRecord record)
    {
        var domain = new Bakabase.Abstractions.Models.Domain.EnhancementRecord
        {
            Id = record.Id,
            ResourceId = record.ResourceId,
            EnhancerId = record.EnhancerId,
            ContextAppliedAt = record.ContextAppliedAt,
            ContextCreatedAt = record.ContextCreatedAt,
            Status = record.Status,
            ErrorMessage = record.ErrorMessage
        };

        if (!string.IsNullOrEmpty(record.Logs))
        {
            try
            {
                domain.Logs = JsonConvert.DeserializeObject<List<EnhancementLog>>(record.Logs);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        if (!string.IsNullOrEmpty(record.OptionsSnapshot))
        {
            try
            {
                domain.OptionsSnapshot = JsonConvert.DeserializeObject<EnhancerFullOptions>(record.OptionsSnapshot);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        return domain;
    }

    public static Bakabase.Abstractions.Models.Db.EnhancementRecord ToDbModel(
        this Bakabase.Abstractions.Models.Domain.EnhancementRecord record)
    {
        return new Bakabase.Abstractions.Models.Db.EnhancementRecord
        {
            Id = record.Id,
            ResourceId = record.ResourceId,
            EnhancerId = record.EnhancerId,
            ContextAppliedAt = record.ContextAppliedAt,
            ContextCreatedAt = record.ContextCreatedAt,
            Status = record.Status,
            Logs = record.Logs != null ? JsonConvert.SerializeObject(record.Logs) : null,
            OptionsSnapshot = record.OptionsSnapshot != null
                ? JsonConvert.SerializeObject(record.OptionsSnapshot)
                : null,
            ErrorMessage = record.ErrorMessage
        };
    }
}