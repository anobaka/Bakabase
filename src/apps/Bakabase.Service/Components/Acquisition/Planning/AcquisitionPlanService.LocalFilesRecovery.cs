using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionPlanService
{
    internal AcquisitionLocalFilesStorage CaptureLocalFilesStorage(AcquisitionTaskDbModel task)
    {
        var plan = Plan(task);
        var old = State(task).StoragePolicy ?? plan.StoragePolicy ?? new ArtifactStoragePolicy();
        var policy = CaptureStorage() with {SourceCleanup = old.SourceCleanup, CleanupIntermediates = old.CleanupIntermediates,
            Materialization = old.Materialization};
        var delivery = DeliveryDirectories.Capture();
        // An explicit local migration must show real configured destinations. Legacy
        // fallback-to-input or fallback-to-frozen-output would make this review misleading.
        if (string.IsNullOrWhiteSpace(policy.ProcessingWorkspaceDirectory) || string.IsNullOrWhiteSpace(delivery.RootDirectory))
            throw new InvalidOperationException("RECOVERY_LOCAL_STORAGE_REQUIRED");
        return new(policy, delivery);
    }

    internal AcquisitionDeliveryLayout LocalFilesLayout(AcquisitionTaskDbModel task, AcquisitionLocalFilesStorage reviewed)
    {
        var plan = Plan(task);
        var old = DeliveryDirectories.Resolve(task.Id, plan.Title, plan.OutputDirectory, plan.Parts, reviewed.Delivery);
        var next = old.TaskDirectory + " [R" + checked(task.PlanRevision + 1).ToString(CultureInfo.InvariantCulture) + "]";
        var layout = old with {TaskDirectory = next, PartDirectories = old.PartDirectories.ToDictionary(p => p.Key,
            p => Path.GetFullPath(Path.Combine(next, Path.GetRelativePath(old.TaskDirectory, p.Value))), StringComparer.Ordinal)};
        AcquisitionDeliveryDirectoryResolver.ValidateLayout(layout, plan.Parts.Select(p => p.Id).ToArray());
        foreach (var path in layout.PartDirectories.Values.Append(next).Append(reviewed.Policy.ProcessingWorkspaceDirectory))
        {
            storage.EnsurePathAllowed(path);
            ArtifactManifestFiles.RejectLinks(path);
        }
        if (File.Exists(next) || Directory.Exists(next)) throw new InvalidOperationException("PLAN_REPLAN_DELIVERY_CONFLICT");
        return layout;
    }
}
