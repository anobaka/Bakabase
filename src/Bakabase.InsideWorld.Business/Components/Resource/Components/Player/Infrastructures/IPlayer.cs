using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Component;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.Player.Infrastructures
{
    public interface IPlayer : IComponent
    {
        Task Play(string file);
    }
}
