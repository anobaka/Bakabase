namespace Bakabase.Abstractions.Components.Component
{
    public interface IComponent
    {
        Task<string> Validate();
    }
}