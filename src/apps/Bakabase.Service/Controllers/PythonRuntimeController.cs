using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>Administrative configuration of Python on the selected server, including remote servers.</summary>
[Route("~/python-runtime")]
[RemoteAccessible(false)]
public sealed class PythonRuntimeController(PythonRuntimeConfigurationService configuration) : Controller
{
    [HttpGet("configuration")]
    [SwaggerOperation(OperationId = "GetPythonRuntimeConfiguration")]
    public async Task<SingletonResponse<PythonRuntimeConfigurationViewModel>> GetConfiguration(CancellationToken ct) =>
        new(await configuration.GetConfigurationAsync(ct));

    [HttpGet("candidates")]
    [SwaggerOperation(OperationId = "GetPythonRuntimeCandidates")]
    public ListResponse<PythonRuntimeCandidateViewModel> GetCandidates() => new(configuration.GetCandidates());

    // Paired/unrestricted clients and the host retain full administration under the global
    // authorization policy; nonpaired restricted callers must not execute a server interpreter.
    // These are explicitly server-owned administrative paths, not media-library file reads.
    // PathParameters is deliberately empty: its media guard cannot authorize executable roots
    // outside libraries, nor does it support paths nested inside a configuration object.
    [HttpPost("validate")]
    [SwaggerOperation(OperationId = "ValidatePythonRuntimeConfiguration")]
    public async Task<SingletonResponse<PythonRuntimeValidationViewModel>> Validate(
        [FromBody] PythonRuntimeConfiguration input, CancellationToken ct) => new(await configuration.ValidateAsync(input, ct));

    [HttpPost("apply")]
    [SwaggerOperation(OperationId = "ApplyPythonRuntimeConfiguration")]
    public async Task<SingletonResponse<PythonRuntimeValidationViewModel>> Apply(
        [FromBody] PythonRuntimeConfiguration input, CancellationToken ct) => new(await configuration.ApplyAsync(input, ct));
}
