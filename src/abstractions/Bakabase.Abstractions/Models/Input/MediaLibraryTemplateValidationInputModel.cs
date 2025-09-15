namespace Bakabase.Abstractions.Models.Input;

public record MediaLibraryTemplateValidationInputModel(string RootPath, int? LimitResourcesCount, string? ResourceKeyword);