namespace Bakabase.Abstractions.Components.Tasks;

public record BTaskEvent<TEvent>
{
    public BTaskEvent(TEvent @event, DateTime? dt = null)
    {
        Event = @event;
        DateTime = dt ?? DateTime.Now;
    }

    public DateTime DateTime { get; set; }
    public TEvent Event { get; set; }
}