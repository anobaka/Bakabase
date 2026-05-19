using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/component")]
    public class ComponentController(IEnumerable<IDependentComponentService> dependentComponentServices) : Controller
    {
        private readonly Dictionary<string, IDependentComponentService> _services =
            dependentComponentServices.ToDictionary(s => s.Id, s => s);

        [HttpPost("{id}/discover")]
        [SwaggerOperation(OperationId = "DiscoverDependentComponent")]
        public async Task<BaseResponse> Discover(string id, CancellationToken ct)
        {
            if (!_services.TryGetValue(id, out var service))
            {
                return BaseResponseBuilder.BuildBadRequest($"Unknown component id: {id}");
            }

            await service.Discover(ct);
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("{id}/install")]
        [SwaggerOperation(OperationId = "InstallDependentComponent")]
        public async Task<BaseResponse> Install(string id, CancellationToken ct)
        {
            if (!_services.TryGetValue(id, out var service))
            {
                return BaseResponseBuilder.BuildBadRequest($"Unknown component id: {id}");
            }

            await service.Install(ct);
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("{id}/latest-version")]
        [SwaggerOperation(OperationId = "GetDependentComponentLatestVersion")]
        public async Task<SingletonResponse<DependentComponentVersion>> GetLatestVersion(
            string id, CancellationToken ct)
        {
            if (!_services.TryGetValue(id, out var service))
            {
                return SingletonResponseBuilder<DependentComponentVersion>.BuildBadRequest(
                    $"Unknown component id: {id}");
            }

            var version = await service.GetLatestVersion(fromCache: false, ct);
            return new SingletonResponse<DependentComponentVersion>(version);
        }
    }
}
