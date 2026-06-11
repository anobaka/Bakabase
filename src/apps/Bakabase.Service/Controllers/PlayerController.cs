using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Abstractions.Models.Input;
using Bakabase.Modules.Player.Abstractions.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/player")]
    public class PlayerController(IBatchPlayService batchPlayService) : Controller
    {
        [SwaggerOperation(OperationId = "GetBatchPlayCandidates")]
        [HttpPost("batch-play/candidates")]
        public async Task<ListResponse<BatchPlayCandidate>> GetBatchPlayCandidates(
            [FromBody] BatchPlayCandidatesInputModel model)
        {
            var candidates =
                await batchPlayService.GetCandidatesAsync(model.ResourceIds, HttpContext.RequestAborted);
            return new ListResponse<BatchPlayCandidate>(candidates);
        }

        [SwaggerOperation(OperationId = "BatchPlayResources")]
        [HttpPost("batch-play")]
        public async Task<SingletonResponse<BatchPlayResult>> BatchPlay([FromBody] BatchPlayInputModel model)
        {
            try
            {
                var result = await batchPlayService.PlayAsync(model, HttpContext.RequestAborted);
                return new SingletonResponse<BatchPlayResult>(result);
            }
            catch (InvalidOperationException e)
            {
                // User-environment problems (player gone, files moved, …);
                // surface the message instead of a 500.
                return SingletonResponseBuilder<BatchPlayResult>.BuildBadRequest(e.Message);
            }
        }

        [SwaggerOperation(OperationId = "GetPlaylistBatchPlayCandidates")]
        [HttpGet("playlist/{playlistId:int}/batch-play/candidates")]
        public async Task<ListResponse<BatchPlayCandidate>> GetPlaylistBatchPlayCandidates(int playlistId)
        {
            try
            {
                var candidates =
                    await batchPlayService.GetPlaylistCandidatesAsync(playlistId, HttpContext.RequestAborted);
                return new ListResponse<BatchPlayCandidate>(candidates);
            }
            catch (InvalidOperationException e)
            {
                return ListResponseBuilder<BatchPlayCandidate>.BuildBadRequest(e.Message);
            }
        }

        [SwaggerOperation(OperationId = "BatchPlayPlaylist")]
        [HttpPost("playlist/{playlistId:int}/batch-play")]
        public async Task<SingletonResponse<BatchPlayResult>> BatchPlayPlaylist(int playlistId,
            [FromBody] PlaylistBatchPlayInputModel model)
        {
            try
            {
                var result = await batchPlayService.PlayPlaylistAsync(playlistId, model.PlayerKey,
                    HttpContext.RequestAborted);
                return new SingletonResponse<BatchPlayResult>(result);
            }
            catch (InvalidOperationException e)
            {
                return SingletonResponseBuilder<BatchPlayResult>.BuildBadRequest(e.Message);
            }
        }
    }
}
