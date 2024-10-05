using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Property.Abstractions.Services;

public interface IPropertyService
{
    Task<Bakabase.Abstractions.Models.Domain.Property> GetProperty(PropertyPool pool, int id);
    Task<List<Bakabase.Abstractions.Models.Domain.Property>> GetProperties(PropertyPool pool);
}