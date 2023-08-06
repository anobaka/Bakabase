using Bakabase.InsideWorld.Business.Components;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Configurations;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bootstrap.Components.Configuration;
using Bootstrap.Extensions;
using Humanizer;
using Microsoft.AspNetCore.SignalR;

namespace Bakabase.InsideWorld.Business.Configurations
{
    public class WebGuiHubConfigurationAdapter
    {
        private readonly ILogger<WebGuiHubConfigurationAdapter> _logger;
        private readonly InsideWorldOptionsManagerPool _optionsManagerPool;
        private readonly IHubContext<WebGuiHub, IWebGuiClient> _hub;

        public WebGuiHubConfigurationAdapter(ILogger<WebGuiHubConfigurationAdapter> logger,
            InsideWorldOptionsManagerPool optionsManagerPool, IHubContext<WebGuiHub, IWebGuiClient> hub)
        {
            _logger = logger;
            _optionsManagerPool = optionsManagerPool;
            _hub = hub;
        }

        private void OnChange<T>(T options) where T : class, new()
        {
            _logger.LogInformation($"Sending new options {SpecificTypeUtils<T>.Type.Name}");
            _ = _hub.Clients.All.OptionsChanged(SpecificTypeUtils<T>.Type.Name.Camelize(), options);
        }

        public void Initialize()
        {
            foreach (var (optionsType, optionsManagerObj) in _optionsManagerPool.AllOptionsManagers)
            {
                var genericType = typeof(AspNetCoreOptionsManager<>).MakeGenericType(optionsType);
                var onChangeMethod = genericType.GetMethods().FirstOrDefault(a =>
                {
                    if (a.Name == nameof(AspNetCoreOptionsManager<AppOptions>.OnChange) &&
                        a.ReturnType == SpecificTypeUtils<IDisposable>.Type)
                    {
                        var parameterType = a.GetParameters().FirstOrDefault()?.ParameterType;
                        if (parameterType != null)
                        {
                            var genericType = parameterType.GetGenericTypeDefinition();
                            if (genericType == typeof(Action<>))
                            {
                                return true;
                            }
                        }
                    }

                    return false;
                })!;

                Action<AppOptions> ac = OnChange;

                var methodInfo = SpecificTypeUtils<WebGuiHubConfigurationAdapter>.Type
                    .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(a => a.Name == nameof(OnChange))!
                    .MakeGenericMethod(optionsType);
                var actionType = typeof(Action<>).MakeGenericType(optionsType);
                var action = Delegate.CreateDelegate(actionType, this, methodInfo);

                var obj = onChangeMethod.Invoke(optionsManagerObj, new object[] {action});

                _logger.LogInformation(
                    $"{optionsType.Name} change listener for {nameof(WebGuiHub)} has been registered.");
            }
        }
    }
}