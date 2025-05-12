namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Services;

public interface IMediaLibraryTemplateService
{
    Task GeneratePreview(int id);

    Task Add(Models.MediaLibraryTemplate template);

    Task Put(int id, Models.MediaLibraryTemplate template);

    Task Delete(int id);

    Task<string> ExportToText(int id);
    Task<byte[]> ExportToPng(int id);
}