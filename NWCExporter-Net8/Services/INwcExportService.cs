using Autodesk.Revit.UI;
using NWCExporter.Models;
using System;
using System.Collections.Generic;

namespace NWCExporter.Services
{
    public interface INwcExportService
    {
        ExportResult Export(
            UIApplication uiApp,
            IEnumerable<RevitFileItem> files,
            string outputFolder,
            NwcExportSettings settings,
            Dictionary<string, List<View3DItem>>? selectedViewsByFile = null,
            Action<int, int, string>? progress = null);
    }
}