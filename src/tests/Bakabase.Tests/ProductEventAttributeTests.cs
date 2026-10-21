using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Telemetry;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Service.Services.Telemetry;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class ProductEventAttributeTests
{
    [TestMethod]
    public async Task BusinessFailureInHttp200IsNotSuccessAndResponseTextIsNeverCaptured()
    {
        var events = new Recorder();
        using var services = new ServiceCollection().AddSingleton<IProductTelemetry>(events).BuildServiceProvider();
        var context = Context(services);
        var attribute = new ProductEventAttribute("property_edit_completed", "properties");
        await attribute.OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(context, [], new object())
        { Result = new ObjectResult(new BaseResponse { Code = 17, Message = "private filename" }) { StatusCode = 200 } }));
        Assert.AreEqual("failed", events.Properties!["outcome"]);
        Assert.AreEqual(4, events.Properties.Count);
        Assert.IsFalse(string.Join(",", events.Properties.Values).Contains("private"));
    }

    [TestMethod]
    public async Task ExceptionPropagatesEvenWhenAnalyticsFails()
    {
        var events = new Recorder { Throw = true };
        using var services = new ServiceCollection().AddSingleton<IProductTelemetry>(events).BuildServiceProvider();
        var original = new InvalidOperationException("business exception");
        var thrown = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            new ProductEventAttribute("resource_search_completed", "search").OnActionExecutionAsync(Context(services),
                () => throw original));
        Assert.AreSame(original, thrown);
        Assert.AreEqual("failed", events.Properties!["outcome"]);
    }

    [TestMethod]
    public async Task EmptySearchIsSuccessfulAndNoConsentProducesNoClientOrSystemEvent()
    {
        var events = new Recorder();
        using var services = new ServiceCollection().AddSingleton<IProductTelemetry>(events).BuildServiceProvider();
        var context = Context(services);
        var attribute = new ProductEventAttribute("resource_search_completed", "search");
        Task<ActionExecutedContext> Search() => Task.FromResult(new ActionExecutedContext(context, [], new object())
        { Result = new ObjectResult(new SearchResponse<Resource> { TotalCount = 0 }) });
        await attribute.OnActionExecutionAsync(context, Search);
        Assert.AreEqual("success", events.Properties!["outcome"]);
        Assert.AreEqual(true, events.Properties["zero_results"]);
        Assert.AreEqual(1, events.Count);
        context.HttpContext.Request.Headers.Remove("X-Bakabase-Analytics-Consent");
        await attribute.OnActionExecutionAsync(context, Search);
        Assert.AreEqual(1, events.Count);
    }

    private static ActionExecutingContext Context(IServiceProvider services)
    {
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Headers["X-Bakabase-Analytics-Consent"] = "1";
        http.Request.Headers["X-Bakabase-Analytics-Client"] = "bdafdd4c-e81a-4124-b4da-1bca7e7ef3f3";
        http.Request.Headers["X-Bakabase-Analytics-Surface"] = "browser";
        return new ActionExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), [],
            new Dictionary<string, object?>(), new object());
    }

    private sealed class Recorder : IProductTelemetry
    {
        public int Count;
        public bool Throw;
        public IReadOnlyDictionary<string, object?>? Properties;
        public void Track(string eventName, IReadOnlyDictionary<string, object?>? properties = null, TelemetryActor? actor = null)
        {
            Count++;
            Properties = properties;
            if (Throw) throw new Exception("telemetry failed");
        }
    }
}
