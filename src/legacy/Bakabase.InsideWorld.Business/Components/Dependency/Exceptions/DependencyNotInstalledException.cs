using System;
using Bakabase.Abstractions.Exceptions;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Exceptions
{
    public class DependencyNotInstalledException : Exception, IUserActionableException
    {
        public string DependencyName { get; }
        public string DependencyDisplayName { get; }
        public string? ComponentId { get; }

        public DependencyNotInstalledException(string dependencyName, string dependencyDisplayName, string message, string? componentId = null)
            : base(message)
        {
            DependencyName = dependencyName;
            DependencyDisplayName = dependencyDisplayName;
            ComponentId = componentId;
        }
    }
}
