using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Allocates new task storage and projects old recorded locations. Neither operation
/// enumerates payloads, moves files, modifies history, or infers ownership from parent names.</summary>
public static class AcquisitionStageDirectoryResolver
{
    public static AcquisitionStageDirectories Allocate(int taskId, AcquisitionPlan plan,
        AcquisitionDeliveryDirectoryResolver deliveries)
    {
        if (taskId <= 0 || !Path.IsPathFullyQualified(plan.OutputDirectory))
            throw new ArgumentException("A task download root is required before allocating its directories.");
        var downloadRoot = Path.GetFullPath(plan.OutputDirectory);
        var processingRoot = string.IsNullOrWhiteSpace(plan.StoragePolicy?.ProcessingWorkspaceDirectory)
            ? downloadRoot : Path.GetFullPath(plan.StoragePolicy.ProcessingWorkspaceDirectory);
        var settings = plan.DeliveryLayout?.Settings ?? new AcquisitionDeliverySettings();
        var downloadLayout = deliveries.Resolve(taskId, plan.Title, downloadRoot, plan.Parts,
            settings with {RootDirectory = downloadRoot});
        var processingLayout = deliveries.Resolve(taskId, plan.Title, downloadRoot, plan.Parts,
            settings with {RootDirectory = processingRoot});
        var delivery = plan.DeliveryLayout ?? deliveries.Resolve(taskId, plan.Title, downloadRoot, plan.Parts, settings);
        // Users may choose the same volume/root for all stages. Keep the stages disjoint
        // without requiring those not-yet-created stage directories to exist at plan time.
        if (Same(downloadRoot, processingRoot) || Same(downloadRoot, delivery.RootDirectory))
            downloadLayout = deliveries.Resolve(taskId, plan.Title, downloadRoot, plan.Parts,
                settings with {RootDirectory = Path.Combine(downloadRoot, "Downloads")});
        if (Same(processingRoot, downloadRoot) || Same(processingRoot, delivery.RootDirectory))
            processingLayout = deliveries.Resolve(taskId, plan.Title, downloadRoot, plan.Parts,
                settings with {RootDirectory = Path.Combine(processingRoot, "Processing")});
        var parts = plan.Parts.ToDictionary(p => p.Id, p => new AcquisitionPartDirectories
        {
            Download = Allocated("download", downloadRoot, downloadLayout.PartDirectories[p.Id], p.Id),
            Processing = Processing(p),
            Delivery = new(delivery.PartDirectories[p.Id], "exclusive", "taskLayout") {BindingId = Guid.NewGuid().ToString("N")}
        }, StringComparer.Ordinal);
        var value = new AcquisitionStageDirectories {TaskId = taskId, Parts = parts};
        Validate(value, plan);
        return value;

        AcquisitionStageDirectory Allocated(string role, string root, string path, string partId) =>
            new(path, "exclusive", "taskAllocation")
            {BindingId = Guid.NewGuid().ToString("N"),
                Allocation = new("acquisition", taskId.ToString(CultureInfo.InvariantCulture), role + "-" + partId, root, path)};
        AcquisitionStageDirectory Processing(AcquisitionPlanPart part)
        {
            var configured = ArtifactStoragePolicies.Resolve(plan.StoragePolicy ?? new(), part.StoragePolicy).ProcessingWorkspaceDirectory;
            var root = string.IsNullOrWhiteSpace(configured) ? downloadRoot : Path.GetFullPath(configured);
            var mapped = Same(root, processingRoot) ? processingLayout : deliveries.Resolve(taskId, plan.Title, downloadRoot, plan.Parts,
                settings with {RootDirectory = Same(root, downloadRoot) || Same(root, delivery.RootDirectory)
                    ? Path.Combine(root, "Processing") : root});
            return Allocated("processing", root, mapped.PartDirectories[part.Id], part.Id);
        }
    }

    /// <summary>A local-only revision resolves only its new processing allocation. Its saved
    /// download root is history and may belong to a disconnected or no-longer-allowed volume.</summary>
    public static IReadOnlyDictionary<string, AcquisitionStageDirectory> AllocateProcessingOnly(int taskId,
        AcquisitionPlan plan, AcquisitionDeliveryDirectoryResolver deliveries)
    {
        if (taskId <= 0) throw new ArgumentException("A task identity is required before allocating its directories.");
        var settings = plan.DeliveryLayout?.Settings ?? new AcquisitionDeliverySettings();
        return plan.Parts.ToDictionary(part => part.Id, part =>
        {
            var configured = ArtifactStoragePolicies.Resolve(plan.StoragePolicy ?? new(), part.StoragePolicy).ProcessingWorkspaceDirectory;
            var root = string.IsNullOrWhiteSpace(configured) ? plan.OutputDirectory : configured;
            if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("A local processing root is required.");
            root = Path.GetFullPath(root);
            var allocationRoot = Same(root, plan.OutputDirectory) || Same(root, plan.DeliveryLayout?.RootDirectory ?? "")
                ? Path.Combine(root, "Processing") : root;
            // Supplying the selected processing root explicitly prevents the delivery
            // resolver from validating or falling back to a historical download location.
            var layout = deliveries.Resolve(taskId, plan.Title, null, plan.Parts, settings with {RootDirectory = allocationRoot});
            var scope = new ArtifactDirectoryScope("acquisition", taskId.ToString(CultureInfo.InvariantCulture),
                "processing-" + part.Id, root, layout.PartDirectories[part.Id]);
            FileProcessingWorkspaceResolver.ValidateDirectoryScope(scope);
            return new AcquisitionStageDirectory(scope.Directory, "exclusive", "taskAllocation")
                {BindingId = Guid.NewGuid().ToString("N"), Allocation = scope};
        }, StringComparer.Ordinal);
    }

    public static void Validate(AcquisitionStageDirectories value, AcquisitionPlan plan)
    {
        if (value.Version != 1 || value.TaskId <= 0 || value.Parts == null ||
            !value.Parts.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(plan.Parts.Select(p => p.Id)))
            throw new ArgumentException("The task stage directory mapping is invalid.");
        foreach (var (partId, part) in value.Parts)
        {
            if (part == null) throw new ArgumentException("The task stage directory mapping is invalid.");
            foreach (var (role, entry) in new[] {("download", part.Download), ("processing", part.Processing), ("delivery", part.Delivery)})
            {
                if (entry == null) continue;
                if (!Path.IsPathFullyQualified(entry.Directory) || entry.Directory.Any(char.IsControl) ||
                    entry.Ownership is not ("exclusive" or "borrowed") || string.IsNullOrWhiteSpace(entry.Evidence) ||
                    entry.BindingId != null && !Guid.TryParseExact(entry.BindingId, "N", out _))
                    throw new ArgumentException("The task stage directory mapping is invalid.");
                if (entry.Allocation is not { } allocation) continue;
                FileProcessingWorkspaceResolver.ValidateDirectoryScope(allocation);
                if (entry.Ownership != "exclusive" || allocation.OwnerKind != "acquisition" ||
                    allocation.OwnerId != value.TaskId.ToString(CultureInfo.InvariantCulture) || allocation.Role != role + "-" + partId ||
                    !Same(entry.Directory, allocation.Directory))
                    throw new ArgumentException("The task stage allocation belongs to another task.");
            }
        }
        var allocated = value.Parts.Values.SelectMany(p => new[] {p.Download, p.Processing, p.Delivery})
            .Where(p => p?.Ownership == "exclusive").Select(p => p!.Directory).ToArray();
        for (var i = 0; i < allocated.Length; i++)
        for (var j = i + 1; j < allocated.Length; j++)
            if (Contains(allocated[i], allocated[j]) || Contains(allocated[j], allocated[i]))
                throw new ArgumentException("Task stage directories must not overlap.");
    }

    /// <summary>Read-only binding for historical tasks. Current target records take precedence
    /// over planned locations. Missing records remain missing instead of guessing a directory.</summary>
    public static AcquisitionStageDirectories FromRecorded(int taskId, AcquisitionPlan plan, DownloadWorkset? workset)
    {
        var parts = new Dictionary<string, AcquisitionPartDirectories>(StringComparer.Ordinal);
        foreach (var part in plan.Parts)
        {
            var saved = plan.StageDirectories?.Parts.GetValueOrDefault(part.Id);
            var target = workset?.Targets.SingleOrDefault(t => t.Id == part.Id);
            var plannedDelivery = plan.DeliveryLayout?.PartDirectories.GetValueOrDefault(part.Id);
            parts[part.Id] = new()
            {
                Download = saved?.Download?.BindingId != null ? saved.Download : Recorded(target?.DownloadDirectory, "downloadRecord", saved?.Download) ?? saved?.Download,
                Processing = saved?.Processing?.BindingId != null ? saved.Processing : Recorded(target?.ProcessedDirectory, "processingRecord", saved?.Processing) ?? saved?.Processing,
                Delivery = saved?.Delivery?.BindingId != null ? saved.Delivery : Recorded(target?.PublishedDirectory, "deliveryRecord", saved?.Delivery) ??
                    (Recorded(plannedDelivery, "plannedDelivery", saved?.Delivery) is { } planned
                        ? saved?.Delivery is {Ownership: "exclusive", Evidence: "taskLayout"}
                            ? planned with {Ownership = "exclusive", Evidence = "taskLayout"} : planned
                        : saved?.Delivery)
            };
        }
        return new() {TaskId = taskId, Parts = parts};
    }

    private static AcquisitionStageDirectory? Recorded(string? path, string evidence, AcquisitionStageDirectory? saved = null) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && !path.Any(char.IsControl)
            ? saved != null && Same(path, saved.Directory) ? saved : new(Path.GetFullPath(path), "borrowed", evidence) : null;
    private static bool Same(string a, string b) => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
    private static bool Contains(string root, string path) => Same(root, path) || Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
