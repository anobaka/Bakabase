using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.Federation.Contracts;

namespace Bakabase.Modules.Federation.Queries;

public static class QueryProtocol
{
    // Explicit V1 wire values; a contract test compares them to ResourceSource to catch enum drift.
    public static IReadOnlySet<int> SupportedSourceKinds { get; } = new[] { 1, 2, 3, 4, 5, 7 }.ToFrozenSet();

    public static void RejectUnknown(StrictQueryInput input, string field = "query")
    {
        if (input.AdditionalFields is { Count: > 0 })
            throw Unsupported($"{field}.{input.AdditionalFields.Keys.First()}");
    }

    public static CommonLibraryQuery Normalize(CommonLibraryQuery? query, FederationQueryLimits limits)
    {
        if (query == null) throw Unsupported("query");
        RejectUnknown(query);
        if (query.QueryContractVersion != 1) throw Unsupported("query.queryContractVersion");
        if (query.Sort is not ("NameAsc" or "NameDesc")) throw Unsupported("query.sort");
        if (query.FileAvailability is not (null or "Any" or "HasFile" or "MetadataOnly"))
            throw Unsupported("query.fileAvailability");
        if (query.Text?.Length > limits.MaxTextLength) throw Unsupported("query.text");
        if (query.SourceKinds?.Any(s => !SupportedSourceKinds.Contains(s)) == true) throw Unsupported("query.sourceKinds");
        string? text;
        try { text = query.Text?.Trim().Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { throw Unsupported("query.text"); }
        return query with
        {
            Text = string.IsNullOrEmpty(text) ? null : text,
            FileAvailability = query.FileAvailability ?? "Any",
            SourceKinds = query.SourceKinds?.Distinct().Order().ToArray() ?? [],
            AdditionalFields = null
        };
    }

    public static string Hash(CommonLibraryQuery normalizedQuery) => Convert.ToHexStringLower(
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(normalizedQuery)));

    public static string? NormalizeName(string? text) => string.IsNullOrWhiteSpace(text)
        ? null : text.Normalize(NormalizationForm.FormC);

    public static bool Matches(LocalResourceProjection resource, CommonLibraryQuery query)
    {
        if (query.FileAvailability == "HasFile" && !resource.HasLocalPath ||
            query.FileAvailability == "MetadataOnly" && resource.HasLocalPath) return false;
        if (query.SourceKinds is { Length: > 0 } && !resource.SourceKinds.Any(query.SourceKinds.Contains)) return false;
        return query.Text == null ||
               NormalizeName(resource.EffectiveName)?.Contains(query.Text, StringComparison.OrdinalIgnoreCase) == true ||
               NormalizeName(resource.FileName)?.Contains(query.Text, StringComparison.OrdinalIgnoreCase) == true;
    }

    public static FederatedResourceSummary Project(LocalLibraryCapture capture, LocalResourceProjection resource)
    {
        var title = NormalizeName(resource.EffectiveName) ?? NormalizeName(resource.FileName);
        return new FederatedResourceSummary
        {
            Ref = new(capture.NodeId, capture.LibraryEpoch, resource.ResourceId),
            OwnerLabel = capture.OwnerLabel,
            Title = title,
            DisplayName = title ?? $"#{resource.ResourceId}",
            FileName = resource.FileName,
            // Only protocol kinds go on the wire; peers reject a block carrying any other value.
            SourceKinds = resource.SourceKinds.Where(SupportedSourceKinds.Contains).Distinct().Order().ToArray(),
            FileAvailability = resource.HasLocalPath ? "HasFile" : "MetadataOnly",
            NormalizedSortKey = title
        };
    }

    public static IComparer<FederatedResourceSummary> Comparer(string sort) =>
        System.Collections.Generic.Comparer<FederatedResourceSummary>.Create((left, right) =>
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;
            var l = left.NormalizedSortKey;
            var r = right.NormalizedSortKey;
            var result = l == null ? r == null ? 0 : 1 : r == null ? -1 :
                (sort == "NameDesc" ? -1 : 1) * Math.Sign(StringComparer.Ordinal.Compare(l, r));
            if (result != 0) return result;
            result = StringComparer.Ordinal.Compare(left.Ref.NodeId, right.Ref.NodeId);
            if (result != 0) return result;
            result = StringComparer.Ordinal.Compare(left.Ref.LibraryEpoch, right.Ref.LibraryEpoch);
            return result != 0 ? result : left.Ref.ResourceId.CompareTo(right.Ref.ResourceId);
        });

    public static FederatedResourceSummary Copy(FederatedResourceSummary value) => value with
    {
        SourceKinds = value.SourceKinds.ToArray(), PlaybackCapabilities = value.PlaybackCapabilities.ToArray()
    };

    public static long EstimateBytes(FederatedResourceSummary value) => 384L +
        2L * (value.OwnerLabel.Length + (value.Title?.Length ?? 0) + value.DisplayName.Length +
              (value.FileName?.Length ?? 0) + (value.NormalizedSortKey?.Length ?? 0) +
              (value.CoverAsset?.Length ?? 0) + value.Ref.NodeId.Length + value.Ref.LibraryEpoch.Length +
              value.PlaybackCapabilities.Sum(x => x.Length)) + value.SourceKinds.Length * 4L + value.PlaybackCapabilities.Length * 24L;

    /// <summary>
    /// Heap a snapshot row retains. Unlike the wire estimate above, strings shared with the capture
    /// (node, epoch, owner label) or with the row's title (display name, sort key) count once.
    /// </summary>
    public static long EstimateRetainedBytes(FederatedResourceSummary value) => 256L +
        2L * ((value.Title?.Length ?? 0) +
              (ReferenceEquals(value.DisplayName, value.Title) ? 0 : value.DisplayName.Length) +
              (ReferenceEquals(value.NormalizedSortKey, value.Title) ? 0 : value.NormalizedSortKey?.Length ?? 0) +
              (value.FileName?.Length ?? 0) + (value.CoverAsset?.Length ?? 0) +
              value.PlaybackCapabilities.Sum(x => x.Length)) +
        value.SourceKinds.Length * 4L + value.PlaybackCapabilities.Length * 24L;

    public static FederationQueryException Unsupported(string field) => new("UnsupportedQuery", 422,
        $"The query field '{field}' is not supported.", field: field);
}

internal sealed class QueryTokenCodec
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public string Encode(string id, int position)
    {
        var body = Encoding.UTF8.GetBytes($"{id}:{position}");
        return Base64(body) + "." + Base64(HMACSHA256.HashData(_key, body));
    }

    public int Decode(string id, string cursor)
    {
        try
        {
            if (cursor.Length > 512) throw new FormatException();
            var parts = cursor.Split('.');
            if (parts.Length != 2) throw new FormatException();
            var body = Unbase64(parts[0]);
            if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(_key, body), Unbase64(parts[1])))
                throw new FormatException();
            var payload = Encoding.UTF8.GetString(body).Split(':');
            if (payload.Length != 2 || payload[0] != id || !int.TryParse(payload[1], out var position) || position < 0)
                throw new FormatException();
            return position;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or NullReferenceException)
        {
            throw new FederationQueryException("InvalidCursor", 400);
        }
    }

    private static string Base64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Unbase64(string value) => Convert.FromBase64String(
        value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
}
