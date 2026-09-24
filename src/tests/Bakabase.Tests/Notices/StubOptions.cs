using System;
using System.Threading.Tasks;
using Bootstrap.Components.Configuration.Abstractions;
using Newtonsoft.Json;

namespace Bakabase.Tests.Notices;

/// <summary>
/// An options manager kept in memory that saves the way the real one does: a copy is changed
/// and replaces the value, so a caller that kept a reference to the old value cannot have
/// changed what is stored, and <see cref="Value"/> reads what was saved last.
/// </summary>
internal sealed class StubOptions<T>(T value) : IBOptionsManager<T> where T : class
{
    public T Value { get; private set; } = value;
    public int SaveCount { get; private set; }

    public void Save(T options)
    {
        Value = options;
        SaveCount++;
    }

    public Task SaveAsync(T options)
    {
        Save(options);
        return Task.CompletedTask;
    }

    public Task SaveAsync(Action<T> modify)
    {
        var copy = JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(Value))!;
        modify(copy);
        return SaveAsync(copy);
    }
}
