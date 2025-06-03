using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Models.Domain;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using Bootstrap.Components.Orm;
using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Services;
using Microsoft.EntityFrameworkCore;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bootstrap.Extensions;
using System.Reflection;
using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Parsers;

namespace Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Extensions;

public static class DownloadTaskParserExtensions
{
    public static IServiceCollection AddDownloadTaskParser<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddScoped<FullMemoryCacheResourceService<TDbContext, DownloadTaskParseTaskDbModel, int>>();
        services.AddScoped<IDownloadTaskParseTaskService, DownloadTaskParseTaskService<TDbContext>>();

        var currentAssemblyTypes = Assembly.GetExecutingAssembly().GetTypes();
        var dtp = currentAssemblyTypes.Where(s =>
                s.IsAssignableTo(SpecificTypeUtils<IDownloadTaskParser>.Type) &&
                s is {IsPublic: true, IsAbstract: false})
            .ToList();
        foreach (var et in dtp)
        {
            services.AddScoped(SpecificTypeUtils<IDownloadTaskParser>.Type, et);
        }

        return services;
    }

    public static DownloadTaskParseTaskDbModel ToDbModel(this DownloadTaskParseTask task)
    {
        return new DownloadTaskParseTaskDbModel
        {
            Id = task.Id,
            Source = task.Source,
            Link = task.Link,
            Title = task.Title,
            ParsedAt = task.ParsedAt,
            Items = task.Items != null ? JsonConvert.SerializeObject(task.Items) : null,
        };
    }

    public static DownloadTaskParseTask ToDomainModel(this DownloadTaskParseTaskDbModel dbModel)
    {
        return new DownloadTaskParseTask
        {
            Id = dbModel.Id,
            Source = dbModel.Source,
            Link = dbModel.Link,
            Title = dbModel.Title,
            ParsedAt = dbModel.ParsedAt,
            Items = dbModel.Items != null
                ? JsonConvert.DeserializeObject<List<DownloadTaskParseTask.Item>>(dbModel.Items)
                : null,
        };
    }
}