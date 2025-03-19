namespace Bakabase.Abstractions.Components.Tasks;

[AttributeUsage(AttributeTargets.Class)]
public class BTaskAttribute : Attribute
{
    public TimeSpan? DefaultInterval { get; set; }
}