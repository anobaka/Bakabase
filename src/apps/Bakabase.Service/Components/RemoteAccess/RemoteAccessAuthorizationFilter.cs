using System.Linq;
using System.Net;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
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
            // The separate node boundary has already authenticated this explicit protocol action.
            if (Bakabase.Service.Components.Federation.FederationLocalAccessFilter.HasHandledEndpoint(context))
            {
                return;
            }

            var remoteContext = context.HttpContext.GetRemoteAccessContext();

            // (1) The host itself. Everything the all-in-one does arrives here, so this
            // has to stay the first branch — it is what keeps the all-in-one untouched.
            // No context means the middleware did not run; that is not loopback, and the
            // checks below fail closed.
            if (remoteContext is {IsLoopback: true})
            {
                return;
            }

            // (2) Where the action runs is not a permission, so it is decided before any
            // mode or identity is consulted. Deliberately ahead of the Unrestricted
            // check: that is the container default, and until now a remote browser could
            // call "play this" there, start a player on a screen nobody is watching, and
            // be told it worked.
            if (FindUserMachineAttribute(context) is { } userMachine)
            {
                Deny(context, HttpStatusCode.Forbidden, ResponseCode.Unauthorized,
                    RemoteAccessDenialReason.RunsOnUserMachine,
                    userMachine.Reason ??
                    "This action only means anything on the machine you are sitting at.");
                return;
            }

            // (3) Anything goes, for callers the host has opted into trusting — either
            // by the mode, or by pairing this specific device. Pairing is what makes a
            // remote client feature-complete without anyone marking up the four hundred
            // odd endpoints nobody has reviewed.
            if (remoteContext is {IsUnrestricted: true} or {IsPaired: true})
            {
                return;
            }

            // (4) Default-deny: reachable only if somebody marked it reachable.
            if (FindAttribute(context) is {Allowed: true})
            {
                return;
            }

            Deny(context, HttpStatusCode.Forbidden, ResponseCode.Unauthorized, RemoteAccessDenialReason.HostOnly,
                "This action runs on the machine hosting Bakabase and is not available from another device.");
        }

        /// <summary>
        /// Action-level wins over controller-level, matching
        /// <see cref="FindAttribute"/>.
        /// </summary>
        internal static RunsOnUserMachineAttribute? FindUserMachineAttribute(FilterContext context)
        {
            if (context.ActionDescriptor is not ControllerActionDescriptor descriptor)
            {
                return null;
            }

            return descriptor.MethodInfo.GetCustomAttributes(typeof(RunsOnUserMachineAttribute), true)
                       .OfType<RunsOnUserMachineAttribute>().FirstOrDefault()
                   ?? descriptor.ControllerTypeInfo.GetCustomAttributes(typeof(RunsOnUserMachineAttribute), true)
                       .OfType<RunsOnUserMachineAttribute>().FirstOrDefault();
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
