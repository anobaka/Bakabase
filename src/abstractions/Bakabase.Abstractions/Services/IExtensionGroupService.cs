using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Input;

namespace Bakabase.Abstractions.Services;

public interface IExtensionGroupService
{
    Task<ExtensionGroup[]> GetAll();
    Task<ExtensionGroup> Get(int id);
    Task<ExtensionGroup> Add(ExtensionGroupAddInputModel group);
    Task<ExtensionGroup[]> AddRange(ExtensionGroupAddInputModel[] groups);
    Task Put(int id, ExtensionGroupPutInputModel group);
    Task Delete(int id);
}