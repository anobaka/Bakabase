using System;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.Migrations;

namespace Bakabase.Migrations.V230;

/// <summary>
/// Previously migrated PlayableFilePaths to PlayableItems in resource cache.
/// No longer needed: PlayableItems column has been removed in favor of
/// filesystem-only cache (ResourceFileSystemCache).
/// </summary>
public class V230Migrator : AbstractMigrator
{
    public V230Migrator(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    protected override string ApplyOnVersionEqualsOrBeforeString => "2.3.0-beta";

    protected override Task MigrateAfterDbMigrationInternal(object? context)
    {
        return Task.CompletedTask;
    }
}
