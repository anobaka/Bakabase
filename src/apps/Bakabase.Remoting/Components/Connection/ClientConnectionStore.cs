using System.Text.Json;
using System.Text.Json.Serialization;
using Bakabase.Remoting.Abstractions;
using Bakabase.Remoting.Abstractions.Models;

namespace Bakabase.Remoting.Components.Connection;

public interface IClientConnectionStore
{
    /// <summary>
    /// The current contents, or an empty set when nothing has been written. Never
    /// creates the file or its directory.
    /// </summary>
    ClientConnectionData Read();

    Task<T> MutateAsync<T>(Func<ClientConnectionData, T> mutate, CancellationToken ct = default);

    Task MutateAsync(Action<ClientConnectionData> mutate, CancellationToken ct = default);
}

/// <summary>
/// <c>connection.json</c> under the client's data directory: which servers this client
/// has paired with, and the keys it signs with.
/// </summary>
/// <remarks>
/// The same shape as the server's device store, for the same reasons — held in memory
/// after the first read because every outgoing request needs the key, written back
/// atomically so a crash mid-write leaves the previous contents rather than a truncated
/// file that would strand the client with no way back to its server.
/// </remarks>
public sealed class ClientConnectionStore(IClientDataDirectory directory) : IClientConnectionStore
{
    public const string FileName = "connection.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = {new JsonStringEnumConverter()}
    };

    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Lock _cacheGate = new();
    private ClientConnectionData? _cache;

    private string FilePath => System.IO.Path.Combine(directory.Path, FileName);

    public ClientConnectionData Read()
    {
        lock (_cacheGate)
        {
            if (_cache != null)
            {
                return _cache;
            }
        }

        var loaded = Load();

        lock (_cacheGate)
        {
            return _cache ??= loaded;
        }
    }

    private ClientConnectionData Load()
    {
        var path = FilePath;
        if (!File.Exists(path))
        {
            return new ClientConnectionData();
        }

        try
        {
            return JsonSerializer.Deserialize<ClientConnectionData>(File.ReadAllText(path), SerializerOptions)
                   ?? new ClientConnectionData();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            // A corrupt file must not stop the client from starting. Treating it as
            // empty means pairing again, which is visible and recoverable; refusing to
            // launch is neither.
            return new ClientConnectionData();
        }
    }

    public async Task MutateAsync(Action<ClientConnectionData> mutate, CancellationToken ct = default) =>
        await MutateAsync<object?>(data =>
        {
            mutate(data);
            return null;
        }, ct);

    public async Task<T> MutateAsync<T>(Func<ClientConnectionData, T> mutate, CancellationToken ct = default)
    {
        await _writeGate.WaitAsync(ct);
        try
        {
            var data = Read();
            var result = mutate(data);

            var dir = directory.Ensure();
            var path = System.IO.Path.Combine(dir, FileName);
            var temp = path + ".tmp";

            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(data, SerializerOptions), ct);
            File.Move(temp, path, true);

            lock (_cacheGate)
            {
                _cache = data;
            }

            return result;
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
