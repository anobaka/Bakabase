using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Tests.Implementations;

public class TestBOptions<TOptions> : IBOptions<TOptions> where TOptions : class
{
    public TestBOptions(TOptions value)
    {
        Value = value;
    }

    public TOptions Value { get; }
}
