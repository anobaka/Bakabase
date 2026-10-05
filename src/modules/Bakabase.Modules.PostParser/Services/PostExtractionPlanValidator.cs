using System.Text.RegularExpressions;
using Bakabase.Modules.PostParser.Models.Domain;

namespace Bakabase.Modules.PostParser.Services;

public static class PostExtractionPlanValidator
{
    public static PostExtractionPlan? Validate(PostExtractionPlan? plan)
    {
        if (plan == null) return null;
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
                !ids.Contains(step.Input) || !ids.Add(step.Id) ||
                step.Op is not ("renameExtension" or "renameFile" or "moveFile" or "extractArchive"))
                throw new InvalidOperationException("The file-processing plan has an unknown operation, duplicate id or invalid input reference.");
            if (step.Selector is { } selector && (selector.Length > 512 || selector.Contains('\0') ||
                selector.Contains("..") || selector.StartsWith('/') || selector.StartsWith('\\') || selector.Contains(':')))
                throw new InvalidOperationException("The file-processing plan contains an unsafe file selector.");
            if (step.Op == "renameExtension" && (step.Extension == null ||
                !Regex.IsMatch(step.Extension, @"^\.[a-zA-Z0-9]{1,16}(?:\.[a-zA-Z0-9]{1,16}){0,2}$")))
                throw new InvalidOperationException("A rename-extension step needs a valid extension, such as .7z.");
            if (step.Op == "renameFile" && !IsPortableName(step.TargetName))
                throw new InvalidOperationException("A rename-file step needs a safe file name without a directory.");
            if (step.Op == "moveFile" && !IsRelativeDirectory(step.TargetDirectory))
                throw new InvalidOperationException("A move-file step needs a relative output directory without parent traversal.");
            if (step.Password?.Length > 2048)
                throw new InvalidOperationException("An archive password in the file-processing plan is too long.");
        }
        return plan;
    }

    private static bool IsPortableName(string? name) => !string.IsNullOrWhiteSpace(name) &&
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
