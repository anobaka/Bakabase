using System;
using System.Threading.Tasks;

namespace Bakabase.Service.Components.DataSync;

/// <summary>
/// <c>dotnet Bakabase.Service.dll federation datasync &lt;command&gt;</c>: manages definitions sharing of a running
/// headless instance through its loopback <c>/data-sync</c> API. Placeholder until the data sync API lands.
/// </summary>
public static class DataSyncCli
{
    public static Task<int> RunAsync(string[] args, string port)
    {
        Console.Error.WriteLine("Data sync is not available in this build yet.");
        return Task.FromResult(2);
    }
}
