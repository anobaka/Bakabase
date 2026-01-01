using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components;

namespace Bakabase.Tests.Implementations;

public class TestSystemPlayer : ISystemPlayer
{
    public Task Play(string file)
    {
        return Task.CompletedTask;
    }
}
