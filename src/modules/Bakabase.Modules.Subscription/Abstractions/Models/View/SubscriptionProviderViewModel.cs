using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Subscription.Abstractions.Models.View;

public record SubscriptionProviderViewModel
{
    public string Kind { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Icon { get; set; }

    /// <summary>What relationship this source has to the things it lists.</summary>
    public SubscriptionSourceKind SourceKind { get; set; }

    /// <summary>The identity namespace its keys belong to; null for a sharing channel.</summary>
    public ResourceSource? ResourceSource { get; set; }
}
