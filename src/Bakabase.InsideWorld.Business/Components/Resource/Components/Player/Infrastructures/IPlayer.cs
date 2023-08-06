using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.Player.Infrastructures
{
    public interface IPlayer : IComponent
    {
        Task Play(string file);
    }
}
