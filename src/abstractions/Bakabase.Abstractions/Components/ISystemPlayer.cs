using System.Threading.Tasks;

namespace Bakabase.Abstractions.Components;

public interface ISystemPlayer
{
    Task Play(string file);
}
