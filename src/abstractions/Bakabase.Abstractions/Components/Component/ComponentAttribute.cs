using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Abstractions.Components.Component
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public abstract class ComponentAttribute : Attribute
    {
        public abstract ComponentType Type { get; }
        public string? Description { get; set; }
        public string Name { get; set; } = null!;
        public Type? OptionsType { get; set; }

        /// <summary>
        /// Usually a <see cref="Version"/> is set according to the static codes, and its default value is 1.0.0.
        /// </summary>
        public string Version { get; set; } = "1.0.0";

        /// <summary>
        /// <para>
        /// A data version should be changed after dynamic options changing. If this type is not set, a default data version generator will be used.
        /// </para>
        /// <para>
        /// This type must inherit from <see cref="IComponentDataVersionGenerator"/> and its instances can be got from global <see cref="IServiceProvider"/>
        /// </para>
        /// </summary>
        public Type? DataVersionGeneratorType { get; set; }

        public virtual ComponentDescriptor ToDescriptor(Type type)
        {
            return new ComponentDescriptor
            {
                AssemblyQualifiedTypeName = type.AssemblyQualifiedName!,
                Name = Name ?? type.Name,
                OptionsType = OptionsType,
                ComponentType = Type,
                Description = Description,
                Version = Version
            };
        }
    }

    public interface IComponentDataVersionGenerator
    {
        Task<string> GetVersionAsync(ComponentDescriptor descriptor);
    }
}