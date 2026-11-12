namespace Bakabase.Modules.Workflow.Artifacts;

/// <summary>Accepts an unambiguous extension with or without its leading dot.</summary>
public static class FileProcessingExtensionNormalizer
{
    public static string Normalize(string? extension)
    {
        if (extension == null || extension.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("A rename-extension step needs a valid extension, such as .7z.");
        // Do not trim, strip file names or repair paths: only the optional separator is
        // presentation. Reuse the strict validator so length, compound-segment and character
        // constraints cannot diverge from those enforced during actual file processing.
        var canonical = !extension.StartsWith('.') ? "." + extension : extension;
        FileProcessingPlanValidator.Validate(new FileProcessingPlan
        {
            Requirement = "required",
            Steps = [new() {Id = "extension", Op = "renameExtension", Extension = canonical}]
        });
        return canonical!;
    }
}
