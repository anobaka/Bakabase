using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Parsers;
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
        var dtp = currentAssemblyTypes.Where(s =>
                s.IsAssignableTo(SpecificTypeUtils<IPostParser>.Type) &&
                s is {IsPublic: true, IsAbstract: false})
            .ToList();
        foreach (var et in dtp)
        {
            services.AddScoped(SpecificTypeUtils<IPostParser>.Type, et);
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
            ParsedAt = task.ParsedAt,
            Items = task.Items != null ? JsonConvert.SerializeObject(task.Items) : null,
            Error = task.Error
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
            ParsedAt = dbModel.ParsedAt,
            Items = dbModel.Items != null
                ? JsonConvert.DeserializeObject<List<PostParserTask.Item>>(dbModel.Items)
                : null,
            Error = dbModel.Error
        };
    }
}