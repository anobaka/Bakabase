using Bootstrap.Components.Doc.Swagger;

namespace Bakabase.Service.Models.View.Constants;

[SwaggerCustomModel]
public enum DecompressionStatus
{
    Pending = 1,
    Decompressing,
    Success,
    Error
}
