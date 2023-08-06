using Bakabase.Infrastructures.Components.App.Migrations;
using Bakabase.InsideWorld.Business.Services;

namespace InsideWorld.Migrations
{
    internal sealed class V161Migrator : AbstractMigrator
    {
        public V161Migrator(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        protected override string MaxVersionString => "1.6.0";

        protected override async Task<object?> MigrateBeforeDbMigrationInternal()
        {
            var resourceTagMappingService = GetRequiredService<ResourceTagMappingService>();
            await resourceTagMappingService.RemoveDuplicate();
            return null;
        }
    }
}