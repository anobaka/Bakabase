using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.InsideWorld.Models.Configs;
using Bootstrap.Components.Configuration;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.Configurations
{
    public class BakabaseOptionsManagerPool
    {
        public Dictionary<Type, object> AllOptionsManagers { get; }

        public BakabaseOptionsManagerPool(IServiceProvider serviceProvider)
        {
            AllOptionsManagers = new Dictionary<Type, object>();
            var allManagers = serviceProvider.GetServices(typeof(IBOptionsManagerInternal));
            foreach (var mgr in allManagers)
            {
                var iface = mgr!.GetType().GetInterfaces()
                    .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IBOptionsManager<>));
                var optionType = iface.GenericTypeArguments[0];
                AllOptionsManagers[optionType] = mgr;
            }
        }

        public IBOptionsManager<T> Get<T>() where T : class, new()
        {
            if (AllOptionsManagers.TryGetValue(typeof(T), out var mgr))
            {
                return (IBOptionsManager<T>)mgr;
            }
            throw new InvalidOperationException($"No IBOptionsManager<{typeof(T).Name}> registered.");
        }
    }
}