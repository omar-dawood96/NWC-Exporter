using NWCExporter.Models;
using System.Collections.Generic;

namespace NWCExporter.Services
{
    public interface IRevitDocumentService
    {
        string GetDocumentPath();
        string GetDocumentTitle();   
        IEnumerable<View3DItem> GetAll3DViews();
    }


}
