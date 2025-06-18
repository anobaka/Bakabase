using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;

namespace Bakabase.Abstractions.Services;

public interface IMediaLibraryTemplateService
{
    Task GeneratePreview(int id);
    Task<MediaLibraryTemplate> Get(int id);
    Task<MediaLibraryTemplate[]> GetByKeys(int[] ids);
    Task<MediaLibraryTemplate[]> GetAll();
    Task<MediaLibraryTemplate> Add(MediaLibraryTemplateAddInputModel model);
    Task Put(int id, MediaLibraryTemplate template);
    Task Delete(int id);
    Task<string> GenerateShareCode(int id);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="model"></param>
    /// <returns>Media library template id</returns>
    Task<int> Import(MediaLibraryTemplateImportInputModel model);

    Task<MediaLibraryTemplateImportConfigurationViewModel> GetImportConfiguration(string shareCode);
    Task<byte[]> AppendShareCodeToPng(int id, byte[] png);
    Task AddByMediaLibraryV1(int v1Id, int pcIdx, string templateName);
    Task Duplicate(int id);
}