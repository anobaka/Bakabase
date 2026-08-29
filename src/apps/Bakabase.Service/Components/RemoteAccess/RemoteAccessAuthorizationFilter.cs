using System.Linq;
using System.Net;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.Constants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Bakabase.Service.Components.RemoteAccess
{
    /// <summary>
    /// The inner half of the gate: decides whether an action means anything when
    /// called from a device other than the host.
    /// <para>
    /// Default-deny. An action is reachable only if it, or its controller, carries
    /// <see cref="RemoteAccessibleAttribute"/> with <c>Allowed</c> set. That way the
    /// dozens of endpoints that launch players, open folders and delete files stay
    /// closed without anyone having to enumerate them, and so does every endpoint
    /// added later.
    /// </para>
    /// </summary>
    public class RemoteAccessAuthorizationFilter : IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var remoteContext = context.HttpContext.GetRemoteAccessContext();

            // No context means the middleware did not run. Fail closed.
            if (remoteContext is {IsUnrestricted: true})
            {
                return;
            }

            if (FindAttribute(context) is {Allowed: true})
            {
                return;
            }

            Deny(context, HttpStatusCode.Forbidden, ResponseCode.Unauthorized, RemoteAccessDenialReason.HostOnly,
                "This action runs on the machine hosting Bakabase and is not available from another device.");
        }

        /// <summary>
        /// Action-level attributes win over controller-level ones, so a read-only
        /// controller can still keep one destructive action on the host.
        /// </summary>
        internal static RemoteAccessibleAttribute? FindAttribute(FilterContext context)
        {
            if (context.ActionDescriptor is not ControllerActionDescriptor descriptor)
            {
                return null;
            }

            return descriptor.MethodInfo.GetCustomAttributes(typeof(RemoteAccessibleAttribute), true)
                       .OfType<RemoteAccessibleAttribute>().FirstOrDefault()
                   ?? descriptor.ControllerTypeInfo.GetCustomAttributes(typeof(RemoteAccessibleAttribute), true)
                       .OfType<RemoteAccessibleAttribute>().FirstOrDefault();
        }

        private static void Deny(AuthorizationFilterContext context, HttpStatusCode statusCode,
            ResponseCode responseCode, RemoteAccessDenialReason reason, string message)
        {
            context.HttpContext.Response.Headers["X-Bakabase-Remote-Access"] = reason.ToString();
            context.Result = new ObjectResult(BaseResponseBuilder.Build(responseCode, message))
            {
                StatusCode = (int) statusCode
            };
        }
    }
}
