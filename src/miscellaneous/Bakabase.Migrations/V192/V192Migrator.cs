using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Helpers;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Infrastructures.Components.App.Migrations;
using Bakabase.InsideWorld.Business;
using Bootstrap.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Migrations.V192;

public class V192Migrator : AbstractMigrator
{
    public V192Migrator(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    protected override string ApplyOnVersionEqualsOrBeforeString => "1.9.2-beta.25";
    protected override async Task MigrateAfterDbMigrationInternal(object context)
    {
        var dbCtx = GetRequiredService<InsideWorldDbContext>();
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