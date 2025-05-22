using Bakabase.Abstractions.Services;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Db;
using Bakabase.Modules.MediaLibraryTemplate.Services;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bootstrap.Components.Orm;

namespace Bakabase.InsideWorld.Business.Components.MediaLibraryTemplate;

public class MediaLibraryTemplateService(FullMemoryCacheResourceService<InsideWorldDbContext, MediaLibraryTemplateDbModel, int> orm, IStandardValueService standardValueService, IEnhancerService enhancerService, ICategoryService categoryService, ISpecialTextService specialTextService, IExtensionGroupService extensionGroupService, IPropertyService propertyService) : AbstractMediaLibraryTemplateService<InsideWorldDbContext>(orm, standardValueService, enhancerService, categoryService, specialTextService, extensionGroupService, propertyService)
{
    
}