using NWCExporter.Models;

namespace NWCExporter.ViewModels
{
    public class SettingsViewModel : BaseViewModel
    {
        // =====================================================================
        // Fields
        // =====================================================================

        private readonly NwcExportSettings _model;

        // =====================================================================
        // Constructor
        // =====================================================================

        public SettingsViewModel(NwcExportSettings model) => _model = model;

        // =====================================================================
        // Properties — Convert Options
        // =====================================================================

        public bool ConvertConstructionParts
        {
            get => _model.ConvertConstructionParts;
            set { _model.ConvertConstructionParts = value; OnPropertyChanged(); }
        }

        public bool ConvertElementIds
        {
            get => _model.ConvertElementIds;
            set { _model.ConvertElementIds = value; OnPropertyChanged(); }
        }

        public string ConvertElementParameters
        {
            get => _model.ConvertElementParameters;
            set { _model.ConvertElementParameters = value; OnPropertyChanged(); }
        }

        public bool ConvertElementProperties
        {
            get => _model.ConvertElementProperties;
            set { _model.ConvertElementProperties = value; OnPropertyChanged(); }
        }

        public bool ConvertLights
        {
            get => _model.ConvertLights;
            set { _model.ConvertLights = value; OnPropertyChanged(); }
        }

        public bool ConvertLinkedCadFormats
        {
            get => _model.ConvertLinkedCadFormats;
            set { _model.ConvertLinkedCadFormats = value; OnPropertyChanged(); }
        }

        public bool ConvertLinkedFiles
        {
            get => _model.ConvertLinkedFiles;
            set { _model.ConvertLinkedFiles = value; OnPropertyChanged(); }
        }

        public bool ConvertRoomAsAttribute
        {
            get => _model.ConvertRoomAsAttribute;
            set { _model.ConvertRoomAsAttribute = value; OnPropertyChanged(); }
        }

        public bool ConvertUrls
        {
            get => _model.ConvertUrls;
            set { _model.ConvertUrls = value; OnPropertyChanged(); }
        }

        // =====================================================================
        // Properties — Export Options
        // =====================================================================

        public string Coordinates
        {
            get => _model.Coordinates;
            set { _model.Coordinates = value; OnPropertyChanged(); }
        }

        public bool DivideFileIntoLevels
        {
            get => _model.DivideFileIntoLevels;
            set { _model.DivideFileIntoLevels = value; OnPropertyChanged(); }
        }

        public bool EmbedTextures
        {
            get => _model.EmbedTextures;
            set { _model.EmbedTextures = value; OnPropertyChanged(); }
        }

        public bool ExportRoomGeometry
        {
            get => _model.ExportRoomGeometry;
            set { _model.ExportRoomGeometry = value; OnPropertyChanged(); }
        }

        public double FacetingFactor
        {
            get => _model.FacetingFactor;
            set { _model.FacetingFactor = value; OnPropertyChanged(); }
        }

        public bool SeparateCustomProperties
        {
            get => _model.SeparateCustomProperties;
            set { _model.SeparateCustomProperties = value; OnPropertyChanged(); }
        }

        public bool StrictSectioning
        {
            get => _model.StrictSectioning;
            set { _model.StrictSectioning = value; OnPropertyChanged(); }
        }

        public bool TryFindMissingMaterials
        {
            get => _model.TryFindMissingMaterials;
            set { _model.TryFindMissingMaterials = value; OnPropertyChanged(); }
        }

        public bool TypePropertiesOnElements
        {
            get => _model.TypePropertiesOnElements;
            set { _model.TypePropertiesOnElements = value; OnPropertyChanged(); }
        }

        // =====================================================================
        // Properties — Output Options
        // =====================================================================

       

        public bool CreateFolderPerProject
        {
            get => _model.CreateFolderPerProject;
            set { _model.CreateFolderPerProject = value; OnPropertyChanged(); }
        }

        // =====================================================================
        // Properties — Static Options
        // =====================================================================

        public static string[] ParameterOptions => new[] { "All", "None", "Elements" };
        public static string[] CoordinateOptions => new[] { "Shared", "Internal" };

        // =====================================================================
        // Methods
        // =====================================================================

        public void ResetDefaults()
        {
            var d = new NwcExportSettings();

            ConvertConstructionParts = d.ConvertConstructionParts;
            ConvertElementIds = d.ConvertElementIds;
            ConvertElementParameters = d.ConvertElementParameters;
            ConvertElementProperties = d.ConvertElementProperties;
            ConvertLights = d.ConvertLights;
            ConvertLinkedCadFormats = d.ConvertLinkedCadFormats;
            ConvertLinkedFiles = d.ConvertLinkedFiles;
            ConvertRoomAsAttribute = d.ConvertRoomAsAttribute;
            ConvertUrls = d.ConvertUrls;
            Coordinates = d.Coordinates;
            DivideFileIntoLevels = d.DivideFileIntoLevels;
            EmbedTextures = d.EmbedTextures;
            ExportRoomGeometry = d.ExportRoomGeometry;
            FacetingFactor = d.FacetingFactor;
            SeparateCustomProperties = d.SeparateCustomProperties;
            StrictSectioning = d.StrictSectioning;
            TryFindMissingMaterials = d.TryFindMissingMaterials;
            TypePropertiesOnElements = d.TypePropertiesOnElements;
            CreateFolderPerProject = d.CreateFolderPerProject;
        }
    }
}