using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Extensions;

public static class BTaskExtensions
{
    /// <summary>
    /// Creates a BTask from a BTaskHandlerBuilder
    /// </summary>
    public static BTask ToBTask(this BTaskHandlerBuilder builder, DateTime? enableAfter = null, TimeSpan? interval = null)
    {
        return new BTask(
            builder.Id,
            builder.GetName,
            builder.GetDescription,
            builder.GetMessageOnInterruption,
            builder.ConflictKeys,
            builder.DependsOn,
            builder.Level,
            builder.IsPersistent,
            builder.Type,
            builder.ResourceType,
            builder.ResourceKeys,
            builder.RetryPolicy,
            builder.DependencyFailurePolicy)
        {
            EnableAfter = enableAfter,
            Interval = interval ?? builder.Interval
        };
    }
}
