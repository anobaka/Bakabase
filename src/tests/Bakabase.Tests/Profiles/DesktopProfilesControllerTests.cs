using Bakabase.Abstractions.Components.App;
using Bakabase.Service.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Profiles;

[TestClass]
public sealed class DesktopProfilesControllerTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Accepted_switch_commits_only_after_response_or_abort_and_only_once(bool abortFirst)
    {
        var profiles = new Profiles();
        using var services = new ServiceCollection().AddSingleton<IDesktopProfileService>(profiles).BuildServiceProvider();
        using var aborted = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestServices = services, RequestAborted = aborted.Token };
        var response = new Response(); context.Features.Set<IHttpResponseFeature>(response);
        var controller = new DesktopProfilesController { ControllerContext = new ControllerContext { HttpContext = context } };
        await controller.Open("target", new(false));
        Assert.AreEqual(0, profiles.Commits, "The old host must remain usable while the response is being written.");
        if (abortFirst) aborted.Cancel();
        await response.Complete();
        aborted.Cancel();
        Assert.AreEqual(1, profiles.Commits);
    }

    [TestMethod]
    public async Task Activating_current_workspace_does_not_commit_a_switch()
    {
        var profiles = new Profiles { Prepared = false };
        using var services = new ServiceCollection().AddSingleton<IDesktopProfileService>(profiles).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        var response = new Response(); context.Features.Set<IHttpResponseFeature>(response);
        var controller = new DesktopProfilesController { ControllerContext = new ControllerContext { HttpContext = context } };
        await controller.Open("current", new(true)); await response.Complete();
        Assert.AreEqual(0, profiles.Commits);
    }

    private sealed class Profiles : IDesktopProfileService
    {
        public bool Prepared = true;
        public int Commits;
        public Task<bool> OpenAsync(string id, bool closeCurrent) => Task.FromResult(Prepared);
        public Task CompleteOpenAsync(string id) { Interlocked.Increment(ref Commits); return Task.CompletedTask; }
        public DesktopProfilesView Get() => throw new NotSupportedException();
        public DesktopProfileView Add(DesktopProfileAddInput input) => throw new NotSupportedException();
        public DesktopProfileView Update(string id, DesktopProfileUpdateInput input) => throw new NotSupportedException();
        public void Remove(string id) => throw new NotSupportedException();
    }
    private sealed class Response : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _completed = [];
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => false;
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) => _completed.Add((callback, state));
        public async Task Complete() { foreach (var (callback, state) in _completed) await callback(state); }
    }
}
