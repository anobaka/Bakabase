using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Helpers;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Infrastructures.Components.App.Migrations;
using Bakabase.InsideWorld.Business;
using Bootstrap.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Bakabase.Abstractions.Services;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Input;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector.Infrastructures;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.Extensions.Options;
using Bakabase.InsideWorld.Models.Configs;

namespace Bakabase.Migrations.V200;

public class V200Migrator : AbstractMigrator
{
    public V200Migrator(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    protected override string ApplyOnVersionEqualsOrBeforeString => "2.0.0-beta";
    protected override async Task MigrateAfterDbMigrationInternal(object context)
    {
        var dbCtx = GetRequiredService<BakabaseDbContext>();

        // 1) Enhancement migration: fill new Key field
        var enhancements = await dbCtx.Enhancements.ToListAsync();
        foreach (var enhancement in enhancements)
        {
            enhancement.FillKey();
        }

        if (enhancements.Any())
        {
            await dbCtx.SaveChangesAsync();
            Logger.LogInformation($"Migrated {enhancements.Count} enhancements.");
        }
    }
}