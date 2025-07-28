using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components;

public class DownloaderFactory(IDownloaderLocalizer localizer, IServiceProvider serviceProvider) : IDownloaderFactory
{
    public List<DownloaderDefinition> GetDefinitions()
    {
        return DownloaderInternals.Definitions
            .Select(def => def with 
            {
                Name = localizer.GetDownloaderName(def.ThirdPartyId, def.EnumTaskType),
                Description = localizer.GetDownloaderDescription(def.ThirdPartyId, def.EnumTaskType),
                NamingFields = def.NamingFields.Select(f => f with 
                {
                    Name = localizer.GetNamingFieldName(f.EnumValue),
                    Description = localizer.GetNamingFieldDescription(f.Description),
                    Example = localizer.GetNamingFieldExample(f.Example)
                }).ToList()
            }).ToList();
    }

    public IDownloaderHelper GetHelper(ThirdPartyId thirdPartyId, int taskType)
    {
        var type = DownloaderInternals.ThirdPartyIdTaskTypeDefinitionMap[thirdPartyId][taskType].HelperType;
        return (IDownloaderHelper)serviceProvider.GetRequiredService(type);
    }

    public IDownloader GetDownloader(ThirdPartyId thirdPartyId, int taskType)
    {
        var type = DownloaderInternals.ThirdPartyIdTaskTypeDefinitionMap[thirdPartyId][taskType].DownloaderType;
        var scope = serviceProvider.CreateAsyncScope();
        return (IDownloader)scope.ServiceProvider.GetRequiredService(type);
    }
}