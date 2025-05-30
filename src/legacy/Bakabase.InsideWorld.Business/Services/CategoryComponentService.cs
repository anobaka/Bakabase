using System;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Models.ResponseModels;
using Microsoft.Extensions.Localization;

namespace Bakabase.InsideWorld.Business.Services
{
    [Obsolete]
    public class CategoryComponentService : ResourceService<InsideWorldDbContext, CategoryComponent, int>
    {
        protected ComponentService ComponentService => GetRequiredService<ComponentService>();
        protected IStringLocalizer<SharedResource> Localizer => GetRequiredService<IStringLocalizer<SharedResource>>();

        public CategoryComponentService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="categoryId"></param>
        /// <param name="keys">Will not be validated.</param>
        /// <param name="type"></param>
        /// <returns></returns>
        public async Task<BaseResponse> Configure(int categoryId, string[] keys, ComponentType? type = null)
        {
            var data = await GetAll(
                a => a.CategoryId == categoryId && (!type.HasValue || a.ComponentType == type.Value));
            var dataKeys = data.Select(a => a.ComponentKey).ToHashSet();
            var badData = data.Where(a => !keys.Contains(a.ComponentKey)).ToArray();
            var newKeys = keys.Except(dataKeys).ToArray();
            var newDescriptors = await ComponentService.GetDescriptors(newKeys);
            var newData = newDescriptors.Select(d => new CategoryComponent
            {
                CategoryId = categoryId,
                ComponentKey = d.Id,
                ComponentType = d.ComponentType,
            }).ToArray();

            var invalid = newData.FirstOrDefault(s => type.HasValue && s.ComponentType != type.Value);
            if (invalid != null)
            {
                return BaseResponseBuilder.BuildBadRequest(Localizer[SharedResource.Component_TypeMismatch,
                    type.ToString()!, invalid.ComponentType.ToString(), invalid.Id]);
            }

            if (badData.Any())
            {
                await RemoveRange(badData);
            }

            if (newData.Any())
            {
                await AddRange(newData);
            }

            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> DuplicateAllInCategory(int fromCategoryId, int toCategoryId)
        {
            var data = await GetAll(x => x.CategoryId == fromCategoryId);
            if (data != null)
            {
                await AddRange(data.Select(d => d.Duplicate(toCategoryId)));
            }

            return BaseResponseBuilder.Ok;
        }
    }
}