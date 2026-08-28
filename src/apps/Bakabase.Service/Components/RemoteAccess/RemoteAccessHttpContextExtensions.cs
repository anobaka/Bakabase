using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Service.Components.RemoteAccess
{
    public static class RemoteAccessHttpContextExtensions
    {
        private const string ContextKey = "Bakabase.RemoteAccess.Context";

        /// <summary>
        /// Name of the cookie carrying a paired device's token. A cookie rather than a
        /// header because <c>&lt;img&gt;</c>, <c>&lt;video&gt;</c> and <c>EventSource</c>
        /// cannot set headers, and those are how the UI loads covers and media.
        /// </summary>
        public const string DeviceTokenCookieName = "bkb_device";

        /// <summary>
        /// Query parameter carrying a signed single-path token, for URLs opened by
        /// something that has no cookie at all — a native player handed a link.
        /// </summary>
        public const string PathTokenQueryKey = "bkbt";

        public static void SetRemoteAccessContext(this HttpContext httpContext, RemoteAccessContext context) =>
            httpContext.Items[ContextKey] = context;

        /// <summary>
        /// The decision the middleware reached for this request. Null only if the
        /// middleware did not run, which the filters treat as "deny".
        /// </summary>
        public static RemoteAccessContext? GetRemoteAccessContext(this HttpContext httpContext) =>
            httpContext.Items.TryGetValue(ContextKey, out var value) ? value as RemoteAccessContext : null;
    }
}
