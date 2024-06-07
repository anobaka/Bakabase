using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/playlist")]
    public class PlaylistController(PlaylistService service, IResourceService resourceService) : Controller
    {
        [SwaggerOperation(OperationId = "GetPlaylist")]
        [HttpGet("{id}")]
        public async Task<SingletonResponse<PlaylistDto>> Get(int id)
        {
            return new SingletonResponse<PlaylistDto>((await service.GetByKey(id)).ToDto());
        }

        [SwaggerOperation(OperationId = "GetAllPlaylists")]
        [HttpGet]
        public async Task<ListResponse<PlaylistDto>> GetAll()
        {
            return new ListResponse<PlaylistDto>((await service.GetAll()).Select(a => a.ToDto()));
        }

        [SwaggerOperation(OperationId = "AddPlaylist")]
        [HttpPost]
        public async Task<BaseResponse> Add([FromBody] PlaylistDto dto)
        {
            return await service.Add(dto.ToEntity());
        }

        [SwaggerOperation(OperationId = "PutPlaylist")]
        [HttpPut]
        public async Task<BaseResponse> Put([FromBody] PlaylistDto dto)
        {
            return await service.Update(dto.ToEntity());
        }

        [SwaggerOperation(OperationId = "DeletePlaylist")]
        [HttpDelete("{id}")]
        public async Task<BaseResponse> Delete(int id)
        {
            return await service.RemoveByKey(id);
        }

        [SwaggerOperation(OperationId = "GetPlaylistFiles")]
        [HttpGet("{id}/files")]
        public async Task<ListResponse<List<string>>> GetFiles(int id)
        {
            var pl = (await service.GetByKey(id)).ToDto();
            if (pl?.Items?.Any() == true)
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