using System.Reflection;
using Bakabase.Infrastructures.Components.App.Migrations;
using Bakabase.Migrations.V190;
using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Migrations
{
    public static class Extensions
    {
        public static void AddInsideWorldMigrations(this IServiceCollection services)
        {
            var migrators = Assembly.GetExecutingAssembly().GetTypes().Where(a =>
                a is {IsClass: true, IsAbstract: false} && a.IsAssignableTo(SpecificTypeUtils<IMigrator>.Type));
            foreach (var m in migrators)
            {
                services.AddTransient(SpecificTypeUtils<IMigrator>.Type, m);
            }

            services.AddTransient<V190MigrationLocalizer>();
        }
    }
}