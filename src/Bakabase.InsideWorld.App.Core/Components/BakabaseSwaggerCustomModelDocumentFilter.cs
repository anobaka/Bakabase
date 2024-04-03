using System.Linq;
using System.Reflection;
using Bakabase.Abstractions.Models.Domain;
using Bootstrap.Components.Doc.Swagger;

namespace Bakabase.InsideWorld.App.Core.Components
{
    public class BakabaseSwaggerCustomModelDocumentFilter : SwaggerCustomModelDocumentFilter
    {
        protected override Assembly[] Assemblies { get; } =
            new[] {typeof(Resource)}.Select(a => Assembly.GetAssembly(a)!).ToArray();
    }
}
