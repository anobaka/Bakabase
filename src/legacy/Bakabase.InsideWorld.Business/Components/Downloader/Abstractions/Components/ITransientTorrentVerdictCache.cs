namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components
{
    /// <summary>
    /// The in-memory record of which tasks have already been probed and found to have no torrent
    /// during this run.
    ///
    /// Narrower than the manager that implements it on purpose: a scheduling pre-check has no
    /// business being able to start or stop downloads, and this one bit is all it reads. The verdict
    /// is transient — it is rebuilt by probing after a restart, which is why the durable copy lives
    /// on the task itself.
    /// </summary>
    public interface ITransientTorrentVerdictCache
    {
        bool IsKnownNoTorrent(int taskId);
    }
}
