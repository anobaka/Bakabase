using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Resolves final delivery once. It never changes download paths, creates folders or marks resources.</summary>
public sealed class AcquisitionDeliveryDirectoryResolver(IUserStoragePolicy storage, IBOptions<AcquisitionOptions>? options = null)
{
    public AcquisitionDeliverySettings Capture(AcquisitionDeliverySettings? input = null)
    {
        var settings = input ?? new AcquisitionDeliverySettings
        {
            RootDirectory = options?.Value.DeliveryRootDirectory ?? "",
            DirectoryTemplate = options?.Value.DeliveryDirectoryTemplate ?? "{Title} [A{TaskId}]",
            PartDirectoryTemplate = options?.Value.DeliveryPartDirectoryTemplate ?? "{PartTitle} [{PartId}]"
        };
        return NormalizeSettings(settings with {ContentLayout = settings.ContentLayout ?? options?.Value.DeliveryContentLayout ?? new()});
    }

    private AcquisitionDeliverySettings NormalizeSettings(AcquisitionDeliverySettings settings)
    {
        ValidateSettings(settings);
        if (settings.RootDirectory.Length == 0) return settings;
        storage.EnsurePathAllowed(settings.RootDirectory);
        return settings with {RootDirectory = Path.GetFullPath(settings.RootDirectory)};
    }

    public AcquisitionDeliveryLayout Resolve(int taskId, string title, string? downloadDirectory,
        IReadOnlyList<AcquisitionPlanPart> parts, AcquisitionDeliverySettings settings,
        AcquisitionDeliveryLayout? previous = null)
    {
        if (taskId <= 0) throw new ArgumentException("DELIVERY_TASK_ID_REQUIRED");
        // A saved plan may predate content projection. Resolving it again must preserve its frozen policy.
        settings = NormalizeSettings(settings);
        if (previous != null && previous.TaskId != taskId) throw new ArgumentException("DELIVERY_TASK_ID_MISMATCH");
        var root = previous?.RootDirectory;
        if (string.IsNullOrEmpty(root)) root = settings.RootDirectory.Length > 0 ? settings.RootDirectory :
            string.IsNullOrWhiteSpace(downloadDirectory) ? "" : Path.Combine(downloadDirectory, "Acquired");
        if (root.Length == 0) return new() {TaskId = taskId, Settings = settings};
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("DELIVERY_ROOT_INVALID");
        root = Path.GetFullPath(root);
        var taskDirectory = !string.IsNullOrEmpty(previous?.TaskDirectory) ? previous.TaskDirectory :
            Join(root, Render(settings.DirectoryTemplate, new() { ["Title"] = SafeName(title),
                ["TaskId"] = taskId.ToString(CultureInfo.InvariantCulture)}));
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in parts)
        {
            if (previous?.PartDirectories.TryGetValue(part.Id, out var saved) == true) paths.Add(part.Id, saved);
            else paths.Add(part.Id, parts.Count == 1 && (previous?.PartDirectories.Count ?? 0) == 0 ? taskDirectory :
                Join(taskDirectory, Render(settings.PartDirectoryTemplate, new() {
                    ["PartTitle"] = SafeName(part.Title ?? title), ["PartId"] = part.Id})));
        }
        var layout = new AcquisitionDeliveryLayout {TaskId = taskId, Settings = settings, RootDirectory = root,
            TaskDirectory = taskDirectory, PartDirectories = paths};
        ValidateLayout(layout, parts.Select(p => p.Id).ToList());
        foreach (var path in paths.Values.Append(root).Append(taskDirectory)) storage.EnsurePathAllowed(path);
        return layout;
    }

    public static void ValidateSettings(AcquisitionDeliverySettings settings)
    {
        if (settings == null || settings.RootDirectory == null || settings.RootDirectory.Length > 4096 ||
            settings.RootDirectory.Length > 0 && (!Path.IsPathFullyQualified(settings.RootDirectory) ||
                settings.RootDirectory.Any(char.IsControl))) throw new ArgumentException("DELIVERY_ROOT_INVALID");
        ValidateTemplate(settings.DirectoryTemplate, "TaskId", ["Title", "TaskId"]);
        ValidateTemplate(settings.PartDirectoryTemplate, "PartId", ["PartTitle", "PartId"]);
        if (settings.ContentLayout is {Version: not 1}) throw new ArgumentException("DELIVERY_CONTENT_LAYOUT_INVALID");
    }

    public static void ValidateLayout(AcquisitionDeliveryLayout layout, IReadOnlyList<string> partIds)
    {
        ValidateSettings(layout.Settings);
        if (layout.Version != 1 || layout.TaskId <= 0 || layout.PartDirectories == null)
            throw new ArgumentException("DELIVERY_LAYOUT_INVALID");
        if (layout.RootDirectory == "" && layout.TaskDirectory == "" && layout.PartDirectories.Count == 0) return;
        if (!Path.IsPathFullyQualified(layout.RootDirectory) || !Path.IsPathFullyQualified(layout.TaskDirectory) ||
            !Descendant(layout.RootDirectory, layout.TaskDirectory) || layout.PartDirectories.Count != partIds.Count ||
            partIds.Any(id => !layout.PartDirectories.ContainsKey(id))) throw new ArgumentException("DELIVERY_LAYOUT_INVALID");
        var paths = layout.PartDirectories.Values.Select(Path.GetFullPath).ToList();
        if (paths.Any(p => !Equal(p, layout.TaskDirectory) && !Descendant(layout.TaskDirectory, p)))
            throw new ArgumentException("DELIVERY_LAYOUT_INVALID");
        for (var i = 0; i < paths.Count; i++)
        for (var j = i + 1; j < paths.Count; j++)
            if (Equal(paths[i], paths[j]) || Descendant(paths[i], paths[j]) || Descendant(paths[j], paths[i]))
                throw new ArgumentException("DELIVERY_DIRECTORY_CONFLICT: Review delivery settings after changing required parts.");
    }

    private static void ValidateTemplate(string template, string identity, string[] allowed)
    {
        if (string.IsNullOrWhiteSpace(template) || template.Length > 240 || template.Contains('\\') ||
            template.StartsWith('/') || template.EndsWith('/') || template.Any(char.IsControl))
            throw new ArgumentException("DELIVERY_TEMPLATE_INVALID");
        var segments = template.Split('/');
        if (segments.Any(s => s is "" or "." or ".." || s.EndsWith(' ') || s.EndsWith('.') ||
                s.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0) ||
            !segments[^1].Contains("{" + identity + "}", StringComparison.Ordinal))
            throw new ArgumentException("DELIVERY_TEMPLATE_ID_REQUIRED: Keep the identity placeholder in the final folder name.");
        var leaf = segments[^1];
        var identityToken = "{" + identity + "}";
        var suffix = identity == "TaskId" ? "[A{TaskId}]" : "[{PartId}]";
        // This exact terminal suffix remains stable across template changes, so IDs cannot be
        // confused with adjacent literal digits or text from a different template version.
        if (!leaf.EndsWith(suffix, StringComparison.Ordinal) ||
            leaf.IndexOf(identityToken, StringComparison.Ordinal) != leaf.LastIndexOf(identityToken, StringComparison.Ordinal))
            throw new ArgumentException("DELIVERY_TEMPLATE_ID_REQUIRED: Keep the fixed identity suffix at the end of the final folder name.");
        var rest = Regex.Replace(template, "\\{([^{}]+)\\}", match => allowed.Contains(match.Groups[1].Value, StringComparer.Ordinal)
            ? "value" : throw new ArgumentException("DELIVERY_TEMPLATE_VARIABLE_INVALID"));
        if (rest.Contains('{') || rest.Contains('}')) throw new ArgumentException("DELIVERY_TEMPLATE_INVALID");
        if (rest.Split('/').Any(IsReserved)) throw new ArgumentException("DELIVERY_TEMPLATE_INVALID");
    }

    private static string Render(string template, Dictionary<string, string> values)
    {
        var rendered = Regex.Replace(template, "\\{([^{}]+)\\}", match => values[match.Groups[1].Value]);
        if (rendered.Split('/').Any(s => s is "" or "." or ".." || IsReserved(s) || Encoding.UTF8.GetByteCount(s) > 240))
            throw new ArgumentException("DELIVERY_FOLDER_NAME_INVALID");
        return rendered;
    }

    private static string SafeName(string value)
    {
        var text = new StringBuilder();
        foreach (var rune in value.Normalize(NormalizationForm.FormC).EnumerateRunes())
        {
            var next = Rune.IsControl(rune) || "<>:\"/\\|?*".Contains(rune.ToString(), StringComparison.Ordinal) ? "_" : rune.ToString();
            if (Encoding.UTF8.GetByteCount(text.ToString()) + Encoding.UTF8.GetByteCount(next) > 80) break;
            text.Append(next);
        }
        var name = text.ToString().Trim().TrimEnd('.');
        if (name is "" or "." or "..") name = "Untitled";
        return IsReserved(name) ? "_" + name : name;
    }
    private static bool IsReserved(string segment) => Regex.IsMatch(segment.Split('.')[0],
        "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static string Join(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        return Descendant(root, path) ? path : throw new ArgumentException("DELIVERY_PATH_OUTSIDE_ROOT");
    }
    private static bool Equal(string a, string b) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
    private static bool Descendant(string root, string path)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!prefix.EndsWith(Path.DirectorySeparatorChar)) prefix += Path.DirectorySeparatorChar;
        return !Equal(root, path) && Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
