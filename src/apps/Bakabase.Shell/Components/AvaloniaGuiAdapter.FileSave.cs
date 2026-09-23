using System.Text;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Bakabase.Abstractions.Components.Gui;

namespace Bakabase.Shell.Components;

public partial class AvaloniaGuiAdapter : ILocalFileSaveDialog
{
    private readonly SemaphoreSlim _saveDialogGate = new(1, 1);

    public async Task<LocalFileSaveOutcome> SaveTextFileAsync(string suggestedFileName, string text,
        CancellationToken cancellationToken = default)
    {
        // A double click or another local tab must not stack native modal dialogs.
        if (!await _saveDialogGate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("A file save dialog is already open.");
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_mainWindow is not { IsVisible: true } || !_mainWindow.StorageProvider.CanSave)
                    return LocalFileSaveOutcome.Unavailable;
                var file = await _mainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    SuggestedFileName = suggestedFileName,
                    DefaultExtension = "json",
                    ShowOverwritePrompt = true,
                    FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
                });
                if (file == null) return LocalFileSaveOutcome.Cancelled;
                using (file)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await using var stream = await file.OpenWriteAsync();
                    if (stream.CanSeek) stream.SetLength(0);
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
                return LocalFileSaveOutcome.Saved;
            });
        }
        finally { _saveDialogGate.Release(); }
    }
}
