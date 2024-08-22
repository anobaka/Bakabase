using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Bakabase.Abstractions.Components.Configuration;

namespace Bakabase.InsideWorld.Business.Helpers
{
    public static class ImageHelpers
    {
        public static Stream OpenAsImage(string path)
        {
            if (Path.GetExtension(path).Equals(InternalOptions.IcoFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                var bitmap = new Icon(File.OpenRead(path)).ToBitmap();
                var ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Jpeg);
                ms.Seek(0, SeekOrigin.Begin);
                return ms;
            }

            return File.OpenRead(path);
        }
    }
}