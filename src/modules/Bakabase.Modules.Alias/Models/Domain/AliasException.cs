using System.Runtime.Serialization;
using Bakabase.Modules.Alias.Models.Domain.Constants;

namespace Bakabase.Modules.Alias.Models.Domain;

public class AliasException : Exception
{
    public AliasExceptionType Type { get; }

    public AliasException(AliasExceptionType type)
    {
        Type = type;
    }

    public AliasException(AliasExceptionType type, string? message) : base(message)
    {
        Type = type;
    }

    public AliasException(AliasExceptionType type, string? message, Exception? innerException) : base(message,
        innerException)
    {
        Type = type;
    }
}