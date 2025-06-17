using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Services;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.BuiltinMediaLibraryTemplate;

public class BuiltinMediaLibraryTemplateService(
    IMediaLibraryTemplateService mlTemplateService,
    IBakabaseLocalizer localizer)
{
    public BuiltinMediaLibraryTemplateDescriptor[] GetAll()
    {
        var types = SpecificEnumUtils<BuiltinMediaLibraryTemplateType>.Values;

        var descriptors = types.SelectMany(t =>
        {
            var attr = t.GetType().GetCustomAttribute<BuiltinMediaLibraryTemplateAttribute>()!;
            var builders = t.GetType().GetCustomAttributes<BuiltinMediaLibraryTemplateBuilderAttribute>();

            return builders.Select(b =>
            {
                var d = new BuiltinMediaLibraryTemplateDescriptor
                {
                    Name = localizer.BuiltinMediaLibraryTemplate_Name((int) t),
                    Description = localizer.BuiltinMediaLibraryTemplate_Description((int) t),
                    Properties = attr.Properties
                        .Select(p => localizer.BuiltinMediaLibraryTemplate_PropertyName((int) p)).ToArray()
                };

                if (b.Properties?.Any() == true)
                {
                    d.LayerProperties = [];
                    for (var index = 0; index < b.Properties.Length; index++)
                    {
                        var p = b.Properties[index];
                        d.LayerProperties[index] = localizer.BuiltinMediaLibraryTemplate_PropertyName((int) p);
                    }
                }

                return d;
            });
        }).ToArray();

        return descriptors;
    }

    public async Task Add(BuiltinMediaLibraryTemplateType type, int builderIdx)
    {
        var attr = type.GetType().GetCustomAttribute<BuiltinMediaLibraryTemplateAttribute>()!;
        var builder =
            type.GetType().GetCustomAttributes<BuiltinMediaLibraryTemplateBuilderAttribute>().ToArray()[builderIdx];
    }
}