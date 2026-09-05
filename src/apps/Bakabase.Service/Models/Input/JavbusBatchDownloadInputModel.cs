using System.Collections.Generic;

namespace Bakabase.Service.Models.Input;

public class JavbusBatchDownloadInputModel
{
    /// <summary>Product codes to look up. Blanks and duplicates are dropped server-side.</summary>
    public List<string>? Codes { get; set; }
}
