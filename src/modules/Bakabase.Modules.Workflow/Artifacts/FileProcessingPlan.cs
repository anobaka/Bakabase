using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Workflow.Artifacts;

/// <summary>Source-independent, ordered instructions for processing an exact file manifest.</summary>
public record FileProcessingPlan
{
    public string Requirement { get; init; } = "unknown";
    public List<FileProcessingStep> Steps { get; init; } = [];
    public List<string> Evidence { get; init; } = [];
}

public record FileProcessingStep
{
    /// <summary>Placement and retention override; excluded from processing identities.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public ArtifactStoragePolicyOverride? StoragePolicy { get; init; }
    public string Id { get; init; } = "";
    public string Op { get; init; } = "";
    public string Input { get; init; } = "download";
    /// <summary>When present, the complete set of input manifests, overriding the legacy Input reference.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public List<string>? Inputs { get; init; }

    public IReadOnlyList<string> GetInputReferences() => Inputs ?? [Input];
    public string? Selector { get; init; }
    public string? Extension { get; init; }
    public string? Password { get; init; }
    /// <summary>Explicit container opening, never an automatic discovery fallback.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public ArchiveOpenMode? ArchiveOpenMode { get; init; }
    /// <summary>Exact parser member name. Selecting a subset never authorizes retiring its source container.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public string? ArchiveMemberSelector { get; init; }
    /// <summary>Fallback for ZIP entry names without Unicode metadata; applies only to ZIP groups in this step.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public ZipFileNameCodePage? ZipFileNameCodePage { get; init; }
    /// <summary>Explicit encoding of the known ZIP password; never inferred from filename encoding.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public ZipPasswordCodePage? ZipPasswordCodePage { get; init; }
    /// <summary>Optional override of the processing node's logical extraction layout.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public string? ExtractionLayout { get; init; }
    /// <summary>renameFile: a file name only, retained in the input file's relative directory.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetName { get; init; }
    /// <summary>moveFile: a relative output directory; the input file name is preserved.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetDirectory { get; init; }
    /// <summary>rebaseDirectory: exact relative directory prefix to replace, preserving its descendant tree.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public string? SourceDirectory { get; init; }
    /// <summary>renameLiteral: case-sensitive literal text within a file name, never a directory or regular expression.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public string? FindText { get; init; }
    /// <summary>renameLiteral: replace every occurrence; an explicitly empty string removes the matched text.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public string? ReplaceText { get; init; }
}

public static class FileExtractionLayouts
{
    public const string CurrentDirectory = "currentDirectory";
    public const string ArchiveDirectory = "archiveDirectory";
    public static bool Valid(string? value) => value is null or CurrentDirectory or ArchiveDirectory;
    public static FileProcessingPlan Apply(FileProcessingPlan plan, string? fallback)
    {
        if (!Valid(fallback)) throw new InvalidOperationException("Choose a valid archive extraction layout.");
        return fallback == null ? plan : plan with {Steps = plan.Steps.Select(s => s.Op == "extractArchive" && s.ExtractionLayout == null
            ? s with {ExtractionLayout = fallback} : s).ToList()};
    }
}

public static class FileProcessingPlanValidator
{
    public static FileProcessingPlan? Validate(FileProcessingPlan? plan)
    {
        ValidateCore(plan, allowMissingRequiredParameters: false);
        return plan;
    }

    /// <summary>
    /// Validates an analysis candidate's structure and every supplied operation parameter, while
    /// reporting absent required parameters. A candidate returning true is not executable and
    /// must not be passed to a processor. Normal execution continues to use strict Validate.
    /// </summary>
    public static bool HasMissingRequiredParameters(FileProcessingPlan? plan) =>
        ValidateCore(plan, allowMissingRequiredParameters: true);

    private static bool ValidateCore(FileProcessingPlan? plan, bool allowMissingRequiredParameters)
    {
        if (plan == null) return false;
        var missing = false;
        bool Missing(string? value, bool allowEmpty = false)
        {
            if (!allowMissingRequiredParameters || value != null && (allowEmpty || value.Length > 0)) return false;
            missing = true;
            return true;
        }
        if (plan.Requirement is not ("required" or "notRequired" or "unknown") || plan.Steps == null ||
            plan.Steps.Count > 24 || plan.Evidence == null || plan.Evidence.Count > 32 ||
            plan.Evidence.Any(e => e == null || e.Length > 1000))
            throw new InvalidOperationException("The file-processing plan has an invalid requirement or too many steps.");
        if (plan.Requirement == "notRequired" && plan.Steps.Count > 0)
            throw new InvalidOperationException("A plan marked as not requiring file processing cannot contain processing steps.");
        var ids = new HashSet<string>(StringComparer.Ordinal) {"download"};
        foreach (var step in plan.Steps)
        {
            if (step == null || string.IsNullOrWhiteSpace(step.Id) || step.Id.Length > 80 ||
                step.GetInputReferences().Count is 0 or > 24 ||
                step.GetInputReferences().Any(input => input == null || !ids.Contains(input)) ||
                step.GetInputReferences().Distinct(StringComparer.Ordinal).Count() != step.GetInputReferences().Count ||
                !ids.Add(step.Id) ||
                step.Op is not ("renameExtension" or "renameFile" or "renameLiteral" or "moveFile" or "rebaseDirectory" or "extractArchive"))
                throw new InvalidOperationException("The file-processing plan has an unknown operation, duplicate id or invalid input reference.");
            if (step.StoragePolicy != null) ArtifactStoragePolicies.Resolve(new(), step.StoragePolicy);
            if (!FileExtractionLayouts.Valid(step.ExtractionLayout) || step.ExtractionLayout != null && step.Op != "extractArchive")
                throw new InvalidOperationException("Only archive extraction accepts a supported extraction layout.");
            if (step.ZipFileNameCodePage is { } codePage && (step.Op != "extractArchive" || !Enum.IsDefined(codePage)))
                throw new InvalidOperationException("Only archive extraction accepts a supported ZIP file-name code page.");
            if (step.ZipPasswordCodePage is { } passwordCodePage && (step.Op != "extractArchive" || !Enum.IsDefined(passwordCodePage)))
                throw new InvalidOperationException("Only archive extraction accepts a supported ZIP password code page.");
            if (step.ArchiveOpenMode is { } mode && (step.Op != "extractArchive" || !Enum.IsDefined(mode) ||
                string.IsNullOrWhiteSpace(step.Selector) || step.Selector.IndexOfAny(['*', '?']) >= 0 ||
                step.ZipFileNameCodePage != null || step.ZipPasswordCodePage != null))
                throw new InvalidOperationException("An explicit archive opening mode requires an exact extraction selector. Configure ZIP filename encoding in a separate inner extraction step.");
            if (step.ArchiveMemberSelector != null && (step.ArchiveOpenMode != ArchiveOpenMode.SevenZipParser ||
                !IsPortableName(step.ArchiveMemberSelector)))
                throw new InvalidOperationException("An archive member selector requires parser mode and one exact member name without a directory or wildcard.");
            if (step.Selector is { } selector && (selector.Length > 512 || selector.Contains('\0') ||
                selector.Contains("..") || selector.StartsWith('/') || selector.StartsWith('\\') || selector.Contains(':')))
                throw new InvalidOperationException("The file-processing plan contains an unsafe file selector.");
            if (step.Op == "renameExtension" && !Missing(step.Extension) && (step.Extension == null ||
                !Regex.IsMatch(step.Extension, @"^\.[a-zA-Z0-9]{1,16}(?:\.[a-zA-Z0-9]{1,16}){0,2}$")))
                throw new InvalidOperationException("A rename-extension step needs a valid extension, such as .7z.");
            if (step.Op == "renameFile" && !Missing(step.TargetName) && !IsPortableName(step.TargetName))
                throw new InvalidOperationException("A rename-file step needs a safe file name without a directory.");
            if (step.Op == "renameLiteral" && ((!Missing(step.FindText) && !IsLiteralFragment(step.FindText, allowEmpty: false)) ||
                (!Missing(step.ReplaceText, allowEmpty: true) && !IsLiteralFragment(step.ReplaceText, allowEmpty: true))))
                throw new InvalidOperationException("A literal rename needs non-empty find text and an explicit replacement without path or wildcard characters.");
            if (step.Op == "moveFile" && !Missing(step.TargetDirectory) && !IsRelativeDirectory(step.TargetDirectory))
                throw new InvalidOperationException("A move-file step needs a relative output directory without parent traversal.");
            if (step.Op == "rebaseDirectory")
            {
                if ((!Missing(step.SourceDirectory) && (step.SourceDirectory == "." || !IsRelativeDirectory(step.SourceDirectory))) ||
                    (!Missing(step.TargetDirectory, allowEmpty: true) && step.TargetDirectory != "" && !IsRelativeDirectory(step.TargetDirectory)) ||
                    !string.IsNullOrEmpty(step.Selector))
                    throw new InvalidOperationException("A directory rebase needs an exact relative source directory and a safe output directory, without a file selector.");
            }
            else if (step.SourceDirectory != null)
                throw new InvalidOperationException("Only a directory-rebase step accepts a source directory.");
            if (step.Password?.Length > 2048)
                throw new InvalidOperationException("An archive password in the file-processing plan is too long.");
        }
        return missing;
    }

    private static bool IsLiteralFragment(string? value, bool allowEmpty) => value != null &&
        value.Length <= 128 && (allowEmpty || value.Length > 0) &&
        !value.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c));

    public static bool IsPortableName(string? name) => !string.IsNullOrWhiteSpace(name) &&
        name.Length <= 255 && name is not ("." or "..") &&
        !name.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c)) &&
        !name.EndsWith('.') && !name.EndsWith(' ') &&
        !Regex.IsMatch(name, @"^(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase);

    private static bool IsRelativeDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || directory.Length > 512) return false;
        if (directory == ".") return true;
        var parts = directory.Replace('\\', '/').Split('/');
        return parts.All(IsPortableName);
    }
}
