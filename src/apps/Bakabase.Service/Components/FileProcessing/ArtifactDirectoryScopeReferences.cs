using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>A validated allocation's storage root configures future siblings; its concrete
/// directory remains a reference. Unknown fields and look-alike source objects stay protected.</summary>
internal static class ArtifactDirectoryScopeReferences
{
    private static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web)
        {UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow};

    internal static JsonElement? AllocationRoot(string name, JsonElement value, JsonElement parent)
    {
        if (name is not ("downloadDirectoryScope" or "processingDirectoryScope" or "workspaceDirectoryScope" or "allocation") ||
            value.ValueKind != JsonValueKind.Object) return null;
        try
        {
            var fields = value.EnumerateObject().ToArray();
            string[] allowed = ["ownerKind", "ownerId", "role", "storageRoot", "directory"];
            if (fields.Length != allowed.Length || fields.Any(p => p.Value.ValueKind != JsonValueKind.String) ||
                !fields.Select(p => p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(allowed)) return null;
            var scope = JsonSerializer.Deserialize<ArtifactDirectoryScope>(value.GetRawText(), Strict)!;
            FileProcessingWorkspaceResolver.ValidateDirectoryScope(scope);
            if (name == "allocation")
            {
                string[] bindingFields = ["directory", "ownership", "evidence", "allocation"];
                if (parent.ValueKind != JsonValueKind.Object) return null;
                var bindingProperties = parent.EnumerateObject().ToArray();
                var hasBinding = parent.TryGetProperty("bindingId", out var binding);
                if (hasBinding && binding.ValueKind != JsonValueKind.Null &&
                    (binding.ValueKind != JsonValueKind.String || !Guid.TryParseExact(binding.GetString(), "N", out _))) return null;
                if (bindingProperties.Length != bindingFields.Length + (hasBinding ? 1 : 0) ||
                    !bindingProperties.Where(p => p.Name != "bindingId").Select(p => p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(bindingFields) ||
                    parent.GetProperty("ownership").GetString() != "exclusive" ||
                    parent.GetProperty("evidence").GetString() != "taskAllocation" ||
                    parent.GetProperty("directory").GetString() != scope.Directory) return null;
            }
            return value.GetProperty("storageRoot");
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        { return null; }
    }
}
