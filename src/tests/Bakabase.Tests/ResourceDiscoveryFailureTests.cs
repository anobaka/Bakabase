using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Services;
using Bakabase.TestKit.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class ResourceDiscoveryFailureTests
{
    [TestMethod]
    public async Task DeletedResource_ReportsTerminalFailure_InsteadOfLeavingDiscoveryPending()
    {
        var services = await TestServiceBuilder.BuildServiceProvider();
        await using var lifetime = (IAsyncDisposable) services;
        using var discovery = new ResourceDiscoveryService(
            NullLogger<ResourceDiscoveryService>.Instance,
            services.GetRequiredService<IServiceScopeFactory>());
        using var output = new FlushedStream();
        var context = new DefaultHttpContext();
        context.Response.Body = output;
        discovery.RegisterConnection(context.Response);
        await discovery.StartAsync(CancellationToken.None);
        try
        {
            await discovery.EnqueueRequest(int.MaxValue, DataOrigin.FileSystem, ResourceDataType.PlayableItem);
            var message = await output.Messages.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            StringAssert.Contains(message, "event: error");
            StringAssert.Contains(message, "\"success\":false");
            StringAssert.Contains(message, "no longer exists");
        }
        finally
        {
            await discovery.StopAsync(CancellationToken.None);
        }
    }

    private sealed class FlushedStream : MemoryStream
    {
        public Channel<string> Messages { get; } = Channel.CreateUnbounded<string>();

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            var text = Encoding.UTF8.GetString(ToArray());
            if (text.EndsWith("\n\n", StringComparison.Ordinal)) Messages.Writer.TryWrite(text);
            return Task.CompletedTask;
        }
    }
}
