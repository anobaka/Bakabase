using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NJsonSchema;
using NJsonSchema.Generation;

namespace Bakabase.Abstractions.Models.Domain
{
    public record ComponentDescriptor
    {
        public ComponentDescriptorType Type { get; set; }
        public ComponentType ComponentType { get; set; }
        public string AssemblyQualifiedTypeName { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Message { get; set; }

        public string OptionsJson { get; set; }
        public int? OptionsId { get; set; }

        public string Version { get; set; }
        public string DataVersion { get; set; }

        private Type _optionsType;

        public Type OptionsType
        {
            get => _optionsType;
            set
            {
                _optionsType = value;
                if (value != null)
                {
                    OptionsJsonSchema = JsonSchema.FromType(value,
                        new JsonSchemaGeneratorSettings
                        {
                            SerializerSettings = new JsonSerializerSettings
                            { ContractResolver = new CamelCasePropertyNamesContractResolver() }
                        }).ToJson();
                }
            }
        }

        public string OptionsJsonSchema { get; private set; }

        /// <summary>
        /// Components with same <see cref="Id"/> should produce same results. 
        /// </summary>
        public string Id => Type switch
        {
            ComponentDescriptorType.Fixed => AssemblyQualifiedTypeName,
            ComponentDescriptorType.Instance => OptionsId!.ToString(),
            ComponentDescriptorType.Configurable => AssemblyQualifiedTypeName,
            ComponentDescriptorType.Invalid => null,
            _ => throw new ArgumentOutOfRangeException()
        };

        public bool IsInstanceable => Type switch
        {
            ComponentDescriptorType.Fixed => true,
            ComponentDescriptorType.Instance => true,
            ComponentDescriptorType.Configurable => false,
            ComponentDescriptorType.Invalid => false,
            _ => throw new ArgumentOutOfRangeException()
        };

        public BaseResponse BuildValidationResponse()
        {
            return Type == ComponentDescriptorType.Invalid
                ? BaseResponseBuilder.BuildBadRequest(Message)
                : BaseResponseBuilder.Ok;
        }

        public virtual ComponentDescriptor ToDto()
        {
            return new ComponentDescriptor
            {
                AssemblyQualifiedTypeName = AssemblyQualifiedTypeName,
                ComponentType = ComponentType,
                OptionsId = OptionsId,
                Description = Description,
                Name = Name,
                DataVersion = DataVersion,
                Message = Message,
                OptionsJson = OptionsJson,
                OptionsType = OptionsType,
                Type = Type,
                Version = Version
            };
        }
        public Category[]? AssociatedCategories { get; set; }
    }
}
