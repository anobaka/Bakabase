using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.Service.Services.Telemetry;

/// <summary>Also checks consent at network-send time, after an SDK's internal queue.</summary>
public sealed class ConsentHttpMessageHandler(Func<bool> allowed, HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        allowed() ? base.SendAsync(request, cancellationToken) :
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
}
