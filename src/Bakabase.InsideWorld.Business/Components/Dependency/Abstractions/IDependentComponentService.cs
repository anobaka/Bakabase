using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Abstractions
{
    /// <summary>
    /// <para>Not available: 1. New version limitation</para>
    /// <para>todo: consider merging with category components</para>
    /// </summary>
    public interface IDependentComponentService
    {
        string Id { get; }
        string DisplayName { get; }
        string DefaultLocation { get; }

        Task Install(CancellationToken ct);
        Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct);

        /// <summary>
        /// Priority: App component directory > System wide
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task Discover(CancellationToken ct);

        // Task<DependentComponentVersion> GetVersion(CancellationToken ct);
        // Task<bool> VerifyIntegrality(CancellationToken ct);

        DependentComponentStatus Status { get; }
        DependentComponentContext Context { get; }

        event Func<DependentComponentContext, Task>? OnStateChange;
    }
}