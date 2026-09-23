namespace Bakabase.Modules.Federation.Security;

public sealed class FederationAccessException(string errorCode, int statusCode, string message)
    : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
    public int StatusCode { get; } = statusCode;
}
