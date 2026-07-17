using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using NWCExporter.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NWCExporter.ExternalEvents
{
    public class LoadViewsEventHandler : IExternalEventHandler
    {
        // =====================================================================
        // Properties
        // =====================================================================

        public string FilePath { get; set; } = string.Empty;

        // =====================================================================
        // Callbacks
        // =====================================================================

        public Action<int, int, string>? OnProgress;

        /// <summary>
        /// Called when views are loaded successfully.
        /// Receives the view list AND the open Document so it can be cached
        /// by the caller and reused during export — avoiding a second open/upgrade.
        /// The caller is responsible for closing the Document when it is no longer needed.
        /// </summary>
        public Action<List<View3DItem>, Document>? OnCompleted;

        public Action<string>? OnFailed;

        // =====================================================================
        // IExternalEventHandler
        // =====================================================================

        public string GetName() => "Load Views External Event";

        public void Execute(UIApplication uiApp)
        {
            Document? openedDoc = null;
            bool shouldClose = false;

            const int totalSteps = 3;

            try
            {
                if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
                    throw new InvalidOperationException("Selected file path is invalid or file does not exist.");

                string selectedPath = Path.GetFullPath(FilePath);
                Document? activeDoc = uiApp.ActiveUIDocument?.Document;

                OnProgress?.Invoke(1, totalSteps, "Opening file...");

                Document docToQuery;

                if (IsActiveDoc(activeDoc, selectedPath))
                {
                    // Already open — no close needed
                    docToQuery = activeDoc!;
                    shouldClose = false;
                }
                else
                {
                    openedDoc = OpenDocument(uiApp, selectedPath, out shouldClose);
                    docToQuery = openedDoc ?? throw new InvalidOperationException("Failed to open the selected file.");
                }

                OnProgress?.Invoke(2, totalSteps, "Collecting 3D views...");

                var views = CollectViews(docToQuery);

                OnProgress?.Invoke(3, totalSteps, "Views loaded.");

                // Pass the Document back to the caller so it can be cached and
                // reused during export. The caller owns the close responsibility.
                // We clear shouldClose so the finally block does NOT close it here.
                shouldClose = false;
                OnCompleted?.Invoke(views, docToQuery);
            }
            catch (Exception ex)
            {
                // On failure, close any doc we opened
                if (openedDoc != null && shouldClose)
                    openedDoc.Close(false);

                OnFailed?.Invoke(ex.Message);
            }
            finally
            {
                // Only reached when shouldClose is still true = an error path
                // where OnCompleted was never called, handled above.
                // Normal success path: shouldClose was set to false before OnCompleted.
            }
        }

        // =====================================================================
        // Private Methods
        // =====================================================================

        private static bool IsActiveDoc(Document? activeDoc, string selectedPath)
        {
            return activeDoc != null
                && !string.IsNullOrWhiteSpace(activeDoc.PathName)
                && string.Equals(
                    Path.GetFullPath(activeDoc.PathName),
                    selectedPath,
                    StringComparison.OrdinalIgnoreCase);
        }

        private Document OpenDocument(UIApplication uiApp, string selectedPath, out bool shouldClose)
        {
            shouldClose = false;

            uiApp.DialogBoxShowing += OnDialogBoxShowing;

            try
            {
                ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(selectedPath);
                OpenOptions openOptions = new OpenOptions();
                openOptions.SetOpenWorksetsConfiguration(
                    new WorksetConfiguration(WorksetConfigurationOption.OpenLastViewed));

                var doc = uiApp.Application.OpenDocumentFile(modelPath, openOptions);
                shouldClose = true;
                return doc;
            }
            finally
            {
                uiApp.DialogBoxShowing -= OnDialogBoxShowing;
            }
        }

        private static List<View3DItem> CollectViews(Document doc)
        {
            return new FilteredElementCollector(doc)
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
                    Name = v.Name,
                    IsSelected = false
                })
                .ToList();
        }

        private void OnDialogBoxShowing(object sender, DialogBoxShowingEventArgs e)
        {
            try
            {
                if (e is TaskDialogShowingEventArgs taskArgs)
                    taskArgs.OverrideResult(1);
            }
            catch { }
        }
    }
}