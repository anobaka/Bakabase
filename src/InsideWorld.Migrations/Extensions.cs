using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.Migrations;
using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InsideWorld.Migrations
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
        }
    }
}