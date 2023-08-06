using System;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.Exceptions
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