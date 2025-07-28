using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.PlayList.Extensions;
using Bakabase.InsideWorld.Business.Components.PlayList.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PlayList.Models.Input;
using Bakabase.InsideWorld.Business.Components.PlayList.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/playlist")]
    public class PlaylistController(IPlayListService service, IResourceService resourceService) : Controller
    {
        [SwaggerOperation(OperationId = "GetPlaylist")]
        [HttpGet("{id}")]
        public async Task<SingletonResponse<PlayList>> Get(int id)
        {
            var playlist = await service.Get(id);
            return new SingletonResponse<PlayList>(playlist);
        }

        [SwaggerOperation(OperationId = "GetAllPlaylists")]
        [HttpGet]
        public async Task<ListResponse<PlayList>> GetAll()
        {
            return new ListResponse<PlayList>(await service.GetAll());
        }

        [SwaggerOperation(OperationId = "AddPlaylist")]
        [HttpPost]
        public async Task<BaseResponse> Add([FromBody] PlayListAddInputModel model)
        {
            return await service.Add(model);
        }

        [SwaggerOperation(OperationId = "PutPlaylist")]
        [HttpPut("{id}")]
        public async Task<BaseResponse> Put(int id, [FromBody] PlayList model)
        {
            return await service.Put(id, model);
        }

        [SwaggerOperation(OperationId = "DeletePlaylist")]
        [HttpDelete("{id}")]
        public async Task<BaseResponse> Delete(int id)
        {
            return await service.Delete(id);
        }

        [SwaggerOperation(OperationId = "PatchPlaylist")]
        [HttpPatch("{id}")]
        public async Task<BaseResponse> Patch(int id, [FromBody] PlayListPatchInputModel request)
        {
            return await service.Patch(id, request);
        }

        [SwaggerOperation(OperationId = "GetPlaylistFiles")]
        [HttpGet("{id}/files")]
        public async Task<ListResponse<List<string>>> GetFiles(int id)
        {
            var pl = await service.Get(id);
            if (pl == null)
            {
                return new ListResponse<List<string>>();
            }
            if (pl.Items?.Any() == true)
            {
                var resourceItemIds = pl.Items.Where(a => a.File.IsNullOrEmpty() && a.ResourceId.HasValue)
                    .Select(a => a.ResourceId).ToHashSet();
                var resourcePaths =
                    (await resourceService.GetAll(e => resourceItemIds.Contains(e.Id), ResourceAdditionalItem.None)).ToDictionary(
                        a => a.Id, a => a.Path);
                var fileGroups = new List<List<string>>();
                foreach (var item in pl.Items)
                {
                    if (item.File.IsNotEmpty())
                    {
                        fileGroups.Add(new List<string> {item.File});
                    }
                    else
                    {
                        if (item.ResourceId.HasValue)
                        {
                            if (resourcePaths.TryGetValue(item.ResourceId.Value, out var path))
                            {
                                if (System.IO.File.Exists(path))
                                {
                                    fileGroups.Add([path]);
                                }
                                else
                                {
                                    if (Directory.Exists(path))
                                    {
                                        fileGroups.Add(Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                                            .ToList());
                                    }
                                }
                            }
                        }
                    }
                }

                return new ListResponse<List<string>>(fileGroups);
            }

            return new ListResponse<List<string>>();
        }
    }
}