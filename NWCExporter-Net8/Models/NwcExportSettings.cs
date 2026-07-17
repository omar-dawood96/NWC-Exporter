namespace NWCExporter.Models
{
    public class NwcExportSettings
    {
        public bool ConvertConstructionParts { get; set; } = false;
        public bool ConvertElementIds { get; set; } = true;
        public string ConvertElementParameters { get; set; } = "All";
        public bool ConvertElementProperties { get; set; } = false;
        public bool ConvertLights { get; set; } = false;
        public bool ConvertLinkedCadFormats { get; set; } = true;
        public bool ConvertLinkedFiles { get; set; } = false;
        public bool ConvertRoomAsAttribute { get; set; } = true;
        public bool ConvertUrls { get; set; } = true;
        public string Coordinates { get; set; } = "Shared";
        public bool DivideFileIntoLevels { get; set; } = true;
        public bool EmbedTextures { get; set; } = true;
        public bool ExportRoomGeometry { get; set; } = true;
        public double FacetingFactor { get; set; } = 1.0;
        public bool SeparateCustomProperties { get; set; } = true;
        public bool StrictSectioning { get; set; } = false;
        public bool TryFindMissingMaterials { get; set; } = true;
        public bool TypePropertiesOnElements { get; set; } = false;
        public bool CreateFolderPerProject { get; set; } = false;
    }
}
