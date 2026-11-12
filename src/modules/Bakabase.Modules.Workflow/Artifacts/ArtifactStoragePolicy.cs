using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Bakabase.Modules.Workflow.Artifacts;

/// <summary>Frozen placement and retention preferences. Never part of a content/operation identity.</summary>
public sealed record ArtifactStoragePolicy
{
    public int Version { get; init; } = 1;
    public string DownloadDirectoryTemplate { get; init; } = "{Title} [D{OperationId}]";
    public string ProcessingDirectoryTemplate { get; init; } = "{Title}/operation-{OperationId}/attempt-{Attempt}";
    public string ProcessingWorkspaceDirectory { get; init; } = "";
    public bool CleanupIntermediates { get; init; } = true;
    /// <summary>auto reuses safe immutable results and attempts independent CoW; copy always materializes independent bytes.</summary>
    public string Materialization { get; init; } = "auto";
    /// <summary>Only after verified delivery and release of all consumers: keep, quarantine or delete.</summary>
    public string SourceCleanup { get; init; } = "keep";
}

/// <summary>Missing members inherit their parent's frozen values; an empty workspace root inherits input storage.</summary>
public sealed record ArtifactStoragePolicyOverride
{
    public string? DownloadDirectoryTemplate { get; init; }
    public string? ProcessingDirectoryTemplate { get; init; }
    public string? ProcessingWorkspaceDirectory { get; init; }
    public bool? CleanupIntermediates { get; init; }
    public string? Materialization { get; init; }
    public string? SourceCleanup { get; init; }
}

public static class ArtifactStoragePolicies
{
    public static ArtifactStoragePolicy Resolve(ArtifactStoragePolicy parent, ArtifactStoragePolicyOverride? value)
    {
        var result = value == null ? parent : parent with
        {
            DownloadDirectoryTemplate = value.DownloadDirectoryTemplate ?? parent.DownloadDirectoryTemplate,
            ProcessingDirectoryTemplate = value.ProcessingDirectoryTemplate ?? parent.ProcessingDirectoryTemplate,
            ProcessingWorkspaceDirectory = value.ProcessingWorkspaceDirectory ?? parent.ProcessingWorkspaceDirectory,
            CleanupIntermediates = value.CleanupIntermediates ?? parent.CleanupIntermediates,
            Materialization = value.Materialization ?? parent.Materialization,
            SourceCleanup = value.SourceCleanup ?? parent.SourceCleanup
        };
        Validate(result);
        return result;
    }

    public static void Validate(ArtifactStoragePolicy value)
    {
        if (value.Version != 1 || value.Materialization is not ("auto" or "copy") || value.SourceCleanup is not ("keep" or "quarantine" or "delete") ||
            value.ProcessingWorkspaceDirectory == null || value.ProcessingWorkspaceDirectory.Length > 4096 ||
            value.ProcessingWorkspaceDirectory.Any(char.IsControl) ||
            value.ProcessingWorkspaceDirectory.Length > 0 && !Path.IsPathFullyQualified(value.ProcessingWorkspaceDirectory))
            throw new ArgumentException("STORAGE_POLICY_INVALID");
        ValidateTemplate(value.DownloadDirectoryTemplate, ["Title", "OperationId", "Provider"], ["OperationId"]);
        ValidateTemplate(value.ProcessingDirectoryTemplate, ["Title", "OperationId", "Kind", "Attempt"], ["OperationId", "Attempt"]);
    }

    private static void ValidateTemplate(string template, string[] allowed, string[] required)
    {
        if (string.IsNullOrWhiteSpace(template) || template.Length > 240 || template.Contains('\\') ||
            template.Any(char.IsControl) || template.Split('/').Length > 5 ||
            required.Any(name => !template.Contains("{" + name + "}", StringComparison.Ordinal)))
            throw new ArgumentException("STORAGE_TEMPLATE_INVALID");
        var maxRendered = Regex.Replace(template, @"\{([^{}]+)\}", m => new string('x', m.Groups[1].Value switch
        {"OperationId" => 64, "Attempt" => 10, _ => 81}));
        if (maxRendered.Split('/').Any(s => Encoding.UTF8.GetByteCount(s) > 240)) throw new ArgumentException("STORAGE_FOLDER_NAME_INVALID");
        var rest = Regex.Replace(template, @"\{([^{}]+)\}", m => allowed.Contains(m.Groups[1].Value, StringComparer.Ordinal)
            ? "value" : throw new ArgumentException("STORAGE_TEMPLATE_VARIABLE_INVALID"));
        if (rest.Contains('{') || rest.Contains('}') || rest.Split('/').Any(s => s is "" or "." or ".." ||
                s.EndsWith('.') || s.EndsWith(' ') || s.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0 || Reserved(s)))
            throw new ArgumentException("STORAGE_TEMPLATE_INVALID");
    }

    public static string RenderDownload(ArtifactStoragePolicy policy, string? title, string operationId, string provider)
    {
        Validate(policy); ValidateIdentity(operationId);
        return Render(policy.DownloadDirectoryTemplate, new() {{"Title", SafeName(title)}, {"OperationId", operationId}, {"Provider", SafeName(provider)}});
    }

    public static string RenderProcessing(ArtifactStoragePolicy policy, string? title, string operationId, string kind, int attempt)
    {
        Validate(policy); ValidateIdentity(operationId);
        if (attempt < 1 || string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("STORAGE_IDENTITY_INVALID");
        return Render(policy.ProcessingDirectoryTemplate, new() {{"Title", SafeName(title)}, {"OperationId", operationId},
            {"Kind", SafeName(kind)}, {"Attempt", attempt.ToString(CultureInfo.InvariantCulture)}});
    }

    private static void ValidateIdentity(string value)
    {
        if (value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c))) throw new ArgumentException("STORAGE_IDENTITY_INVALID");
    }
    private static string Render(string template, Dictionary<string, string> values)
    {
        var rendered = Regex.Replace(template, @"\{([^{}]+)\}", m => values[m.Groups[1].Value]);
        if (rendered.Split('/').Any(s => s is "" or "." or ".." || Reserved(s) || Encoding.UTF8.GetByteCount(s) > 240))
            throw new ArgumentException("STORAGE_FOLDER_NAME_INVALID");
        return rendered.Replace('/', Path.DirectorySeparatorChar);
    }
    public static string SafeName(string? value)
    {
        var text = new StringBuilder(); var bytes = 0;
        foreach (var rune in (value ?? "Untitled").Normalize(NormalizationForm.FormC).EnumerateRunes())
        {
            var next = Rune.IsControl(rune) || "<>:\"/\\|?*".Contains(rune.ToString(), StringComparison.Ordinal) ? "_" : rune.ToString();
            var size = Encoding.UTF8.GetByteCount(next); if (bytes + size > 80) break;
            text.Append(next); bytes += size;
        }
        var name = text.ToString().Trim().TrimEnd('.');
        if (name is "" or "." or "..") name = "Untitled";
        return Reserved(name) ? "_" + name : name;
    }
    private static bool Reserved(string segment) => Regex.IsMatch(segment.Split('.')[0],
        "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
