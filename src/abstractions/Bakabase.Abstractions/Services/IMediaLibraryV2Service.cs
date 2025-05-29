using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;

namespace Bakabase.Abstractions.Services;

public interface IMediaLibraryV2Service
{
    Task Add(MediaLibraryV2AddOrPutInputModel model);
    Task Put(int id, MediaLibraryV2AddOrPutInputModel model);
    Task SaveAll(MediaLibraryV2[] models);
    Task<MediaLibraryV2> Get(int id);
    Task<List<MediaLibraryV2>> GetAll(MediaLibraryV2AdditionalItem additionalItems = MediaLibraryV2AdditionalItem.None);
    Task Delete(int id);
    Task Sync(int id);
    Task SyncAll();
}