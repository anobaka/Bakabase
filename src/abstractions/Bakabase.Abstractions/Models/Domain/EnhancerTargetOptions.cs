namespace Bakabase.Abstractions.Models.Domain;

public abstract record EnhancerTargetOptions()
{
    public int Target { get; set; }
    public string? DynamicTarget { get; set; }
}