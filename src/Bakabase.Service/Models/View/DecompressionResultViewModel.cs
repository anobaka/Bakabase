using Bakabase.Service.Models.View.Constants;
using Bootstrap.Components.Doc.Swagger;

namespace Bakabase.Service.Models.View;

[SwaggerCustomModel]
public record DecompressionResultViewModel
{
    public string Key { get; set; } = string.Empty;
    public DecompressionStatus Status { get; set; }
    public int? Percentage { get; set; }
    public string? Message { get; set; }
}
