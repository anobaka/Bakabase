using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Models.Models.Aos
{
    /// <summary>
    /// todo: properties in this type are not defined clearly
    /// </summary>
    public class CoverDiscoverResult
    {
        public CoverDiscoverResultType Type { get; set; }

        /// <summary>
        /// LocalFile or Logical fullname.
        /// </summary>
        public string? FileFullname { get; set; }

        public string? FileRelativeName { get; set; }

        public string? KeyInSource { get; set; }

        public string? Extension
        {
            get
            {
                var f = LogicalFullname;
                return f.IsNotEmpty() ? Path.GetExtension(FileFullname) : null;
            }
        }

        public string? LogicalFullname => (Type switch
        {
            CoverDiscoverResultType.LocalFile => FileFullname,
            CoverDiscoverResultType.FromAdditionalSource => Path.Combine(FileFullname!, KeyInSource!), _ => null,
        })?.ToLower();

        public Stream? Cover { get; set; }

        public static CoverDiscoverResult FromLocalFile(string path, string coverFullname,
            bool populateCoverStream)
        {
            var relativeName = coverFullname.Replace(path, null, StringComparison.OrdinalIgnoreCase)
                .Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var r = new CoverDiscoverResult
            {
                Type = CoverDiscoverResultType.LocalFile,
                FileFullname = coverFullname,
                FileRelativeName = relativeName.IsNullOrEmpty() ? null : relativeName,
                Cover = populateCoverStream ? File.OpenRead(coverFullname) : null
            };

            return r;
        }

        public static CoverDiscoverResult FromAdditionalSource(string path, string sourceFullname,
            string keyInSource,
            MemoryStream cover)
        {
            var relativeName = sourceFullname.Replace(path, null, StringComparison.OrdinalIgnoreCase)
                .Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            relativeName = Path.Combine(relativeName, keyInSource);
            return new CoverDiscoverResult
            {
                Type = CoverDiscoverResultType.FromAdditionalSource,
                FileRelativeName = relativeName,
                FileFullname = sourceFullname,
                Cover = cover,
                KeyInSource = keyInSource
            };
        }

        public static CoverDiscoverResult FromIcon(Icon icon)
        {
            var ms = new MemoryStream();
            // Ico encoder is not found.
            icon.ToBitmap().Save(ms, ImageFormat.Png);
            icon.Dispose();
            ms.Seek(0, SeekOrigin.Begin);

            return new CoverDiscoverResult
            {
                Type = CoverDiscoverResultType.Icon,
                Cover = ms,
            };
        }
    }

    public enum CoverDiscoverResultType
    {
        LocalFile = 1,
        FromAdditionalSource = 2,
        Icon
    }
}