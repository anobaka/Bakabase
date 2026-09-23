using System.Threading.Tasks;
using System.Linq;
using Bakabase.Service.Components;
using Bakabase.Service.Components.Federation;

namespace Bakabase.Service;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.FirstOrDefault() == FederationCli.Command) return await FederationCli.RunAsync(args);
        var host = new BakabaseHost(new NullGuiAdapter(), new NullSystemService());
        await host.Start(args.Where(a => a != "--federation-invite-on-start").ToArray());
        return 0;
    }
}
