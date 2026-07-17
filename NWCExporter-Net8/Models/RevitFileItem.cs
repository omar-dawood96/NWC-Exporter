using System.IO;

namespace NWCExporter.Models
{
    public class RevitFileItem
    {
        public string FilePath   { get; set; } = string.Empty;
        public string FileName   => Path.GetFileName(FilePath);
        public bool   IsSelected { get; set; } = true;
    }
}
