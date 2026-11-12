using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.CloudDownloads;

/// <summary>Node-owned resolution of invocation defaults and explicit per-target configuration.</summary>
public static class CloudStoragePolicyBinding
{
    public static void Validate(ArtifactStoragePolicy? policy,
        IReadOnlyDictionary<string, ArtifactStoragePolicyOverride>? overrides)
    {
        if (policy != null) ArtifactStoragePolicies.Validate(policy);
        if (overrides == null) return;
        if (policy == null || overrides.Count > 500 || overrides.Any(e => string.IsNullOrWhiteSpace(e.Key) ||
                e.Key.Length > 128 || e.Value == null)) throw new ArgumentException("STORAGE_POLICY_INVALID");
        foreach (var value in overrides.Values) _ = ArtifactStoragePolicies.Resolve(policy, value);
    }

    public static List<CloudDownloadTarget> Apply(IReadOnlyList<CloudDownloadTarget> targets,
        ArtifactStoragePolicy? policy, IReadOnlyDictionary<string, ArtifactStoragePolicyOverride>? overrides)
    {
        Validate(policy, overrides);
        if (policy == null) return targets.ToList();
        return targets.Select(target => target with
            {StoragePolicy = ArtifactStoragePolicies.Resolve(policy, overrides?.GetValueOrDefault(target.Id))}).ToList();
    }
}
