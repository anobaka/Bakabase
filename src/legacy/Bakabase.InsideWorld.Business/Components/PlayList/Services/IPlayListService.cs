using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PlayList.Models.Input;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Components.PlayList.Services;

public interface IPlayListService
{
    Task<BaseResponse> Patch(int id, PlayListPatchInputModel model);
    Task<BaseResponse> Put(int id, Models.Domain.PlayList model);
    Task<BaseResponse> Delete(int id);
    Task<Models.Domain.PlayList?> Get(int id);
    Task<List<Models.Domain.PlayList>> GetAll();
    Task<BaseResponse> Add(PlayListAddInputModel model);
}