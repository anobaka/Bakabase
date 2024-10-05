using System;
using Bakabase.Modules.Enhancer.Services;
using Bakabase.Modules.StandardValue.Abstractions.Components;

namespace Bakabase.InsideWorld.Business.Components.Enhancer;

public class EnhancementService(IServiceProvider serviceProvider) : AbstractEnhancementService<InsideWorldDbContext>(serviceProvider);