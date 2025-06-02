using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;

namespace Bakabase.Abstractions.Services;

public interface IMediaLibraryTemplateService
{
    Task GeneratePreview(int id);
    Task<Models.Domain.MediaLibraryTemplate> Get(int id);
    Task<Models.Domain.MediaLibraryTemplate[]> GetByKeys(int[] ids);
    Task<Models.Domain.MediaLibraryTemplate[]> GetAll();
    Task<MediaLibraryTemplate> Add(MediaLibraryTemplateAddInputModel model);
    Task Put(int id, Models.Domain.MediaLibraryTemplate template);
    Task Delete(int id);
    Task<string> GenerateShareCode(int id);
    Task Import(MediaLibraryTemplateImportInputModel model);
    Task<MediaLibraryTemplateValidationViewModel?> Validate(string shareCode);
    Task<byte[]> AppendShareCodeToPng(int id, byte[] png);
    Task AddByMediaLibraryV1(int v1Id, int pcIdx, string templateName);
    Task Duplicate(int id);
}