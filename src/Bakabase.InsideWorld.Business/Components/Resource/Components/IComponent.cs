using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components
{
    public interface IComponent
    {
        Task<string> Validate();
    }
}