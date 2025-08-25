using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Newtonsoft.Json;
using Enhancement = Bakabase.Abstractions.Models.Domain.Enhancement;

namespace Bakabase.Abstractions.Extensions
{
    public static class EnhancementExtensions
    {
        public static void FillKey(this EnhancementDbModel dbModel)
        {
            dbModel.Key = $"{dbModel.ResourceId}:{dbModel.EnhancerId}:{dbModel.Target}:{dbModel.DynamicTarget}";
        }
    }
}