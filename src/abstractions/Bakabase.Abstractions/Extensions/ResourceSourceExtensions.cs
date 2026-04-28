namespace Bakabase.Abstractions.Models.Domain.Constants;

public static class ResourceSourceExtensions
{
    public static PropertyValueScope GetPropertyValueScope(this ResourceSource source) => source switch
    {
        ResourceSource.Steam => PropertyValueScope.Steam,
        ResourceSource.DLsite => PropertyValueScope.DLsite,
        ResourceSource.ExHentai => PropertyValueScope.ExHentai,
        _ => PropertyValueScope.Synchronization
    };
}
