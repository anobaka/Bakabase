using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

public sealed record ManagedDeviceNameView(string Name, string LibraryEpoch, bool EnvironmentControlled);
public sealed record ManagedDeviceNameInput(string? Name, [Required] string ExpectedName,
    [Required] string LibraryEpoch);

/// <summary>Ordinary administrator access, not a read-only library grant or a widened local federation API.</summary>
[ApiController]
[Route("management/device-name")]
public sealed class ManagedDeviceNameController(FederationStateStore store) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetManagedDeviceName")]
    public async Task<ManagedDeviceNameView> Get(CancellationToken ct)
    {
        var identity = await store.GetIdentityAsync(ct);
        return new(identity.Name, identity.LibraryEpoch, FederationStateStore.IsDisplayNameEnvironmentControlled);
    }

    [HttpPut]
    [SwaggerOperation(OperationId = "SetManagedDeviceName")]
    [ProducesResponseType(typeof(ManagedDeviceNameView), 200)]
    public async Task<IActionResult> Put([FromBody] ManagedDeviceNameInput input, CancellationToken ct)
    {
        if (!NodeRequestSignature.IsIdentifier(input.LibraryEpoch) || string.IsNullOrWhiteSpace(input.ExpectedName))
            return BadRequest(new {code = "InvalidDeviceName", message = "A current name and library identity are required."});
        try
        {
            var identity = await store.ChangeDisplayNameAsync(input.Name, input.ExpectedName, input.LibraryEpoch, ct);
            return Ok(new ManagedDeviceNameView(identity.Name, identity.LibraryEpoch, false));
        }
        catch (FederationAccessException error)
        {
            return StatusCode(error.StatusCode, new {code = error.ErrorCode, message = error.Message});
        }
    }
}
