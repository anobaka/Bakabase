using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Modules.Federation;
using Bakabase.Modules.Federation.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Bakabase.Service.Components.Federation;

/// <summary>Global fail-closed check: endpoint metadata and the early gate must agree.</summary>
public sealed class FederationLocalAccessFilter : IAsyncAuthorizationFilter, IOrderedFilter
{
    public int Order => int.MinValue;

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var attribute = GetEndpointAttribute(context);
        var handled = FederationHttpContext.IsHandled(context.HttpContext);
        if (!handled && attribute == null) return Task.CompletedTask;
        var path = context.HttpContext.Request.Path.Value ?? "";
        var valid = attribute != null && handled && FederationHttpContext.GetKind(context.HttpContext) == attribute.Kind &&
                    FederationRoutePolicy.Allows(attribute.Kind, context.HttpContext.Request.Method, path) &&
                    (attribute.Kind != FederationEndpointKind.Export || FederationHttpContext.GetNodePrincipal(context.HttpContext) != null) &&
                    (attribute.Kind != FederationEndpointKind.Local || FederationAccessMiddleware.IsLocalCaller(context.HttpContext));
        if (!valid)
            context.Result = new ContentResult
            {
                StatusCode = 403,
                ContentType = "application/json; charset=utf-8",
                Content = JsonSerializer.Serialize(new { code = "FederationEndpointDenied", message = "The endpoint did not pass its federation access policy." }, FederationJson.Options)
            };
        return Task.CompletedTask;
    }

    public static FederationEndpointAttribute? GetEndpointAttribute(FilterContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor descriptor) return null;
        return descriptor.MethodInfo.GetCustomAttributes(typeof(FederationEndpointAttribute), true)
                   .OfType<FederationEndpointAttribute>().FirstOrDefault() ??
               descriptor.ControllerTypeInfo.GetCustomAttributes(typeof(FederationEndpointAttribute), true)
                   .OfType<FederationEndpointAttribute>().FirstOrDefault();
    }

    public static bool HasHandledEndpoint(FilterContext context) =>
        FederationHttpContext.IsHandled(context.HttpContext) && GetEndpointAttribute(context) is { } attribute &&
        FederationHttpContext.GetKind(context.HttpContext) == attribute.Kind;
}
