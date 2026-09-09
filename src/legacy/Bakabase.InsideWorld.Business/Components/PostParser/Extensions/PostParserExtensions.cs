using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
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
using Newtonsoft.Json.Linq;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Extensions;

public static class PostParserExtensions
{
    public static IServiceCollection AddPostParser<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddScoped<FullMemoryCacheResourceService<TDbContext, PostParserTaskDbModel, int>>();
        services.AddScoped<IPostParserTaskService, PostParserTaskService<TDbContext>>();
        services.AddSingleton<PostParserTaskTrigger>();

        // Every loaded assembly, not just this one: readers and extractors now come from the
        // acquisition side too, and a reader nobody scanned for is a reader that silently does not
        // exist. Assemblies that refuse to enumerate (native, dynamic) are skipped rather than
        // taking startup down.
        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return []; }
            })
            .ToList();

        foreach (var rt in Concrete<ISharedContentReader>(types))
        {
            services.AddScoped(SpecificTypeUtils<ISharedContentReader>.Type, rt);
        }

        foreach (var pt in Concrete<ISharedContentPurchaser>(types))
        {
            services.AddScoped(SpecificTypeUtils<ISharedContentPurchaser>.Type, pt);
        }

        foreach (var ht in Concrete<IPostParseTargetHandler>(types))
        {
            services.AddScoped(SpecificTypeUtils<IPostParseTargetHandler>.Type, ht);
        }

        services.AddScoped<SharedContentReaderResolver>();
        services.AddHttpClient(nameof(GenericHtmlReader));

        return services;
    }

    private static List<Type> Concrete<TContract>(IEnumerable<Type> types) =>
        types.Where(t => t.IsAssignableTo(SpecificTypeUtils<TContract>.Type) &&
                         t is {IsPublic: true, IsAbstract: false, IsInterface: false})
            .Distinct()
            .ToList();

    public static async Task ConfigurePostParser(this IApplicationBuilder app)
    {
        var trigger = app.ApplicationServices.GetRequiredService<PostParserTaskTrigger>();
        await trigger.Initialize();
    }

    public static PostParserTaskDbModel ToDbModel(this PostParserTask task)
    {
        string? resultsJson = null;
        if (task.Results != null)
        {
            var jResults = new JObject();
            foreach (var (target, dataNode) in task.Results)
            {
                jResults[target.ToString()] = dataNode != null
                    ? JToken.Parse(dataNode.ToJsonString())
                    : JValue.CreateNull();
            }

            resultsJson = jResults.ToString(Formatting.None);
        }

        return new PostParserTaskDbModel
        {
            Id = task.Id,
            Source = task.Source,
            Link = task.Link,
            Title = task.Title,
            Targets = task.Targets.Count > 0 ? JsonConvert.SerializeObject(task.Targets) : null,
            Results = resultsJson,
            Error = task.Error,
            IsDeleted = task.IsDeleted,
        };
    }

    public static PostParserTask ToDomainModel(this PostParserTaskDbModel dbModel)
    {
        List<PostParseTarget>? targets = null;
        if (dbModel.Targets != null)
        {
            try
            {
                targets = JsonConvert.DeserializeObject<List<PostParseTarget>>(dbModel.Targets);
            }
            catch (JsonException)
            {
            }
        }

        Dictionary<PostParseTarget, JsonNode?>? results = null;
        if (dbModel.Results != null)
        {
            try
            {
                var jObj = JObject.Parse(dbModel.Results);
                results = new Dictionary<PostParseTarget, JsonNode?>();
                foreach (var (key, value) in jObj)
                {
                    if (System.Enum.TryParse<PostParseTarget>(key, out var target))
                    {
                        results[target] = value is {Type: not JTokenType.Null}
                            ? JsonNode.Parse(value.ToString(Formatting.None))
                            : null;
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        var error = dbModel.Error;

        return new PostParserTask
        {
            Id = dbModel.Id,
            Source = dbModel.Source,
            Link = dbModel.Link,
            Title = dbModel.Title,
            Targets = targets ?? [],
            Results = results,
            Error = error,
            IsDeleted = dbModel.IsDeleted,
        };
    }
}
