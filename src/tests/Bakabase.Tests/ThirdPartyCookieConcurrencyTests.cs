using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Bakabase.Modules.ThirdParty.Components.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class ThirdPartyCookieConcurrencyTests
{
    [TestMethod]
    public async Task ConcurrentResponsesAndRequestsObserveCompleteCookieUpdates()
    {
        var cookies = new ThirdPartyCookieContainer();
        var uri = new Uri("https://example.test/");
        const string key = "source:account";
        const string seed = "session=initial; revision=initial";
        cookies.GetOrCreate(key, seed, uri);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writers = Enumerable.Range(0, 3).Select(writer => Task.Run(async () =>
        {
            await start.Task;
            for (var i = 0; i < 1000; i++)
            {
                var value = $"{writer}-{i}";
                using var response = new HttpResponseMessage();
                response.Headers.TryAddWithoutValidation("Set-Cookie",
                    new[] {$"session={value}; Path=/", $"revision={value}; Path=/"});
                cookies.ProcessResponse(key, seed, uri, response);
            }
        })).ToArray();
        var reader = Task.Run(async () =>
        {
            await start.Task;
            for (var i = 0; i < 5000; i++)
            {
                var values = cookies.GetCookieHeader(key, seed, uri)!.Split(';')
                    .Select(x => x.Trim().Split('=', 2)).ToDictionary(x => x[0], x => x[1]);
                Assert.IsTrue(values.ContainsKey("session") && values.ContainsKey("revision"),
                    "A request observed the gap between expiring and replacing a cookie.");
                Assert.AreEqual(values["session"], values["revision"],
                    "A request observed a partly applied Set-Cookie response.");
            }
        });
        start.TrySetResult();
        await Task.WhenAll(writers.Append(reader));
    }
}
