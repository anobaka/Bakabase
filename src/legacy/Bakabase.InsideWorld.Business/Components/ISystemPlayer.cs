using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components;

public interface ISystemPlayer
{
    Task Play(string file);
}