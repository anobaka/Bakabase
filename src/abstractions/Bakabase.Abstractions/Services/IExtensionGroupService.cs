using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Input;

namespace Bakabase.Abstractions.Services;

public interface IExtensionGroupService
{
    Task<ExtensionGroup[]> GetAll();
    Task<ExtensionGroup> Get(int id);
    Task Add(ExtensionGroupAddInputModel group);
    Task Put(int id, ExtensionGroupPutInputModel group);
    Task Delete(int id);
}