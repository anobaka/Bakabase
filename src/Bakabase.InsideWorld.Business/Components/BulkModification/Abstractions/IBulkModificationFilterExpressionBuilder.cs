using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions
{
    public interface IBulkModificationFilterExpressionBuilder
    {
        Expression<Func<Business.Models.Domain.Resource, bool>> Build(BulkModificationFilter filter);
    }
}
