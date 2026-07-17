using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using NWCExporter.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NWCExporter.Services
{
    /// <summary>
    /// Service responsible for exporting Revit documents to NWC (Navisworks Cache) format.
    /// Supports both full-model exports and targeted 3D view exports across multiple files.
    /// </summary>
    public class NwcExportService : INwcExportService
    {
        // =====================================================================
        // Public Methods
        // =====================================================================

        /// <summary>
        /// Exports a collection of Revit files to NWC format based on the provided settings.
        /// </summary>
        /// <param name="uiApp">
        /// The active Revit <see cref="UIApplication"/> instance used to open documents and access the application context.
        /// </param>
        /// <param name="files">
        /// A collection of <see cref="RevitFileItem"/> objects representing the Revit files to export.
        /// Only items with <c>IsSelected = true</c> will be processed.
        /// </param>
        /// <param name="outputFolder">
        /// The root output directory where exported NWC files will be saved.
        /// Created automatically if it does not exist.
        /// </param>
        /// <param name="settings">
        /// An <see cref="NwcExportSettings"/> object containing export configuration such as
        /// coordinate system, faceting factor, element parameters, and folder structure options.
        /// </param>
        /// <param name="selectedViewsByFile">
        /// Optional. A dictionary mapping each file path to its list of selected <see cref="View3DItem"/> objects.
        /// When provided, each view is exported as a separate NWC file.
        /// When <c>null</c> or empty, the entire model is exported as a single NWC file.
        /// </param>
        /// <param name="progress">
        /// Optional callback to report export progress.
        /// Receives three arguments: current step (int), total steps (int), and a status message (string).
        /// </param>
        /// <returns>
        /// An <see cref="ExportResult"/> containing the count of successful exports
        /// and a list of error messages for any files or views that failed.
        /// </returns>
        /// <remarks>
        /// Documents that are not currently open in Revit will be opened temporarily and closed after export.
        /// Dialog boxes triggered during document opening are automatically suppressed.
        /// </remarks>
        public ExportResult Export(
            UIApplication uiApp,
            IEnumerable<RevitFileItem> files,
            string outputFolder,
            NwcExportSettings settings,
            Dictionary<string, List<View3DItem>>? selectedViewsByFile = null,
            Action<int, int, string>? progress = null)
        {
            if (!Directory.Exists(outputFolder))
                Directory.CreateDirectory(outputFolder);

            var result = new ExportResult();
            var app = uiApp.Application;
            var activeDoc = uiApp.ActiveUIDocument?.Document;

            var fileList = files.Where(f => f.IsSelected).ToList();
            bool useViews = selectedViewsByFile != null && selectedViewsByFile.Count > 0;

            int totalSteps = useViews
                ? selectedViewsByFile!.Values.Sum(v => v.Count)
                : fileList.Count;

            int currentStep = 0;

            foreach (var file in fileList)
            {
                Document? docToExport = null;
                bool shouldClose = false;

                try
                {
                    if (string.IsNullOrWhiteSpace(file.FilePath) || !File.Exists(file.FilePath))
                    {
                        result.Errors.Add($"{file.FileName}: file path is invalid or file does not exist.");
                        continue;
                    }

                    string selectedPath = Path.GetFullPath(file.FilePath);

                    docToExport = TryGetActiveDoc(activeDoc, selectedPath)
                        ?? OpenDocument(uiApp, selectedPath, out shouldClose);

                    if (docToExport == null)
                    {
                        result.Errors.Add($"{file.FileName}: failed to open document.");
                        continue;
                    }

                    if (useViews)
                    {
                        if (!selectedViewsByFile!.TryGetValue(selectedPath, out var fileViews) || fileViews.Count == 0)
                        {
                            result.Errors.Add($"{file.FileName}: no 3D views selected for this file.");
                            continue;
                        }

                        foreach (var view in fileViews)
                        {
                            currentStep++;
                            progress?.Invoke(currentStep, totalSteps, $"Exporting {currentStep} of {totalSteps}...");

                            string name = $"{Path.GetFileNameWithoutExtension(file.FileName)}_{SanitizeName(view.Name)}.nwc";
                            string targetFolder = GetProjectOutputFolder(outputFolder, file, settings);

                            docToExport.Export(targetFolder, name, BuildOptions(settings, view));
                            result.SuccessCount++;
                        }
                    }
                    else
                    {
                        currentStep++;
                        progress?.Invoke(currentStep, totalSteps, $"Exporting {currentStep} of {totalSteps}...");

                        string name = Path.GetFileNameWithoutExtension(file.FileName) + ".nwc";
                        string targetFolder = GetProjectOutputFolder(outputFolder, file, settings);

                        docToExport.Export(targetFolder, name, BuildOptions(settings, null));
                        result.SuccessCount++;
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{file.FileName}: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[NWCExporter] {file.FileName}: {ex}");
                }
                finally
                {
                    if (docToExport != null && shouldClose)
                        docToExport.Close(false);
                }
            }

            progress?.Invoke(totalSteps, totalSteps, "Done");
            return result;
        }

        // =====================================================================
        // Private Methods — Document Handling
        // =====================================================================

        /// <summary>
        /// Checks whether the currently active Revit document matches the specified file path.
        /// </summary>
        /// <param name="activeDoc">
        /// The currently active <see cref="Document"/> in the Revit session, or <c>null</c> if none is open.
        /// </param>
        /// <param name="selectedPath">
        /// The fully-qualified path of the file to match against the active document.
        /// </param>
        /// <returns>
        /// The active <see cref="Document"/> if its path matches <paramref name="selectedPath"/>
        /// (case-insensitive); otherwise <c>null</c>.
        /// </returns>
        private static Document? TryGetActiveDoc(Document? activeDoc, string selectedPath)
        {
            if (activeDoc == null || string.IsNullOrWhiteSpace(activeDoc.PathName))
                return null;

            return string.Equals(
                Path.GetFullPath(activeDoc.PathName),
                selectedPath,
                StringComparison.OrdinalIgnoreCase)
                ? activeDoc
                : null;
        }

        /// <summary>
        /// Opens a Revit document from disk using the last-viewed workset configuration.
        /// Suppresses any dialog boxes that appear during the open operation.
        /// </summary>
        /// <param name="uiApp">The active <see cref="UIApplication"/> used to open the document.</param>
        /// <param name="selectedPath">The full file system path of the Revit document to open.</param>
        /// <param name="shouldClose">
        /// Output parameter set to <c>true</c> when the document was successfully opened
        /// and should be closed by the caller after use.
        /// </param>
        /// <returns>
        /// The opened <see cref="Document"/>, or <c>null</c> if the operation failed.
        /// </returns>
        private Document? OpenDocument(UIApplication uiApp, string selectedPath, out bool shouldClose)
        {
            shouldClose = false;

            uiApp.DialogBoxShowing += OnDialogBoxShowing;

            try
            {
                ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(selectedPath);
                OpenOptions openOptions = new OpenOptions();
                WorksetConfiguration wsConfig =
                                     new WorksetConfiguration(WorksetConfigurationOption.OpenLastViewed);

                openOptions.SetOpenWorksetsConfiguration(wsConfig);

                var doc = uiApp.Application.OpenDocumentFile(modelPath, openOptions);
                shouldClose = true;
                return doc;
            }
            finally
            {
                uiApp.DialogBoxShowing -= OnDialogBoxShowing;
            }
        }

        /// <summary>
        /// Handles the <see cref="UIApplication.DialogBoxShowing"/> event to automatically
        /// dismiss task dialogs that appear during document opening.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">
        /// The <see cref="DialogBoxShowingEventArgs"/> containing event data.
        /// If the event is a <see cref="TaskDialogShowingEventArgs"/>, the dialog is overridden with result code <c>1</c>.
        /// </param>
        private void OnDialogBoxShowing(object sender, DialogBoxShowingEventArgs e)
        {
            try
            {
                if (e is TaskDialogShowingEventArgs taskArgs)
                    taskArgs.OverrideResult(1);
            }
            catch { }
        }

        // =====================================================================
        // Private Methods — Export Options
        // =====================================================================

        /// <summary>
        /// Builds a <see cref="NavisworksExportOptions"/> instance from the given settings and optional 3D view.
        /// </summary>
        /// <param name="s">
        /// The <see cref="NwcExportSettings"/> containing user-configured export parameters such as
        /// coordinate system, faceting factor, element parameters, and toggle options.
        /// </param>
        /// <param name="view">
        /// An optional <see cref="View3DItem"/> specifying which 3D view to export.
        /// When <c>null</c>, the export scope is set to <see cref="NavisworksExportScope.Model"/>.
        /// When provided, the scope is set to <see cref="NavisworksExportScope.View"/> and
        /// <see cref="NavisworksExportOptions.ViewId"/> is assigned accordingly.
        /// </param>
        /// <returns>
        /// A fully configured <see cref="NavisworksExportOptions"/> object ready for use in a Revit export call.
        /// </returns>
        /// <remarks>
        /// The <c>ViewId</c> assignment uses a conditional compilation symbol to support both
        /// Revit 2022/2023 (<c>ElementId(int)</c>) and later versions (<c>ElementId(long)</c>).
        /// </remarks>
        private static NavisworksExportOptions BuildOptions(NwcExportSettings s, View3DItem? view)
        {
            var opts = new NavisworksExportOptions
            {
                ExportScope = view is null
                    ? NavisworksExportScope.Model
                    : NavisworksExportScope.View,

                Coordinates = s.Coordinates == "Internal"
                    ? NavisworksCoordinates.Internal
                    : NavisworksCoordinates.Shared,

                DivideFileIntoLevels = s.DivideFileIntoLevels,
                ExportElementIds = s.ConvertElementIds,
                ExportParts = s.ConvertConstructionParts,
                ExportRoomAsAttribute = s.ConvertRoomAsAttribute,
                ExportRoomGeometry = s.ExportRoomGeometry,
                ExportUrls = s.ConvertUrls,
                FacetingFactor = s.FacetingFactor,
                FindMissingMaterials = s.TryFindMissingMaterials,

                Parameters = s.ConvertElementParameters switch
                {
                    "None" => NavisworksParameters.None,
                    "Elements" => NavisworksParameters.Elements,
                    _ => NavisworksParameters.All
                }
            };

            if (view is not null)
            {
#if REVIT2022 || REVIT2023
                opts.ViewId = new ElementId((int)view.ElementId);
#else
                opts.ViewId = new ElementId(view.ElementId);
#endif
            }

            return opts;
        }

        // =====================================================================
        // Private Methods — Utilities
        // =====================================================================

        /// <summary>
        /// Removes invalid file name characters from the given string.
        /// </summary>
        /// <param name="name">The raw string to sanitize (e.g., a view name).</param>
        /// <returns>
        /// A new string with all characters invalid for file names stripped out,
        /// safe to use as part of a file or folder name.
        /// </returns>
        private static string SanitizeName(string name)
            => string.Concat(name.Split(Path.GetInvalidFileNameChars()));

        /// <summary>
        /// Determines the output folder for a given file's export, optionally creating
        /// a dedicated subdirectory per project based on the export settings.
        /// </summary>
        /// <param name="baseOutputFolder">The root output directory for all exports.</param>
        /// <param name="file">
        /// The <see cref="RevitFileItem"/> whose file name is used to derive the project subfolder name.
        /// </param>
        /// <param name="settings">
        /// The <see cref="NwcExportSettings"/> controlling whether per-project subfolders are created
        /// via <see cref="NwcExportSettings.CreateFolderPerProject"/>.
        /// </param>
        /// <returns>
        /// The full path of the target output folder.
        /// Returns <paramref name="baseOutputFolder"/> directly if <c>CreateFolderPerProject</c> is <c>false</c>;
        /// otherwise returns (and creates if needed) a subdirectory named after the Revit file.
        /// </returns>
        private static string GetProjectOutputFolder(
            string baseOutputFolder,
            RevitFileItem file,
            NwcExportSettings settings)
        {
            if (!settings.CreateFolderPerProject)
                return baseOutputFolder;

            string projectFolderName = SanitizeName(Path.GetFileNameWithoutExtension(file.FileName));
            string projectFolder = Path.Combine(baseOutputFolder, projectFolderName);

            if (!Directory.Exists(projectFolder))
                Directory.CreateDirectory(projectFolder);

            return projectFolder;
        }
    }
}