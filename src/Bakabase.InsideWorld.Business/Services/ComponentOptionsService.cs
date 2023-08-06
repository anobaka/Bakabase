using System;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Services
{
    public class ComponentOptionsService : ResourceService<InsideWorldDbContext, ComponentOptions, int>
    {
        protected ComponentService ComponentService => GetRequiredService<ComponentService>();

        public ComponentOptionsService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task<SingletonResponse<ComponentOptions>> Add(ComponentOptionsAddRequestModel model)
        {
            var cd = await ComponentService.GetDescriptor(model.ComponentAssemblyQualifiedTypeName);
            var validation = cd.BuildValidationResponse();
            if (validation.Code != 0)
            {
                return validation.ToSingletonResponse<ComponentOptions>();
            }

            var co = new ComponentOptions
            {
                ComponentAssemblyQualifiedTypeName = model.ComponentAssemblyQualifiedTypeName,
                Json = model.Json,
                ComponentType = cd.ComponentType,
                Description = model.Description ?? cd.Description,
                Name = model.Name
            };

            await base.Add(co);

            return new SingletonResponse<ComponentOptions>(co);
        }

        public async Task<BaseResponse> Put(int id, ComponentOptionsAddRequestModel model)
        {
            var cd = await ComponentService.GetDescriptor(id.ToString());
            var validation = cd.BuildValidationResponse();
            if (validation.Code != 0)
            {
                return validation.ToSingletonResponse<ComponentOptions>();
            }

            var options = await GetByKey(id);
            if (options == null)
            {
                return BaseResponseBuilder.NotFound;
            }

            options.ComponentAssemblyQualifiedTypeName = model.ComponentAssemblyQualifiedTypeName;
            options.Description = model.Description;
            options.Name = model.Name;
            options.Json = model.Json;

            await base.Update(options);

            return BaseResponseBuilder.Ok;
        }
    }
}