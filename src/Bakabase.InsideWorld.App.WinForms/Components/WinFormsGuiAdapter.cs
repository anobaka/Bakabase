using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.InsideWorld.Business.Components;

namespace Bakabase.InsideWorld.App.WinForms.Components
{
    public class WinFormsGuiAdapter : IGuiAdapter
    {
        private readonly MainWindow _mainWindow;

        public WinFormsGuiAdapter(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public Task<string[]?> OpenFilesSelector()
        {
            string[]? files = null;
            _mainWindow.Invoke(() =>
            {
                using var dialog = new OpenFileDialog();
                dialog.Multiselect = true;
                var result = dialog.ShowDialog();
                if (result == DialogResult.OK)
                {
                    files = dialog.FileNames;
                }
            });
            return Task.FromResult(files);
        }

        public Task<string?> OpenFileSelector()
        {
            string? file = null;
            _mainWindow.Invoke(() =>
            {
                using var dialog = new OpenFileDialog();
                var result = dialog.ShowDialog();
                if (result == DialogResult.OK)
                {
                    file = dialog.FileName;
                }
            });
            return Task.FromResult(file);
        }

        public Task<string?> OpenFolderSelector()
        {
            string? folder = null;
            _mainWindow.Invoke(() =>
            {
                using var dialog = new FolderBrowserDialog();
                var result = dialog.ShowDialog();
                if (result == DialogResult.OK)
                {
                    folder = dialog.SelectedPath;
                }
            });
            return Task.FromResult(folder);
        }

        public void SetTrayText(string text)
        {
            if (_mainWindow.TrayIcon != null)
            {
                _mainWindow.TrayIcon.Text = text;
            }
        }

        public void SetTrayIcon(Icon icon)
        {
            if (_mainWindow.TrayIcon != null)
            {
                _mainWindow.TrayIcon.Icon = icon;
            }
        }

        public void SetInitialProcessName(string name)
        {
            
        }
    }
}