using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.SystemService;
using Bakabase.InsideWorld.Business.Components.Configurations.Extensions;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Extensions;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Extensions;
using Bootstrap.Components.Tasks;
using Bootstrap.Components.Tasks.Progressor.Abstractions;
using Bootstrap.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using NPOI.SS.Formula.Functions;
using Org.BouncyCastle.Asn1.Sec;
using Bakabase.Abstractions.Components.Configuration;
using Microsoft.EntityFrameworkCore.Storage.Json;

namespace Bakabase.InsideWorld.Business.Services;

public class MediaLibraryV2Service<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, MediaLibraryV2DbModel, int> orm,
    IResourceService resourceService,
    IPropertyService propertyService,
    FullMemoryCacheResourceService<TDbContext, ResourceCacheDbModel, int> cacheOrm,
    IBOptions<ResourceOptions> resourceOptions,
    BTaskManager btm,
    IBakabaseLocalizer localizer,
    IServiceProvider serviceProvider,
    ISystemService systemService
)
    : ScopedService(serviceProvider), IMediaLibraryV2Service where TDbContext : DbContext
{
    protected IMediaLibraryTemplateService TemplateService => GetRequiredService<IMediaLibraryTemplateService>();

    public async Task<MediaLibraryV2> Add(MediaLibraryV2AddOrPutInputModel model)
    {
        var domainModel = new MediaLibraryV2 { Paths = model.Paths, Name = model.Name, Color = model.Color };
        var dbModel = domainModel.ToDbModel();
        await orm.Add(dbModel);
        orm.DbContext.Detach(dbModel);
        domainModel.Id = dbModel.Id;
        return domainModel;
    }

    public async Task Put(int id, MediaLibraryV2AddOrPutInputModel model)
    {
        var data = await Get(id);
        data.Name = model.Name;
        data.Color = model.Color;
        data.Players = model.Players;
        if (!data.Paths.SequenceEqual(model.Paths))
        {
            data.Paths = model.Paths;
            data.SyncVersion = null;
        }

        await orm.Update(data.ToDbModel());
    }

    public async Task Put(IEnumerable<MediaLibraryV2> data)
    {
        await orm.UpdateRange(data.Select(d => d.ToDbModel()).ToList());
    }

    public async Task Patch(int id, MediaLibraryV2PatchInputModel model)
    {
        var data = await Get(id);

        if (model.SyncVersion.IsNotEmpty())
        {
            data.SyncVersion = model.SyncVersion;
        }

        if (model.Paths != null)
        {
            if (!data.Paths.SequenceEqual(model.Paths))
            {
                data.SyncVersion = null;
                data.Paths = model.Paths;
            }
        }

        if (model.TemplateId.HasValue)
        {
            data.TemplateId = model.TemplateId;
        }

        if (model.Name.IsNotEmpty())
        {
            data.Name = model.Name;
        }

        if (model.ResourceCount.HasValue)
        {
            data.ResourceCount = model.ResourceCount.Value;
        }

        if (model.Color != null)
        {
            data.Color = model.Color;
        }

        await orm.Update(data.ToDbModel());
    }

    public async Task RefreshResourceCount(int id)
    {
        var count = (await resourceService.GetAllGeneratedByMediaLibraryV2(new[] { id })).Length;
        await orm.UpdateByKey(id, d =>
        {
            d.ResourceCount = count;
        });
    }

    /// <summary>
    /// <inheritdoc cref="IMediaLibraryV2Service.ReplaceAll"/>
    /// </summary>
    /// <param name="models"></param>
    /// <returns></returns>
    public async Task ReplaceAll(MediaLibraryV2[] models)
    {
        var currentData = (await GetAll()).ToDictionary(d => d.Id, d => d);

        foreach (var m in models)
        {
            m.Paths = m.Paths.Select(p => p.StandardizePath()!).Distinct().ToList();
            if (currentData.TryGetValue(m.Id, out var current))
            {
                if (!current.Paths.SequenceEqual(m.Paths) || current.TemplateId != m.TemplateId)
                {
                    m.SyncVersion = null;
                }
            }
        }

        var newData = models.Where(x => x.Id == 0).ToArray();
        var dbData = models.Except(newData).ToArray();
        var ids = dbData.Select(x => x.Id).ToArray();
        await orm.RemoveAll(x => !ids.Contains(x.Id));
        await orm.AddRange(newData.Select(d => d.ToDbModel()).ToList());
        await orm.UpdateRange(dbData.Select(d => d.ToDbModel()).ToList());
    }

    protected async Task Populate(List<MediaLibraryV2> models,
        MediaLibraryV2AdditionalItem additionalItems = MediaLibraryV2AdditionalItem.None)
    {
        foreach (var ai in SpecificEnumUtils<MediaLibraryV2AdditionalItem>.Values)
        {
            if (additionalItems.HasFlag(ai))
            {
                switch (ai)
                {
                    case MediaLibraryV2AdditionalItem.None:
                        break;
                    case MediaLibraryV2AdditionalItem.Template:
                    {
                        var templateIds = models.Select(d => d.TemplateId).OfType<int>().ToHashSet();
                        var templates =
                            (await TemplateService.GetByKeys(templateIds.ToArray())).ToDictionary(d => d.Id);
                        foreach (var model in models)
                        {
                            if (model.TemplateId.HasValue)
                            {
                                model.Template = templates.GetValueOrDefault(model.TemplateId.Value);
                            }
                        }

                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
    }

    public async Task<MediaLibraryV2> Get(int id,
        MediaLibraryV2AdditionalItem additionalItems = MediaLibraryV2AdditionalItem.None)
    {
        var domainModel = (await orm.GetByKey(id)).ToDomainModel();
        await Populate([domainModel], additionalItems);
        return domainModel;
    }

    public Task<List<MediaLibraryV2>> GetByKeys(int[] ids,
        MediaLibraryV2AdditionalItem additionalItems = MediaLibraryV2AdditionalItem.None) =>
        GetAll(x => ids.Contains(x.Id), additionalItems);

    public async Task<List<MediaLibraryV2>> GetAll(Expression<Func<MediaLibraryV2DbModel, bool>>? filter = null,
        MediaLibraryV2AdditionalItem additionalItems = MediaLibraryV2AdditionalItem.None)
    {
        var data = (await orm.GetAll(filter)).Select(x => x.ToDomainModel()).ToList();
        await Populate(data, additionalItems);
        return data;
    }

    public async Task Delete(int id)
    {
        await orm.RemoveByKey(id);
    }

}