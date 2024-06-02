using System;
using Bakabase.InsideWorld.Business.Components.Legacy.Models;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Orm.Infrastructures;

namespace Bakabase.InsideWorld.Business.Components.Legacy.Services;

public class LegacyResourceService(IServiceProvider serviceProvider)
    : ResourceService<InsideWorldDbContext, LegacyDbResource, int>(serviceProvider);