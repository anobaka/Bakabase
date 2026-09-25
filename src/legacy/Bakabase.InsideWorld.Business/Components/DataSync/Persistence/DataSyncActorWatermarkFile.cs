using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Runtime;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

/// <summary>
/// What <c>{data-sync}/actor.json</c> holds (§4.7): the actor this device last issued counters under, as of the
/// last commit that issued counters or rotated. Kept outside the database so a database restored behind this
/// device's back is noticed (§5.6).
/// </summary>
public sealed record DataSyncActorWatermark(int Generation, string ActorId, long Counter, string DbInstanceId);

/// <param name="Watermark">Null when the file is missing or unreadable.</param>
/// <param name="Problem">Why an existing file could not be used; null when it is missing or valid.</param>
public sealed record DataSyncActorWatermarkRead(DataSyncActorWatermark? Watermark, string? Problem)
{
    public bool Missing => Watermark is null && Problem is null;
}

/// <summary>
/// Reads and writes the watermark file. Writes are atomic (a temporary file in the same folder, then a move), so a
/// crash leaves either the old file or the new one, never a torn one. Deciding what the watermark means is the
/// actor guard's (§5.6); this class only keeps the file.
/// </summary>
public sealed class DataSyncActorWatermarkFile(IDataSyncDataDirectory directory)
{
    public const string FileName = "actor.json";

    private readonly object _writeLock = new();

    public string FilePath => Path.Combine(directory.Path, FileName);

    public DataSyncActorWatermarkRead Read()
    {
        string json;
        try
        {
            json = File.ReadAllText(FilePath);
        }
        catch (FileNotFoundException)
        {
            return new DataSyncActorWatermarkRead(null, null);
        }
        catch (DirectoryNotFoundException)
        {
            return new DataSyncActorWatermarkRead(null, null);
        }

        DataSyncActorWatermark? watermark;
        try
        {
            watermark = JsonSerializer.Deserialize<DataSyncActorWatermark>(json, DataSyncJson.Options);
        }
        catch (JsonException e)
        {
            return new DataSyncActorWatermarkRead(null, $"notJson: {e.Message}");
        }

        var problem = watermark switch
        {
            null => "empty",
            {Generation: < 1} => "generation",
            _ when !DataSyncActorId.IsValid(watermark.ActorId) => "actorId",
            {Counter: < 0} => "counter",
            _ when !IsInstanceId(watermark.DbInstanceId) => "dbInstanceId",
            _ => null,
        };
        return problem is null ? new DataSyncActorWatermarkRead(watermark, null) : new DataSyncActorWatermarkRead(null, problem);
    }

    public Task WriteAsync(DataSyncActorWatermark watermark, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(watermark);
        ct.ThrowIfCancellationRequested();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(watermark, DataSyncJson.Options);
        lock (_writeLock)
        {
            Directory.CreateDirectory(directory.Path);
            var temp = Path.Combine(directory.Path, $"{FileName}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temp, FilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        return Task.CompletedTask;
    }

    private static bool IsInstanceId(string? value) =>
        value is {Length: 32} && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
