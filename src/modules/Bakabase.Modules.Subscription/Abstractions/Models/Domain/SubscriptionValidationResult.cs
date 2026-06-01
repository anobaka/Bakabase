namespace Bakabase.Modules.Subscription.Abstractions.Models.Domain;

public record SubscriptionValidationResult(bool IsValid, string? Error = null)
{
    public static readonly SubscriptionValidationResult Valid = new(true);
    public static SubscriptionValidationResult Invalid(string error) => new(false, error);
}
