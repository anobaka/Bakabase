using System.Threading.Tasks;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Tests.Implementations;

public class TestBOptionsManager<TOptions> : IBOptionsManager<TOptions> where TOptions : class
{
    private TOptions _value;

    public TestBOptionsManager(TOptions value)
    {
        _value = value;
    }

    public TOptions Value => _value;

    public void Save(TOptions options)
    {
        _value = options;
    }

    public Task SaveAsync(TOptions options)
    {
        _value = options;
        return Task.CompletedTask;
    }
}
