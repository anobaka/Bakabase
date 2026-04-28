using Bootstrap.Components.Doc.Swagger;

namespace Bakabase.Service.Models.View.Constants;

[SwaggerCustomModel]
public enum CompressedFileDetectionResultStatus
{
    Init = 1, 
    Inprogress,
    Complete,
    Error
}