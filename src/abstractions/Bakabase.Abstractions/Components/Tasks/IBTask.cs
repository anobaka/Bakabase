namespace Bakabase.Abstractions.Components.Tasks;

public interface IBTask
{
    string Key { get; }
    CancellationToken? Ct { get; }
    string Name { get; }
    string Description { get; }

    Task ChangeInterval(TimeSpan interval);
    TimeSpan Interval { get; }
    Task Start();
    Task Stop();
    Task Disable();
    Task Enable();
}