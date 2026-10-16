namespace Bakabase.Modules.RemoteAccess.Abstractions.Models;

/// <summary>A bounded upstream API response. No credentials or upstream headers are exposed.</summary>
public sealed record ManagedServerResponse(int StatusCode, string ContentType, string Body);
