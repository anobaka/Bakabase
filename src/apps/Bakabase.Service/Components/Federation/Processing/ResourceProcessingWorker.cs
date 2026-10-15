using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bootstrap.Extensions;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Enhancer.Components;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Player.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Management;
using Bakabase.Modules.StandardValue;
using Bakabase.Modules.StandardValue.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Bakabase.Service.Components.Federation.Processing;

/// <summary>Computes against a pinned source snapshot. Never looks up source numeric ids in this
/// worker's resource database and never writes to the worker's property/cache tables.</summary>
public sealed class ResourceProcessingWorker(INodeIdentityProvider identity, IManagedServerService managed,
    IUserStoragePolicy storage, ResourceProcessingArtifacts artifacts, IServiceProvider services,
    IEnhancerDescriptors descriptors)
{
    public async Task<ResourceProcessingOutput> ExecuteAsync(ResourceProcessingPrepared prepared,
        Func<ResourceProcessingProgress, CancellationToken, Task> checkpoint, CancellationToken ct)
    {
        ResourceProcessingWire.ValidateInput(prepared.Input);
        _ = ResourceProcessingWire.Serialize(prepared);
        var resource = ResourceProcessingWire.Deserialize<Resource>(prepared.ResourceJson);
        if (resource.Id != prepared.Input.ResourceId)
            throw new ResourceProcessingException("InvalidResource", "The prepared resource id differs from the requested resource.");
        var own = await identity.GetAsync(ct);
        var needsFiles = prepared.Input.Kind == ResourceProcessingKind.PlayableFiles ||
            ((EnhancerId)prepared.Input.Enhancer!.EnhancerId).GetAttribute<EnhancerAttribute>()?.RequiresLocalFiles == true;
        if (needsFiles && !resource.HasLocalPath)
            throw new ResourceProcessingException("NoLocalFiles", "This resource has no source filesystem files to process.");
        string? path = resource.Path;
        if (!needsFiles)
        {
            // The foreign path is metadata here, not a worker filesystem location. Normalize only
            // its separators so default filename keywords retain source semantics across OSes.
            path = resource.Path?.Replace('\\', '/');
        }
        else if (own.NodeId == prepared.Input.OwnerId)
        {
            if (own.LibraryEpoch != prepared.Input.OwnerEpoch)
                throw new ResourceProcessingException("EpochChanged", "The source library has changed.");
            path = resource.Path;
        }
        else
        {
            var source = (await managed.GetAsync(false, ct)).Servers.SingleOrDefault(s => s.ServerId == prepared.Input.OwnerId)
                ?? throw new ResourceProcessingException("SourceNotManaged", "This worker must explicitly manage the source device and configure its path mappings.");
            path = ManagedPathMapper.Map(resource.Path, source.PathMappings).LocalPath
                ?? throw new ResourceProcessingException("PathNotMapped", "Configure a source-to-worker path mapping before running this rule.");
        }
        if (needsFiles)
        {
            storage.EnsurePathAllowed(path!, UserStoragePurpose.UserFiles);
            ResourceProcessingPaths.RejectLinks(path!, path!, resource.IsFile);
        }
        // The immutable wire snapshot is copied; its owner paths are never overwritten in storage.
        resource.Path = path;
        RehydrateProperties(resource);
        await checkpoint(new("Preparing", 0), ct);
        if (prepared.Input.Kind == ResourceProcessingKind.PlayableFiles)
        {
            long visited = 0;
            var found = await PlayableFileDiscovery.DiscoverAsync(path, resource.IsFile, prepared.Input.PlayableFiles,
                ct, async (file, token) =>
                {
                    storage.EnsurePathAllowed(file, UserStoragePurpose.UserFiles);
                    ResourceProcessingPaths.Relative(path, resource.IsFile, file);
                    visited++;
                    if (visited % 64 == 0) await checkpoint(new("DiscoveringFiles", visited), token);
                }, ResourceProcessingWire.MaxFiles);
            var result = new ResourceProcessingOutput(ResourceProcessingKind.PlayableFiles,
                found.Select(f => ResourceProcessingPaths.Relative(path, resource.IsFile, f)).ToArray(), [], [], visited);
            _ = ResourceProcessingWire.Serialize(result);
            await checkpoint(new("FilesDiscovered", visited, visited), ct);
            return result;
        }
        if (needsFiles) await ValidateResourceTree(path!, resource.IsFile, checkpoint, ct);
        var options = prepared.Input.Enhancer!;
        var registered = services.GetServices<IEnhancer>().SingleOrDefault(e => e.Id == options.EnhancerId)
            ?? throw new ResourceProcessingException("EnhancerUnavailable", "The selected enhancer is not installed on this worker.");
        var fileManager = artifacts.CreateWorkerFileManager(prepared, ct);
        var enhancer = (IEnhancer) ActivatorUtilities.CreateInstance(services, registered.GetType(), fileManager);
        var resultValues = await enhancer.CreateEnhancements(resource, options, new EnhancementLogCollector(), ct);
        ct.ThrowIfCancellationRequested();
        if (!string.IsNullOrEmpty(resultValues?.ErrorMessage))
            throw new ResourceProcessingException("EnhancementFailed", resultValues.ErrorMessage);
        var values = resultValues?.Values ?? [];
        await services.GetRequiredService<IEnhancerService>().ProcessComputedValuesAsync(values, options, resource.Id, ct);
        var manifest = new Dictionary<string, ResourceProcessingArtifact>();
        foreach (var value in values)
        {
            ct.ThrowIfCancellationRequested();
            var descriptor = descriptors[options.EnhancerId][value.Target];
            if (descriptor.PropertyType != PropertyType.Attachment || value.Value == null) continue;
            if (value.Value is not IEnumerable<string> paths)
                throw new ResourceProcessingException("InvalidAttachment", "The enhancer returned an invalid attachment value.");
            var references = new List<string>();
            foreach (var file in paths)
            {
                var full = Path.IsPathRooted(file) ? file : Path.Combine(fileManager.BaseDir, file);
                // Owner data references remain relative; copied/generated bytes have content-addressed ids.
                if (Inside(fileManager.BaseDir, full))
                {
                    var artifact = await artifacts.CaptureAsync(prepared, full, fileManager.BaseDir, false, ct);
                    manifest[artifact.Hash] = artifact;
                    if (manifest.Count > ResourceProcessingWire.MaxArtifacts || manifest.Values.Sum(a => a.Length) > ResourceProcessingWire.MaxTotalArtifactBytes)
                        throw new ResourceProcessingException("BudgetExceeded", "Enhancement attachments exceed the job budget.");
                    references.Add(ResourceProcessingWire.ArtifactPrefix + artifact.Hash);
                }
                else
                {
                    storage.EnsurePathAllowed(full, UserStoragePurpose.UserFiles);
                    references.Add(ResourceProcessingWire.ResourceFilePrefix + ResourceProcessingPaths.Relative(path, resource.IsFile, full));
                }
            }
            value.Value = references;
        }
        var output = new ResourceProcessingOutput(ResourceProcessingKind.Enhancement, [], values.Select(v =>
            new ResourceProcessingValue(v.Target, v.DynamicTarget, v.ValueType,
                v.Value?.SerializeAsStandardValue(v.ValueType))).ToArray(), manifest.Values.ToArray(), values.Count);
        _ = ResourceProcessingWire.Serialize(output);
        await checkpoint(new("EnhancementComputed", values.Count, values.Count), ct);
        return output;
    }
    private async Task ValidateResourceTree(string root, bool isFile,
        Func<ResourceProcessingProgress, CancellationToken, Task> checkpoint, CancellationToken ct)
    {
        if (isFile) return;
        var stack = new Stack<IEnumerator<string>>();
        stack.Push(Directory.EnumerateFileSystemEntries(root).GetEnumerator());
        long visited = 0;
        try
        {
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var iterator = stack.Peek();
                if (!iterator.MoveNext()) { iterator.Dispose(); stack.Pop(); continue; }
                var path = iterator.Current;
                storage.EnsurePathAllowed(path, UserStoragePurpose.UserFiles);
                ResourceProcessingPaths.Relative(root, false, path);
                if (++visited > ResourceProcessingWire.MaxFiles || stack.Count > 1024)
                    throw new ResourceProcessingException("BudgetExceeded", "The resource tree exceeds the processing budget.");
                if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                    stack.Push(Directory.EnumerateFileSystemEntries(path).GetEnumerator());
                if (visited % 64 == 0) await checkpoint(new("CheckingFiles", visited), ct);
            }
        }
        finally { foreach (var iterator in stack) iterator.Dispose(); }
    }

    private static bool Inside(string root, string file)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(file));
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar) && !Path.IsPathRooted(relative);
    }
    private static void RehydrateProperties(Resource resource)
    {
        foreach (var property in resource.Properties?.Values.SelectMany(p => p.Values) ?? [])
            foreach (var value in property.Values ?? [])
            {
                value.Value = ConvertValue(value.Value, property.DbValueType);
                value.BizValue = ConvertValue(value.BizValue, property.BizValueType);
                value.AliasAppliedBizValue = ConvertValue(value.AliasAppliedBizValue, property.BizValueType);
            }
        static object? ConvertValue(object? value, StandardValueType type) => value is JToken json
            ? json.ToObject(StandardValueSystem.GetHandler(type).ExpectedType) : value;
    }
}
