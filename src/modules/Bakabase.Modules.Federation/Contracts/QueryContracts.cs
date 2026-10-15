using System.Text.Json.Serialization;

namespace Bakabase.Modules.Federation.Contracts;

/// <summary>An identity in one incarnation of one owner's library, never a path or an external work ID.</summary>
public sealed record ResourceRef(string NodeId, string LibraryEpoch, int ResourceId);

public sealed class FederationQueryException : Exception
{
    public FederationQueryException(string code, int statusCode, string? message = null,
        bool retryable = false, string? field = null, string? nodeId = null,
        Exception? innerException = null)
        : base(message ?? code, innerException)
    {
        Code = code;
        StatusCode = statusCode;
        Retryable = retryable;
        Field = field;
        NodeId = nodeId;
    }

    public string Code { get; }
    public int StatusCode { get; }
    public bool Retryable { get; }
    public string? Field { get; }
    public string? NodeId { get; }
}
