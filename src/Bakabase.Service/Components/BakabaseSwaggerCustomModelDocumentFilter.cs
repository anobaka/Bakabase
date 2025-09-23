using System.Linq;
using System.Reflection;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Service.Models.View;
using Bootstrap.Components.Doc.Swagger;

namespace Bakabase.Service.Components
{
    public class BakabaseSwaggerCustomModelDocumentFilter : SwaggerCustomModelDocumentFilter
    {
        protected override Assembly[] Assemblies { get; } =
            new[] {typeof(Resource), typeof(CompressedFileDetectionResultViewModel)}.Select(a => Assembly.GetAssembly(a)!).ToArray();
    }
}
