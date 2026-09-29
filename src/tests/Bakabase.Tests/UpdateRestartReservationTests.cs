using System.Threading.Tasks;
using Bakabase.Shell.Components;

namespace Bakabase.Tests;

[TestClass]
public sealed class UpdateRestartReservationTests
{
    [TestMethod]
    public async Task ReservedUpdateBlocksAnOrdinaryCloseAndAnotherUpdate()
    {
        // Reservation uses only the exit gate; the app and GUI are deliberately absent.
        // If a normal close slips past it, that path will dereference them and fail.
        var coordinator = new ExitCoordinator(null!, null!);

        Assert.IsTrue(coordinator.TryReserveUpdateRestart(() => { }));
        Assert.IsFalse(coordinator.TryReserveUpdateRestart(() => { }));
        await coordinator.RequestExitAsync(ExitTrigger.WindowClose);
    }
}
