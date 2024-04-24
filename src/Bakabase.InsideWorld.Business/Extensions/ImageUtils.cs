using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;

namespace Bakabase.InsideWorld.Business.Extensions
{
    public static class ImageUtils
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