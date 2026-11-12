using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bootstrap.Components.Configuration.Abstractions;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Only creation or an explicit never-started update captures live defaults.</summary>
public sealed class AcquisitionStoragePolicyDefaults(IBOptions<AcquisitionOptions> options, IUserStoragePolicy storage)
{
    public ArtifactStoragePolicy Capture(ArtifactStoragePolicy? input = null)
    {
        var value = input ?? options.Value.StoragePolicy with
        {ProcessingWorkspaceDirectory = options.Value.ProcessingWorkspaceDirectory ?? options.Value.StoragePolicy.ProcessingWorkspaceDirectory};
        ArtifactStoragePolicies.Validate(value);
        FileProcessingWorkspaceResolver.ValidateConfiguredRoot(value.ProcessingWorkspaceDirectory, storage);
        return value;
    }
}
