using System;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Bakabase.Service.Components.OAuth;

namespace Bakabase.Service.Components.CloudDownloads.Rclone;

/// <summary>Expected native authorization failures use the application's normal response envelope.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RcloneAuthorizationErrorsAttribute : Attribute, IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        var code = context.Exception switch {RcloneRemoteException error => error.Code, OAuthAuthorizationException error => error.Code, _ => null};
        if (code == null) return;
        // Do not serialize the exception: its stack and native process details are not an API contract.
        context.Result = new ObjectResult(SingletonResponseBuilder<RcloneAuthorizationSessionViewModel>.BuildBadRequest(code));
        context.ExceptionHandled = true;
    }
}
