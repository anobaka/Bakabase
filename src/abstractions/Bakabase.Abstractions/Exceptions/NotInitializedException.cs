using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Exceptions
{
    public class NotInitializedException : Exception
    {
        public InitializationContentType Type { get; }

        public NotInitializedException(InitializationContentType type, string message = null) : base($"[{type}] not initialized. {message}".Trim())
        {
            Type = type;
        }
    }
}