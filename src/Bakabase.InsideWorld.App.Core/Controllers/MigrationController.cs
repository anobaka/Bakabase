using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Models.Domain;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bootstrap.Components.Logging.LogService.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/migration")]
    public class MigrationController : Controller
    {
        private readonly PublisherService _publisherService;
        private readonly VolumeService _volumeService;
        private readonly SeriesService _seriesService;
        private readonly OriginalService _originalService;
        private readonly CustomResourcePropertyService _customResourcePropertyService;
        private readonly FavoritesService _favoritesService;
        private readonly ResourceService _resourceService;
        private readonly CustomPropertyService _customPropertyService;
        private readonly MigrationService _migrationService;

        public MigrationController(PublisherService publisherService, VolumeService volumeService,
            SeriesService seriesService, OriginalService originalService,
            CustomResourcePropertyService customResourcePropertyService, FavoritesService favoritesService,
            ResourceService resourceService, CustomPropertyService customPropertyService,
            MigrationService migrationService)
        {
            _publisherService = publisherService;
            _volumeService = volumeService;
            _seriesService = seriesService;
            _originalService = originalService;
            _customResourcePropertyService = customResourcePropertyService;
            _favoritesService = favoritesService;
            _resourceService = resourceService;
            _customPropertyService = customPropertyService;
            _migrationService = migrationService;
        }

        [HttpGet("targets")]
        [SwaggerOperation(OperationId = "GetMigrationTargets")]
        public async Task<ListResponse<MigrationTarget>> GetTargets()
        {
            return new ListResponse<MigrationTarget>(await _migrationService.GetMigrationTargets());
        }

        [HttpPost("target/migration")]
        [SwaggerOperation(OperationId = "MigrateTarget")]
        public async Task<BaseResponse> ApplyMigration([FromBody] MigrationTarget target)
        {
            return await _migrationService.ApplyMigration(target);
        }
    }
}