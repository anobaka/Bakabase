using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.BuiltinMediaLibraryTemplate;

public interface IBuiltinMediaLibraryTemplateLocalizer
{
    string BuiltinMediaLibraryTemplate_TypeName(BuiltinMediaLibraryTemplateType type);
    string BuiltinMediaLibraryTemplate_PropertyName(BuiltinMediaLibraryTemplateProperty property);
}