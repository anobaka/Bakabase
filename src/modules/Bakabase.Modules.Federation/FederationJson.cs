using System.Text.Json;

namespace Bakabase.Modules.Federation;

/// <summary>The federation wire format is independent of the legacy application's date converter.</summary>
public static class FederationJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32
    };
}
