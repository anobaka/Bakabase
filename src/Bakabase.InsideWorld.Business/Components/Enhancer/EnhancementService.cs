using System;
using Bakabase.Modules.Enhancer.Services;

namespace Bakabase.InsideWorld.Business.Components.Enhancer;

public class EnhancementService(IServiceProvider serviceProvider) : AbstractEnhancementService<InsideWorldDbContext>(serviceProvider)
{
    
}