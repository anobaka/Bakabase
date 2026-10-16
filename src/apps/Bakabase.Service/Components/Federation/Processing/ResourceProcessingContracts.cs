using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Newtonsoft.Json;

namespace Bakabase.Service.Components.Federation.Processing;

public enum ResourceProcessingKind { PlayableFiles, Enhancement }
public enum ResourceProcessingRole { Manager, Owner, Worker }
public enum ResourceProcessingState { Queued, Running, Paused, Cancelled, Completed, Failed, Conflict }

/// <summary>One resource and one enhancer step. The manager orders dependent enhancers and prepares
/// a fresh source snapshot after applying each step; worker-local ids never resolve owner data.</summary>
public sealed record ResourceProcessingInput(string JobId, string OwnerId, string OwnerEpoch, int ResourceId,
    long? ExpectedResourceRevision, string RuleHash, ResourceProcessingKind Kind,
    ResourceProfilePlayableFileOptions? PlayableFiles = null, EnhancerFullOptions? Enhancer = null);
public sealed record ResourceProcessingPrepared(ResourceProcessingInput Input, string Token,
    long ResourceRevision, string CatalogGeneration, string ResourceJson);
public sealed record ResourceProcessingValue(int Target, string? DynamicTarget, StandardValueType ValueType,
    string? SerializedValue);
public sealed record ResourceProcessingArtifact(string Hash, string FileName, long Length);
public sealed record ResourceProcessingOutput(ResourceProcessingKind Kind, IReadOnlyList<string> PlayableFiles,
    IReadOnlyList<ResourceProcessingValue> Values, IReadOnlyList<ResourceProcessingArtifact> Artifacts,
    long ProcessedCount);
public sealed record ResourceProcessingProgress(string Phase, long ProcessedCount, long? TotalCount = null);
public sealed record ResourceProcessingReceipt(string JobId, long ResourceRevision, string ResultHash);
public sealed record ResourceProcessingArtifactChunk(string JobId, string Token,
    ResourceProcessingArtifact Artifact, long Offset, string Base64);
public sealed record ResourceProcessingJobView(string Id, string JobId, string Role, string OwnerId,
    string OwnerEpoch, int ResourceId, string Kind, string State, string? Phase, long ProcessedCount,
    long? TotalCount, int Attempt, bool PauseRequested, bool CancelRequested, string? ErrorCode, string? Error,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc, long Version, string? CurrentStepPhase = null,
    long? CurrentStepProcessedCount = null, long? CurrentStepTotalCount = null);

/// <summary>Internal disk envelope; HTTP queue callers still send the owner's preparation.
/// Pin the worker's identity at admission, independently from the resource owner's identity.</summary>
internal sealed record ResourceProcessingWorkerEnvelope(ResourceProcessingPrepared Prepared, string WorkerId, string WorkerEpoch)
{
    public static ResourceProcessingWorkerEnvelope Read(string json)
    {
        var saved = ResourceProcessingWire.Deserialize<ResourceProcessingWorkerEnvelope>(json);
        if (saved.Prepared == null || !ResourceProcessingWire.IsIdentity(saved.WorkerId) ||
            !ResourceProcessingWire.IsIdentity(saved.WorkerEpoch))
            throw new ResourceProcessingException("EpochChanged", "This work has no saved worker identity. Start a new run.");
        return saved;
    }

    public void RequireIdentity(Bakabase.Modules.Federation.Identity.NodeIdentity identity)
    {
        if (WorkerId != identity.NodeId || WorkerEpoch != identity.LibraryEpoch)
            throw new ResourceProcessingException("EpochChanged", "The worker's data directory was replaced. Start a new run with its current library.");
    }
}

public static class ResourceProcessingWire
{
    public const int MaxJsonBytes = 768 * 1024; // Every signed request stays below the 1 MiB body hash limit.
    public const int MaxFiles = 100_000;
    public const int MaxArtifacts = 128;
    public const int MaxArtifactBytes = 16 * 1024 * 1024;
    public const int MaxTotalArtifactBytes = 64 * 1024 * 1024;
    public const int ArtifactChunkBytes = 128 * 1024;
    public const string ArtifactPrefix = "bakabase-processing-artifact:";
    public const string ResourceFilePrefix = "bakabase-processing-resource:";
    // Independent of process-global UI JSON defaults, so hashes survive restart and culture changes.
    private static readonly JsonSerializerSettings Settings = new() {TypeNameHandling = TypeNameHandling.None,
        NullValueHandling = NullValueHandling.Include, MaxDepth = 64};
    public static string Serialize(object value)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        JsonSerializer.Create(Settings).Serialize(writer, value);
        var json = writer.ToString();
        if (Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
            throw new ResourceProcessingException("BudgetExceeded", "The processing payload exceeds its bounded transfer budget.");
        return json;
    }
    public static T Deserialize<T>(string json)
    {
        if (json == null || Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
            throw new ResourceProcessingException("BudgetExceeded", "The processing payload exceeds its bounded transfer budget.");
        using var text = new StringReader(json);
        using var reader = new JsonTextReader(text) {MaxDepth = 64};
        return JsonSerializer.Create(Settings).Deserialize<T>(reader)
            ?? throw new ResourceProcessingException("InvalidPayload", "The processing payload is empty.");
    }
    public static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(value)))).ToLowerInvariant();
    public static void ValidateInput(ResourceProcessingInput input)
    {
        if (input == null || !Guid.TryParseExact(input.JobId, "N", out _) || input.ResourceId <= 0 ||
            input.ExpectedResourceRevision < 0 || !IsIdentity(input.OwnerId) || !IsIdentity(input.OwnerEpoch) ||
            input.RuleHash is not {Length: 64} || !IsHex(input.RuleHash) || !Enum.IsDefined(input.Kind) ||
            (input.Kind == ResourceProcessingKind.PlayableFiles ? input.PlayableFiles == null || input.Enhancer != null :
                input.Enhancer == null || input.PlayableFiles != null || input.Enhancer.EnhancerId <= 0))
            throw new ResourceProcessingException("InvalidInput", "The processing input is invalid.");
        _ = Serialize(input);
    }
    public static bool IsIdentity(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        System.Linq.Enumerable.All(value, c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    public static bool IsHex(string? value) => value != null && System.Linq.Enumerable.All(value, char.IsAsciiHexDigit);
}
public sealed class ResourceProcessingException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
