using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Service.Models.Input
{
    public record RemoteAccessModeInputModel
    {
        /// <summary>
        /// Null resets to the runtime default.
        /// </summary>
        public RemoteAccessMode? Mode { get; set; }
    }
}
