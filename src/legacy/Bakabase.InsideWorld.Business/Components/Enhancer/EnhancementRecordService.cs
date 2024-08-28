using Bakabase.Abstractions.Models.Db;
using Bakabase.Modules.Enhancer.Services;
using Bootstrap.Components.Orm.Infrastructures;

namespace Bakabase.InsideWorld.Business.Components.Enhancer;

public class EnhancementRecordService(ResourceService<InsideWorldDbContext, EnhancementRecord, int> orm)
    : AbstractEnhancementRecordService<InsideWorldDbContext>(orm)
{

}