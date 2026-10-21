using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Telemetry;
using Bakabase.Abstractions.Models.Domain;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Services.Telemetry;

/// <summary>
/// An explicitly selected synchronous business boundary. Never apply to an endpoint that only queues work.
/// Only fixed enums and numeric summaries leave the request; no parameters, model values, IDs or exception text.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ProductEventAttribute(string eventName, string featureId, string trigger = "unknown") : ActionFilterAttribute
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var telemetry = context.HttpContext.RequestServices.GetService<IProductTelemetry>();
        var actor = TelemetryHttpContext.Read(context.HttpContext);
        if (telemetry == null || actor == null) { await next(); return; }
        var watch = Stopwatch.StartNew();
        ActionExecutedContext? result = null;
        Exception? exception = null;
        try { result = await next(); }
        catch (Exception error) { exception = error; throw; }
        finally
        {
            try
            {
                exception ??= result?.Exception;
                var value = (result?.Result as ObjectResult)?.Value;
                var status = (result?.Result as ObjectResult)?.StatusCode ??
                             (result?.Result as StatusCodeResult)?.StatusCode ?? context.HttpContext.Response.StatusCode;
                var outcome = exception is OperationCanceledException ? "cancelled" :
                    exception != null || status >= 400 || value is BaseResponse { Code: not 0 } ? "failed" : "success";
                var properties = new Dictionary<string, object?>
                {
                    ["feature_id"] = featureId, ["outcome"] = outcome,
                    ["duration_ms"] = watch.Elapsed.TotalMilliseconds, ["trigger"] = trigger
                };
                if (value is SearchResponse<Resource> search && outcome == "success")
                {
                    properties["result_count"] = search.TotalCount;
                    properties["zero_results"] = search.TotalCount == 0;
                }
                telemetry.Track(eventName, properties, actor);
            }
            catch { /* Analytics must not mask a business result or the original exception. */ }
        }
    }
}
