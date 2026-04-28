using System.Linq.Expressions;
using Bakabase.Modules.DataCard.Models.Input;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Modules.DataCard.Abstractions.Services;

public interface IDataCardService
{
    Task<List<Models.Domain.DataCard>> GetAll(Expression<Func<Models.Db.DataCardDbModel, bool>>? selector = null);
    Task<Models.Domain.DataCard?> GetById(int id);
    Task<SearchResponse<Models.Domain.DataCard>> Search(DataCardSearchInputModel model);
    Task<SingletonResponse<Models.Domain.DataCard>> Add(DataCardAddInputModel model);
    Task<BaseResponse> Update(int id, DataCardUpdateInputModel model);
    Task<BaseResponse> Delete(int id);
    Task<SingletonResponse<Models.Domain.DataCard>> FindByIdentity(DataCardFindByIdentityInputModel model);
    Task<List<Models.Domain.DataCard>> GetAssociatedCards(int resourceId);
    Task<List<int>> GetAssociatedResourceIds(int cardId);
    Task<BaseResponse> DeleteByTypeId(int typeId);
    Task<SingletonResponse<int>> CreateInitialData(int typeId, bool onlyFromResources,
        List<int>? allowNullPropertyIds);

    Task<SingletonResponse<Models.Domain.DataCardInitialDataPreview>> PreviewInitialData(int typeId,
        bool onlyFromResources, List<int>? allowNullPropertyIds);
}
