namespace Bakabase.InsideWorld.Business.Components.BuiltinMediaLibraryTemplate;


using P = BuiltinMediaLibraryTemplateProperty;

public enum BuiltinMediaLibraryTemplateType
{
    [BuiltinMediaLibraryTemplateBuilder()]
    [BuiltinMediaLibraryTemplateBuilder([P.Year])]
    [BuiltinMediaLibraryTemplateBuilder([P.Tag])]
    [BuiltinMediaLibraryTemplateBuilder([P.Tag, P.Year])]
    [BuiltinMediaLibraryTemplate([P.Year, P.Name, P.Tag])]
    Movie = 1,

    [BuiltinMediaLibraryTemplateBuilder()]
    [BuiltinMediaLibraryTemplateBuilder([P.Year])]
    [BuiltinMediaLibraryTemplateBuilder([P.Tag])]
    [BuiltinMediaLibraryTemplateBuilder([P.Tag, P.Year])]
    [BuiltinMediaLibraryTemplate([P.Year, P.ReleaseDate, P.Name, P.Tag])]
    Anime,

    [BuiltinMediaLibraryTemplateBuilder()]
    [BuiltinMediaLibraryTemplateBuilder([P.Year])]
    [BuiltinMediaLibraryTemplateBuilder([P.Tag])]
    [BuiltinMediaLibraryTemplateBuilder([P.Tag, P.Year])]
    [BuiltinMediaLibraryTemplateBuilder([P.Publisher])]
    [BuiltinMediaLibraryTemplate([P.Publisher, P.Name, P.Tag, P.Series])]
    Series,

    [BuiltinMediaLibraryTemplateBuilder()]
    [BuiltinMediaLibraryTemplateBuilder([P.Year])]
    [BuiltinMediaLibraryTemplateBuilder([P.Tag])]
    [BuiltinMediaLibraryTemplateBuilder([P.Tag, P.Year])]
    [BuiltinMediaLibraryTemplateBuilder([P.Author])]
    [BuiltinMediaLibraryTemplate([P.Author, P.Name, P.Tag, P.Series])]
    Manga,

    [BuiltinMediaLibraryTemplateBuilder()]
    [BuiltinMediaLibraryTemplateBuilder([P.Tag])]
    [BuiltinMediaLibraryTemplateBuilder([P.Tag, P.Year])]
    [BuiltinMediaLibraryTemplateBuilder([P.Author])]
    [BuiltinMediaLibraryTemplateBuilder([P.Series])]
    [BuiltinMediaLibraryTemplateBuilder([P.Year, P.Series])]
    [BuiltinMediaLibraryTemplate([P.Author, P.Name, P.Tag, P.Series])]
    Audio
}