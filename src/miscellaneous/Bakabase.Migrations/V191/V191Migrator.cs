using Bakabase.Abstractions.Helpers;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Infrastructures.Components.App.Migrations;
using Bakabase.InsideWorld.Business;
using Bootstrap.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Migrations.V191;

public class V191Migrator : AbstractMigrator
{
    public V191Migrator(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    protected override string ApplyOnVersionEqualsOrBeforeString => "1.9.1-beta2";

    private static readonly HashSet<PropertyValueScope> BadIntroductionValueScopes =
    [
        PropertyValueScope.DLsite,
        PropertyValueScope.Bangumi,
        PropertyValueScope.ExHentai
    ];

    protected override async Task MigrateAfterDbMigrationInternal(object context)
    {
        var dbCtx = GetRequiredService<BakabaseDbContext>();
        var reservedPropertyValues = await dbCtx.ReservedPropertyValues.ToListAsync();
        var optimizedValues = new List<ReservedPropertyValue>();

        foreach (var reservedPropertyValue in reservedPropertyValues)
        {
            if (BadIntroductionValueScopes.Contains((PropertyValueScope) reservedPropertyValue.Scope))
            {
                if (reservedPropertyValue.Introduction.IsNotEmpty())
                {
                    var optimizedHtml = StringHelpers.MinifyHtml(reservedPropertyValue.Introduction);
                    if (optimizedHtml != reservedPropertyValue.Introduction)
                    {
                        reservedPropertyValue.Introduction = optimizedHtml;
                        optimizedValues.Add(reservedPropertyValue);
                    }
                }
            }
        }

        if (optimizedValues.Any())
        {
            await dbCtx.SaveChangesAsync();
            Logger.LogInformation($"Optimized {optimizedValues.Count} introduction values");
        }
    }
}