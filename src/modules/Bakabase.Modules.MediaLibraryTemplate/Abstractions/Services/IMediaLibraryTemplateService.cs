namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Services;

public interface IMediaLibraryTemplateService
{
    Task GeneratePreview(int id);
    Task<Models.Domain.MediaLibraryTemplate> Get(int id);
    Task<Models.Domain.MediaLibraryTemplate[]> GetAll();
    Task Add(Models.Domain.MediaLibraryTemplate template);
    Task Put(int id, Models.Domain.MediaLibraryTemplate template);
    Task Delete(int id);
    Task<string> ExportToText(int id);
    Task<byte[]> AppendShareTextToPng(int id, byte[] png);
}