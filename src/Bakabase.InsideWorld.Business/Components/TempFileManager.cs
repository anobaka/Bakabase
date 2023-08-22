using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Extensions;
using Bootstrap.Components.Storage;
using Microsoft.AspNetCore.Hosting;
using SixLabors.ImageSharp;

namespace Bakabase.InsideWorld.Business.Components
{
    public class TempFileManager
    {
        private readonly string _coverBaseDirectory;

        public TempFileManager(IWebHostEnvironment env)
        {
            _coverBaseDirectory = Path.Combine(env.WebRootPath, "cover").StandardizePath()!;
        }

        public async Task<string?> GetCover(int resourceId)
        {
            var dir = Path.Combine(_coverBaseDirectory, resourceId.ToString());
            if (Directory.Exists(dir))
            {
                var file = Directory.GetFiles(dir, "cover.*", SearchOption.TopDirectoryOnly).FirstOrDefault();
                return file;
            }

            return null;
        }

        public async Task<string> SaveCover(int resourceId, Stream data, CancellationToken ct)
        {
            var dir = Path.Combine(_coverBaseDirectory, resourceId.ToString());
            Directory.CreateDirectory(dir);
            var files = Directory.GetFiles(dir, "cover.*", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                FileUtils.Delete(file, true, true);
            }

            var filePath = Path.Combine(dir, "cover.png");
            using var image = await Image.LoadAsync(data, ct);
            await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await image.SaveAsPngAsync(fs, ct);

            return filePath;
        }
    }
}