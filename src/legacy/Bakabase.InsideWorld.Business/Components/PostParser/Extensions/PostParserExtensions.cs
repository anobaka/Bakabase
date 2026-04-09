using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.InsideWorld.Business.Components.PostParser.Handlers;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Extensions;

public static class PostParserExtensions
{
    public static IServiceCollection AddPostParser<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddScoped<FullMemoryCacheResourceService<TDbContext, PostParserTaskDbModel, int>>();
        services.AddScoped<IPostParserTaskService, PostParserTaskService<TDbContext>>();
        services.AddSingleton<PostParserTaskTrigger>();

        var currentAssemblyTypes = Assembly.GetExecutingAssembly().GetTypes();

        var fetcherTypes = currentAssemblyTypes.Where(s =>
                s.IsAssignableTo(SpecificTypeUtils<IPostContentFetcher>.Type) &&
                s is {IsPublic: true, IsAbstract: false})
            .ToList();
        foreach (var ft in fetcherTypes)
        {
            services.AddScoped(SpecificTypeUtils<IPostContentFetcher>.Type, ft);
        }

        var handlerTypes = currentAssemblyTypes.Where(s =>
                s.IsAssignableTo(SpecificTypeUtils<IPostParseTargetHandler>.Type) &&
                s is {IsPublic: true, IsAbstract: false})
            .ToList();
        foreach (var ht in handlerTypes)
        {
            services.AddScoped(SpecificTypeUtils<IPostParseTargetHandler>.Type, ht);
        }

        return services;
    }

    public static async Task ConfigurePostParser(this IApplicationBuilder app)
    {
        var trigger = app.ApplicationServices.GetRequiredService<PostParserTaskTrigger>();
        await trigger.Initialize();
    }

    public static PostParserTaskDbModel ToDbModel(this PostParserTask task)
    {
        return new PostParserTaskDbModel
        {
            Id = task.Id,
            Source = task.Source,
            Link = task.Link,
            Title = task.Title,
            Targets = task.Targets.Count > 0 ? JsonConvert.SerializeObject(task.Targets) : null,
            Results = task.Results != null ? JsonConvert.SerializeObject(task.Results) : null,
            IsDeleted = task.IsDeleted,
        };
    }

    public static PostParserTask ToDomainModel(this PostParserTaskDbModel dbModel)
    {
        return new PostParserTask
        {
            Id = dbModel.Id,
            Source = dbModel.Source,
            Link = dbModel.Link,
            Title = dbModel.Title,
            Targets = dbModel.Targets != null
                ? JsonConvert.DeserializeObject<List<PostParseTarget>>(dbModel.Targets)!
                : [],
            Results = dbModel.Results != null
                ? JsonConvert.DeserializeObject<Dictionary<PostParseTarget, PostParseTargetResult>>(dbModel.Results)
                : null,
            IsDeleted = dbModel.IsDeleted,
        };
    }
}
