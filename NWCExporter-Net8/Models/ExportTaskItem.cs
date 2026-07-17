using NWCExporter.Models;

namespace NWCExporter.Models
{
    public class ExportTaskItem
    {
        public RevitFileItem File { get; set; } = null!;
        public View3DItem? View { get; set; } 
    }
}