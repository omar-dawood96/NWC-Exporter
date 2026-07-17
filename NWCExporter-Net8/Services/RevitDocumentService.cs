using Autodesk.Revit.DB;
using NWCExporter.Models;
using System.Collections.Generic;
using System.Linq;

namespace NWCExporter.Services
{
    public class RevitDocumentService : IRevitDocumentService
    {
        // =====================================================================
        // Fields
        // =====================================================================

        private readonly Document _doc;

        // =====================================================================
        // Constructor
        // =====================================================================

        public RevitDocumentService(Document doc)
        {
            _doc = doc;
        }

        // =====================================================================
        // Methods
        // =====================================================================

        public string GetDocumentPath() => _doc.PathName;
        public string GetDocumentTitle() => _doc.Title;

        public IEnumerable<View3DItem> GetAll3DViews()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .Where(v => !v.IsTemplate)
                .OrderBy(v => v.Name)
                .Select(v => new View3DItem
                {
#if REVIT2022 || REVIT2023
                    ElementId = v.Id.IntegerValue,
#else
                    ElementId = v.Id.Value,
#endif
                    Name = v.Name
                })
                .ToList();
        }
    }
}