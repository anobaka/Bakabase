using System.Text.Json;
using Bakabase.Modules.Federation;
using Microsoft.AspNetCore.Mvc;

namespace Bakabase.Service.Controllers;

public abstract class FederationControllerBase : ControllerBase
{
    protected ContentResult FederationResult<T>(T value, int status = 200) => new()
    {
        Content = JsonSerializer.Serialize(value, FederationJson.Options),
        ContentType = "application/json; charset=utf-8",
        StatusCode = status
    };
}
