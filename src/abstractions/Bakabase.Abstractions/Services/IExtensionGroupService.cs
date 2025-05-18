using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Services;

public interface IExtensionGroupService
{
    Task<ExtensionGroup[]> GetAll();
    Task<ExtensionGroup> Get(int id);
    Task Add(ExtensionGroup group);
    Task Put(int id, ExtensionGroup group);
    Task Delete(int id);
}