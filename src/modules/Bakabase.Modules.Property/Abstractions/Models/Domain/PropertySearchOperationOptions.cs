using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Property.Abstractions.Models.Domain;

public record PropertySearchOperationOptions(PropertyType AsType, Func<Bakabase.Abstractions.Models.Domain.Property, Bakabase.Abstractions.Models.Domain.Property>? ConvertProperty = null);