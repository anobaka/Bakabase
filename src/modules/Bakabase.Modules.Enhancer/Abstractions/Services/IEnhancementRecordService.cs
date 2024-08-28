﻿using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Domain;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Modules.Enhancer.Abstractions.Services;

public interface IEnhancementRecordService
{
    Task<List<EnhancementRecord>>
        GetAll(Expression<Func<Bakabase.Abstractions.Models.Db.EnhancementRecord, bool>>? exp);

    Task Add(EnhancementRecord record);

    Task DeleteAll(Expression<Func<Bakabase.Abstractions.Models.Db.EnhancementRecord, bool>>? exp);
}