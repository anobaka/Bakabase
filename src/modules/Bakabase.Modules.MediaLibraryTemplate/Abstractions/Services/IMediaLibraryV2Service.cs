using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Input;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Services;

public interface IMediaLibraryV2Service
{
    Task Add(MediaLibraryV2AddOrPutInputModel model);
    Task Put(int id, MediaLibraryV2AddOrPutInputModel model);
    Task SaveAll(MediaLibraryV2[] models);
    Task<MediaLibraryV2> Get(int id);
    Task<List<MediaLibraryV2>> GetAll();
    Task Delete(int id);
}