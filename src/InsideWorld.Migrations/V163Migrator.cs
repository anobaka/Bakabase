using Bakabase.Infrastructures.Components.App.Migrations;
using Bakabase.InsideWorld.Business.Services;

namespace InsideWorld.Migrations
{
    /// <summary>
    /// before 1.6.3
    /// <br>1. tag.name and tagGroup.name is nullable and tag.name has no unique indexes.</br>
    /// <br>2. alias.name is not unique.</br>
    /// <br>so, we'll remove invalid tags and aliases.</br>
    /// </summary>
    internal sealed class V163Migrator : AbstractMigrator
    {
        public V163Migrator(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        protected override string MaxVersionString => "1.6.2";

        protected override async Task<object?> MigrateBeforeDbMigrationInternal()
        {
            var tagService = GetRequiredService<TagService>();
            await tagService.RemoveInvalid();

            var aliasService = GetRequiredService<AliasService>();
            await aliasService.RemoveInvalid();

            return null;
        }
    }
}