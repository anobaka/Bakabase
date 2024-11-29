using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Abstractions.Models;
using Bakabase.Modules.BulkModification.Components;
using Bakabase.Modules.BulkModification.Extensions;
using Bakabase.Modules.BulkModification.Models.Db;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bootstrap.Extensions;
using Newtonsoft.Json;
using static Aliyun.OSS.Model.LiveChannelStat;

namespace Bakabase.Service.Extensions;

public static class BulkModificationExtensions
{
    public static BulkModificationViewModel ToViewModel(this BulkModification domainModel,
        IPropertyLocalizer propertyLocalizer)
    {
        return new BulkModificationViewModel
        {
            Id = domainModel.Id,
            CreatedAt = domainModel.CreatedAt,
            FilteredResourceIds = domainModel.FilteredResourceIds,
            IsActive = domainModel.IsActive,
            Name = domainModel.Name,
            Filter = domainModel.Filter?.ToViewModel(propertyLocalizer),
            Processes = domainModel.Processes?.Select(p => p.ToViewModel(propertyLocalizer)).ToList(),
            Variables = domainModel.Variables?.Select(p => p.ToViewModel(propertyLocalizer)).ToList(),
        };
    }

    public static BulkModificationProcessViewModel ToViewModel(this BulkModificationProcess domainModel,
        IPropertyLocalizer propertyLocalizer)
    {
        return new BulkModificationProcessViewModel
        {
            Property = domainModel.Property.ToViewModel(propertyLocalizer),
            PropertyId = domainModel.PropertyId,
            PropertyPool = domainModel.PropertyPool,
            Steps = domainModel.Steps
        };
    }

    public static BulkModificationVariableViewModel ToViewModel(this BulkModificationVariable domainModel,
        IPropertyLocalizer propertyLocalizer)
    {
        return new BulkModificationVariableViewModel
        {
            Key = domainModel.Key,
            Name = domainModel.Name,
            Preprocesses = domainModel.Preprocesses,
            Scope = domainModel.Scope,
            Property = domainModel.Property.ToViewModel(propertyLocalizer),
            PropertyId = domainModel.PropertyId,
            PropertyPool = domainModel.PropertyPool,
        };
    }

    public static async Task<PatchBulkModification> ToDomainModel(this BulkModificationPatchInputModel inputModel,
        IPropertyService propertyService)
    {
        var propertyMap = (await propertyService.GetProperties(PropertyPool.All)).ToMap();

        return new PatchBulkModification
        {
            IsActive = inputModel.IsActive,
            Name = inputModel.Name,
            Processes = inputModel.Processes?.Select(p =>
            {
                var property = propertyMap.GetValueOrDefault(p.PropertyPool)?.GetValueOrDefault(p.PropertyId);
                if (property == null)
                {
                    return null;
                }

                return p.ToDomainModel(property);
            }).OfType<BulkModificationProcess>().ToList(),
            Variables = inputModel.Variables?.Select(p =>
            {
                var property = propertyMap.GetValueOrDefault(p.PropertyPool)?.GetValueOrDefault(p.PropertyId);
                if (property == null)
                {
                    return null;
                }

                return p.ToDomainModel(property);
            }).OfType<BulkModificationVariable>().ToList(),
            Filter = inputModel.Filter?.ToDomainModel(propertyMap)
        };
    }

    public static BulkModificationProcess ToDomainModel(this BulkModificationProcessInputModel inputModel,
        Property property)
    {
        var steps = inputModel.Steps.IsNullOrEmpty()
            ? null
            : inputModel.Steps.DeserializeAsBulkModificationProcessSteps(BulkModificationInternals
                .PropertyTypeProcessorDescriptorMap[property.Type].ProcessOptionsType);

        return new BulkModificationProcess
        {
            Property = property,
            PropertyId = inputModel.PropertyId,
            PropertyPool = inputModel.PropertyPool,
            Steps = steps
        };
    }

    public static BulkModificationVariable ToDomainModel(this BulkModificationVariableInputModel inputModel,
        Property property)
    {
        var preprocesses = inputModel.Preprocesses.IsNullOrEmpty()
            ? null
            : inputModel.Preprocesses.DeserializeAsBulkModificationProcessSteps(BulkModificationInternals
                .PropertyTypeProcessorDescriptorMap[property.Type].ProcessOptionsType);

        return new BulkModificationVariable
        {
            Property = property,
            PropertyId = inputModel.PropertyId,
            PropertyPool = inputModel.PropertyPool,
            Key = Guid.NewGuid().ToString("N"),
            Name = inputModel.Name,
            Scope = inputModel.Scope,
            Preprocesses = preprocesses
        };
    }

    public static async Task<List<BulkModificationDiffViewModel>> ToViewModels(
        this List<BulkModificationDiff> domainModels, IPropertyService propertyService, IPropertyLocalizer? propertyLocalizer = null)
    {

        var propertyPools = domainModels.SelectMany(y => y.Diffs.Select(z => z.PropertyPool)).Distinct()
            .Aggregate((ps, p) => ps | p);
        var propertyMap = (await propertyService.GetProperties(propertyPools)).ToMap();

        var viewModels = domainModels.Select(dbModel => new BulkModificationDiffViewModel
        {
            Id = dbModel.Id,
            BulkModificationId = dbModel.BulkModificationId,
            ResourceId = dbModel.ResourceId,
            ResourcePath = dbModel.ResourcePath,
            Diffs = dbModel.Diffs.Select(x =>
            {
                var property = propertyMap.GetValueOrDefault(x.PropertyPool)?.GetValueOrDefault(x.PropertyId);
                return property == null ? null : x.ToViewModel(property, propertyLocalizer);
            }).OfType<ResourceDiffViewModel>().ToList()
        }).ToList();

        return viewModels;
    }
}