using System.Threading;
using System.Threading.Tasks;
using Bootstrap.Components.Orm;

namespace Bakabase.InsideWorld.Business.Services;

/// <summary>Share the profile's cache lifetime and serialize library deletion with mark sync snapshots.</summary>
internal static class MediaLibraryMutationGate
{
    public static Task<SemaphoreSlim> Acquire(GlobalCacheVault cache) =>
        cache.RequestLock("Bakabase.MediaLibraryMutation");
}
