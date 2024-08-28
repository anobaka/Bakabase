namespace Bakabase.Abstractions.Extensions;

public static class EnhancementRecordExtensions
{
    public static Bakabase.Abstractions.Models.Domain.EnhancementRecord ToDomainModel(
        this Bakabase.Abstractions.Models.Db.EnhancementRecord record)
    {
        return new Bakabase.Abstractions.Models.Domain.EnhancementRecord
        {
            Id = record.Id,
            ResourceId = record.ResourceId,
            EnhancerId = record.EnhancerId,
            EnhancedAt = record.EnhancedAt
        };
    }

    public static Bakabase.Abstractions.Models.Db.EnhancementRecord ToDbModel(
        this Bakabase.Abstractions.Models.Domain.EnhancementRecord record)
    {
        return new Bakabase.Abstractions.Models.Db.EnhancementRecord
        {
            Id = record.Id,
            ResourceId = record.ResourceId,
            EnhancerId = record.EnhancerId,
            EnhancedAt = record.EnhancedAt
        };
    }
}