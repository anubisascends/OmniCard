using System.Collections.Generic;

namespace OmniCard.Models;

public class TcgOrderImportPreview
{
    public List<TcgOrderImportRow> Rows { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
