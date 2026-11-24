using System;
using Bakabase.Abstractions.Components.FileSystem;
using MonoTorrent;
using MonoTorrent.PieceWriter;
using ReusableTasks;

namespace Bakabase.Modules.Downloader.Components;

/// <summary>Counts completed payload operations at the disk writer, below the torrent cache.</summary>
internal sealed class ObservedTorrentPieceWriter(IPieceWriter writer, IFileIoObservation observation) : IPieceWriter
{
    public int OpenFiles => writer.OpenFiles;
    public int MaximumOpenFiles => writer.MaximumOpenFiles;

    public ReusableTask CloseAsync(ITorrentManagerFile file) => writer.CloseAsync(file);
    public ReusableTask<bool> CreateAsync(ITorrentManagerFile file, FileCreationOptions options) => writer.CreateAsync(file, options);
    public ReusableTask<bool> ExistsAsync(ITorrentManagerFile file) => writer.ExistsAsync(file);
    public ReusableTask FlushAsync(ITorrentManagerFile file) => writer.FlushAsync(file);
    public ReusableTask<long?> GetLengthAsync(ITorrentManagerFile file) => writer.GetLengthAsync(file);
    public ReusableTask MoveAsync(ITorrentManagerFile file, string fullPath, bool overwrite) => writer.MoveAsync(file, fullPath, overwrite);
    public ReusableTask<bool> SetLengthAsync(ITorrentManagerFile file, long length) => writer.SetLengthAsync(file, length);
    public ReusableTask SetMaximumOpenFilesAsync(int maximumOpenFiles) => writer.SetMaximumOpenFilesAsync(maximumOpenFiles);

    public async ReusableTask<int> ReadAsync(ITorrentManagerFile file, long offset, Memory<byte> buffer)
    {
        var read = await writer.ReadAsync(file, offset, buffer).ConfigureAwait(false);
        observation.Add(read, 0);
        return read;
    }

    public async ReusableTask WriteAsync(ITorrentManagerFile file, long offset, ReadOnlyMemory<byte> buffer)
    {
        await writer.WriteAsync(file, offset, buffer).ConfigureAwait(false);
        observation.Add(0, buffer.Length);
    }

    public void Dispose() => writer.Dispose();
}
