using System.Collections.Generic;

namespace NWCExporter.Models
{
    public class ExportResult
    {
        public int SuccessCount { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }
}