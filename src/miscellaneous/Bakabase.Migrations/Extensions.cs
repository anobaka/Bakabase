using System.Reflection;
using Bakabase.Infrastructures.Components.App.Migrations;
using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Migrations
{
    public static class Extensions
    {
        public static void AddBakabaseMigrations(this IServiceCollection services)
        {
            var migrators = Assembly.GetExecutingAssembly().GetTypes().Where(a =>
                a is {IsClass: true, IsAbstract: false} && a.IsAssignableTo(SpecificTypeUtils<IMigrator>.Type));
            foreach (var m in migrators)
            {
                services.AddScoped(SpecificTypeUtils<IMigrator>.Type, m);
            }
        }
    }
}