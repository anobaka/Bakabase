using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Service.Models.View;

public record SavedSearchViewModel(string Id, ResourceSearchViewModel Search, string Name, FilterDisplayMode DisplayMode);