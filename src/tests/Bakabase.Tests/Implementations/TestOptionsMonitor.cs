using System;
using Microsoft.Extensions.Options;

namespace Bakabase.Tests.Implementations;

public class TestOptionsMonitor<TOptions>(TOptions currentValue) : IOptionsMonitor<TOptions>
{
    public TOptions Get(string? name)
    {
        return CurrentValue;
    }

    public IDisposable? OnChange(Action<TOptions, string?> listener)
    {
        return null;
    }

    public TOptions CurrentValue { get; } = currentValue;
}