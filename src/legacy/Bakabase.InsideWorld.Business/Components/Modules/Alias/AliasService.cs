using System;
using Bakabase.Modules.Alias.Services;
using Bootstrap.Components.Orm;

namespace Bakabase.InsideWorld.Business.Components.Modules.Alias;

public class AliasService(
    FullMemoryCacheResourceService<InsideWorldDbContext, Bakabase.Modules.Alias.Abstractions.Models.Db.Alias, int> orm)
    : AbstractAliasService<InsideWorldDbContext>(orm)
{

}