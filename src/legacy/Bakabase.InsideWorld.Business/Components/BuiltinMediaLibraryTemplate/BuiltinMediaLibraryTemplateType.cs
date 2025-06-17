using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.BuiltinMediaLibraryTemplate;


using P = BuiltinMediaLibraryTemplateProperty;

public enum BuiltinMediaLibraryTemplateType
{
    [BuiltinMediaLibraryTemplateBuilder("b7c8025a-c6ae-4a70-b4d4-e71c413eb11c")]
    [BuiltinMediaLibraryTemplateBuilder("e1a1c2b3-4d5e-6f70-8a91-b2c3d4e5f601", [P.Year])]
    [BuiltinMediaLibraryTemplateBuilder("f2b3c4d5-6e7f-8091-a2b3-c4d5e6f70213", [P.Tag])]
    [BuiltinMediaLibraryTemplateBuilder("a3c4d5e6-7f80-91a2-b3c4-d5e6f7032415", [P.Tag, P.Year])]
    [BuiltinMediaLibraryTemplate(MediaType.Video, [P.Year, P.Name, P.Tag])]
    Movie = 1,

    [BuiltinMediaLibraryTemplateBuilder("b4d5e6f7-8091-a2b3-c4d5-e6f704356178")]
    [BuiltinMediaLibraryTemplateBuilder("c5e6f708-091a-2b3c-4d5e-6f7054678912", [P.Year])]
    [BuiltinMediaLibraryTemplateBuilder("d6f7091a-2b3c-4d5e-6f70-548790123456", [P.Tag])]
    [BuiltinMediaLibraryTemplateBuilder("e7f80a1b-2c3d-4e5f-6071-892134567890", [P.Tag, P.Year])]
    [BuiltinMediaLibraryTemplate(MediaType.Video, [P.Year, P.ReleaseDate, P.Name, P.Tag])]
    Anime,

    [BuiltinMediaLibraryTemplateBuilder("f8091a2b-3c4d-5e6f-7081-9234567890ab")]
    [BuiltinMediaLibraryTemplateBuilder("091a2b3c-4d5e-6f70-8192-34567890abcd", [P.Year])]
    [BuiltinMediaLibraryTemplateBuilder("1a2b3c4d-5e6f-7081-9234-567890abcde1", [P.Tag])]
    [BuiltinMediaLibraryTemplateBuilder("2b3c4d5e-6f70-8192-3456-7890abcdef12", [P.Tag, P.Year])]
    [BuiltinMediaLibraryTemplateBuilder("3c4d5e6f-7081-9234-5678-90abcdef1234", [P.Publisher])]
    [BuiltinMediaLibraryTemplate(MediaType.Video, [P.Publisher, P.Name, P.Tag, P.Series])]
    Series,

    [BuiltinMediaLibraryTemplateBuilder("4d5e6f70-8192-3456-7890-abcd12345678")]
    [BuiltinMediaLibraryTemplateBuilder("5e6f7081-9234-5678-90ab-cdef12345679", [P.Year])]
    [BuiltinMediaLibraryTemplateBuilder("6f708192-3456-7890-abcd-ef123456789a", [P.Tag])]
    [BuiltinMediaLibraryTemplateBuilder("70819234-5678-90ab-cdef-123456789abc", [P.Tag, P.Year])]
    [BuiltinMediaLibraryTemplateBuilder("81923456-7890-abcd-ef12-3456789abcd1", [P.Author])]
    [BuiltinMediaLibraryTemplate(MediaType.Image, [P.Author, P.Name, P.Tag, P.Series])]
    Manga,

    [BuiltinMediaLibraryTemplateBuilder("92345678-90ab-cdef-1234-56789abcde12")]
    [BuiltinMediaLibraryTemplateBuilder("a2345678-90ab-cdef-1234-56789abcdef1", [P.Tag])]
    [BuiltinMediaLibraryTemplateBuilder("b3456789-0abc-def1-2345-6789abcdef12", [P.Tag, P.Year])]
    [BuiltinMediaLibraryTemplateBuilder("c456789a-bcde-f123-4567-89abcdef1234", [P.Author])]
    [BuiltinMediaLibraryTemplateBuilder("d56789ab-cdef-1234-5678-9abcdef12345", [P.Series])]
    [BuiltinMediaLibraryTemplateBuilder("e6789abc-def1-2345-6789-abcdef123456", [P.Year, P.Series])]
    [BuiltinMediaLibraryTemplate(MediaType.Audio, [P.Author, P.Name, P.Tag, P.Series])]
    Audio
}