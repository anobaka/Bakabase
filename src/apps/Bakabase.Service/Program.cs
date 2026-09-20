using System.Threading.Tasks;
using System.Linq;
using Bakabase.Service.Components;

namespace Bakabase.Service;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = new BakabaseHost(new NullGuiAdapter(), new NullSystemService());
        await host.Start(args.Where(a => a != "--federation-invite-on-start").ToArray());
    }
}
