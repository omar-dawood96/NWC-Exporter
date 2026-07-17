using Autodesk.Revit.UI;
using System;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace NWCExporter.Revit
{
    public class App : IExternalApplication
    {
        // =====================================================================
        // IExternalApplication
        // =====================================================================

        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                CreateRibbonTab(app, "KAITECH-BD-R10");

                RibbonPanel panel = app.CreateRibbonPanel("KAITECH-BD-R10", "Export");
                string path = Assembly.GetExecutingAssembly().Location;

                PushButtonData button = new("NWCExporter", "NWC\nExporter", path,
                    "NWCExporter.Commands.OpenExporterCommand")
                {
                    ToolTip = "Export Revit views or the whole model to Navisworks NWC format.",
                    LongDescription = "Export Revit views or the whole model to Navisworks NWC format. " +
                                      "Select views to export, specify export options, " +
                                      "and choose the destination folder for the NWC files.\nAuthor: Omar Ali",
                    LargeImage = LoadImage("NWC-16.png"),
                    Image = LoadImage("NWC-32.png")
                };

                panel.AddItem(button);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("NWC Exporter", ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

        // =====================================================================
        // Private Methods
        // =====================================================================

        private static void CreateRibbonTab(UIControlledApplication app, string tabName)
        {
            try { app.CreateRibbonTab(tabName); }
            catch { /* Tab already exists — ignore */ }
        }

        private static BitmapImage? LoadImage(string name)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"NWCExporter.Resources.{name}";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return null;

            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = stream;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();

            return image;
        }
    }
}