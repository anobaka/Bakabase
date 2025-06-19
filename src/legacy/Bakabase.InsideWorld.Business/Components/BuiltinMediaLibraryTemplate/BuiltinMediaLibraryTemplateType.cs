using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Components.BuiltinMediaLibraryTemplate;


using P = BuiltinMediaLibraryTemplateProperty;

public enum BuiltinMediaLibraryTemplateType
{
    [BuiltinMediaLibraryTemplateBuilder]
    [BuiltinMediaLibraryTemplateBuilder([P.Year])]
    [BuiltinMediaLibraryTemplateBuilder([P.Tag])]
    [BuiltinMediaLibraryTemplateBuilder([P.Tag, P.Year])]
    [BuiltinMediaLibraryTemplate(MediaType.Video, [P.Year, P.Name, P.Tag])]
    Movie = 1,

    [BuiltinMediaLibraryTemplateBuilder]
    [BuiltinMediaLibraryTemplateBuilder([P.Year])]
    [BuiltinMediaLibraryTemplateBuilder([P.Tag])]
    [BuiltinMediaLibraryTemplateBuilder([P.Tag, P.Year])]
    [BuiltinMediaLibraryTemplate(MediaType.Video, [P.Year, P.ReleaseDate, P.Name, P.Tag], [EnhancerId.Bangumi])]
    Anime,

    [BuiltinMediaLibraryTemplateBuilder]
    [BuiltinMediaLibraryTemplateBuilder([P.Year])]
    [BuiltinMediaLibraryTemplateBuilder([P.Tag])]
    [BuiltinMediaLibraryTemplateBuilder([P.Tag, P.Year])]
    [BuiltinMediaLibraryTemplateBuilder([P.Publisher])]
    [BuiltinMediaLibraryTemplate(MediaType.Video, [P.Publisher, P.Name, P.Tag, P.Series, P.Year])]
    Series,

    [BuiltinMediaLibraryTemplateBuilder]
    [BuiltinMediaLibraryTemplateBuilder([P.Year])]
    [BuiltinMediaLibraryTemplateBuilder([P.Tag])]
    [BuiltinMediaLibraryTemplateBuilder([P.Tag, P.Year])]
    [BuiltinMediaLibraryTemplateBuilder([P.Author])]
    [BuiltinMediaLibraryTemplate(MediaType.Image, [P.Author, P.Name, P.Tag, P.Series], [EnhancerId.ExHentai])]
    Manga,

    [BuiltinMediaLibraryTemplateBuilder]
    [BuiltinMediaLibraryTemplateBuilder([P.Tag])]
    [BuiltinMediaLibraryTemplateBuilder([P.Tag, P.Year])]
    [BuiltinMediaLibraryTemplateBuilder([P.Author])]
    [BuiltinMediaLibraryTemplateBuilder([P.Series])]
    [BuiltinMediaLibraryTemplateBuilder([P.Year, P.Series])]
    [BuiltinMediaLibraryTemplate(MediaType.Audio, [P.Author, P.Name, P.Tag, P.Series], [EnhancerId.DLsite])]
    Audio
}
