using System.Linq.Expressions;
using Bakabase.Modules.DataCard.Abstractions.Models.Domain;
using Bakabase.Modules.DataCard.Models.Input;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Modules.DataCard.Abstractions.Services;

public interface IDataCardTypeService
{
    Task<List<DataCardType>> GetAll(Expression<Func<Models.Db.DataCardTypeDbModel, bool>>? selector = null);
    Task<DataCardType?> GetById(int id);
    Task<SingletonResponse<DataCardType>> Add(DataCardTypeAddInputModel model);
    Task<BaseResponse> Update(int id, DataCardTypeUpdateInputModel model);
    Task<BaseResponse> UpdateDisplayTemplate(int id, DataCardDisplayTemplate template);
    Task<BaseResponse> Delete(int id);
}
