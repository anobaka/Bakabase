using Bakabase.Abstractions.Services;

namespace Bakabase.Tests.Implementations;

/// <summary>
/// Test implementation of IPrepareCacheTrigger that does nothing.
/// </summary>
public class TestPrepareCacheTrigger : IPrepareCacheTrigger
{
    public void RequestTrigger()
    {
        // Do nothing in tests
    }
}
