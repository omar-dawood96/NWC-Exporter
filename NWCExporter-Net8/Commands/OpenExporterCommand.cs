using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NWCExporter.Services;
using NWCExporter.ViewModels;
using NWCExporter.Views;
using System.Windows;

namespace NWCExporter.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class OpenExporterCommand : IExternalCommand
    {
        // ── Fields ────────────────────────────────────────────────────────────
        private static MainWindow? _window;

        // ── IExternalCommand ──────────────────────────────────────────────────
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;

            if (uiDoc == null || uiDoc.Document == null)
            {
                TaskDialog.Show("NWC Exporter", "No active Revit document is open.");
                return Result.Cancelled;
            }

            if (_window != null)
                return BringToFront();

            return OpenNewWindow(uiApp);
        }

        // ── Private methods ───────────────────────────────────────────────────
        private static Result BringToFront()
        {
            if (_window!.WindowState == WindowState.Minimized)
                _window.WindowState = WindowState.Normal;

            _window.Activate();
            return Result.Succeeded;
        }

        private static Result OpenNewWindow(UIApplication uiApp)
        {
            var doc = uiApp.ActiveUIDocument.Document;

            var revitService = new RevitDocumentService(doc);
            var exportService = new NwcExportService();
            var vm = new MainViewModel(uiApp, revitService, exportService);

            _window = new MainWindow(vm);
            _window.Closed += (s, e) => _window = null;

            new System.Windows.Interop.WindowInteropHelper(_window).Owner = uiApp.MainWindowHandle;

            _window.Show();
            return Result.Succeeded;
        }
    }
}