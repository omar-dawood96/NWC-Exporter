using Autodesk.Revit.DB;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using NWCExporter.Models;
using NWCExporter.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NWCExporter.ExternalEvents
{
    /// <summary>
    /// Revit external-event handler that processes a queue of NWC export tasks one step at a time.
    /// Tasks are grouped by file so each Revit document is opened exactly once, preventing
    /// repeated model-upgrade prompts when the same file has multiple views selected.
    /// Each Execute() call handles all views for one file, then re-raises via RequestNextStep.
    /// </summary>
    public class ExportEventHandler : IExternalEventHandler
    {
        // =====================================================================
        // Fields — Dependencies
        // =====================================================================

        private readonly INwcExportService _exportService;

        // =====================================================================
        // Properties
        // =====================================================================

        /// <summary>
        /// Flat task list set by the ViewModel before raising the event.
        /// Tasks are automatically grouped by file path during Execute().
        /// </summary>
        public List<ExportTaskItem> Tasks { get; set; } = new List<ExportTaskItem>();
        public string OutputPath { get; set; } = string.Empty;
        public NwcExportSettings? Settings { get; set; }

        /// <summary>
        /// Optional cache of already-open Revit documents keyed by normalized file path.
        /// When a document is found here, it is used directly instead of re-opening the file,
        /// which prevents a second model-upgrade prompt on files opened during LoadViews.
        /// The ViewModel owns the lifetime of these documents.
        /// </summary>
        public Dictionary<string, Document>? OpenDocumentsCache { get; set; }

        // =====================================================================
        // Callbacks
        // =====================================================================

        public event Action<int, int, string>? OnProgress;
        public event Action<ExportResult>? OnCompleted;
        public event Action? RequestNextStep;

        // =====================================================================
        // Fields — State
        // =====================================================================

        /// <summary>
        /// Tasks grouped by file path. Built once in the first Execute() call.
        /// Each entry is (filePath, viewsForThatFile). Processed one file per Execute() call.
        /// </summary>
        private List<(string FilePath, RevitFileItem File, List<View3DItem?> Views)> _fileGroups
            = new List<(string, RevitFileItem, List<View3DItem?>)>();

        private int _fileGroupIndex = 0;
        private int _completedTaskCount = 0;
        private ExportResult _result = new ExportResult();

        // =====================================================================
        // Constructor
        // =====================================================================

        public ExportEventHandler(INwcExportService exportService)
        {
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        }

        // =====================================================================
        // Public Methods
        // =====================================================================

        public string GetName() => "NWC Export Queue Export";

        /// <summary>
        /// Resets handler state so it can be reused for a new export run.
        /// </summary>
        public void Reset()
        {
            _fileGroups.Clear();
            _fileGroupIndex = 0;
            _completedTaskCount = 0;
            _result = new ExportResult();
        }

        // =====================================================================
        // IExternalEventHandler
        // =====================================================================

        public void Execute(UIApplication uiApp)
        {
            if (Settings == null)
            {
                _result.Errors.Add("Export settings are not initialized.");
                OnCompleted?.Invoke(_result);
                return;
            }

            // Build file groups once on first Execute() call
            if (_fileGroups.Count == 0)
                BuildFileGroups();

            if (_fileGroups.Count == 0 || _fileGroupIndex >= _fileGroups.Count)
            {
                OnCompleted?.Invoke(_result);
                return;
            }

            var group = _fileGroups[_fileGroupIndex];
            int totalTasks = Tasks.Count;

            // Process all views for this file in a single open/close cycle
            ProcessFileGroup(uiApp, group, totalTasks);

            _fileGroupIndex++;

            if (_fileGroupIndex >= _fileGroups.Count)
                OnCompleted?.Invoke(_result);
            else
                RequestNextStep?.Invoke();
        }

        // =====================================================================
        // Private Methods — Grouping
        // =====================================================================

        /// <summary>
        /// Groups the flat Tasks list by file path so each file is opened only once.
        /// Whole-model tasks (View == null) are kept as a single entry with a null view.
        /// </summary>
        private void BuildFileGroups()
        {
            _fileGroups = Tasks
                .GroupBy(t => Path.GetFullPath(t.File.FilePath), StringComparer.OrdinalIgnoreCase)
                .Select(g => (
                    FilePath: g.Key,
                    File: g.First().File,
                    Views: g.Select(t => t.View).ToList()
                ))
                .ToList();
        }

        // =====================================================================
        // Private Methods — Processing
        // =====================================================================

        /// <summary>
        /// Opens the Revit document for this group once, exports all views, then closes it.
        /// </summary>
        private void ProcessFileGroup(
            UIApplication uiApp,
            (string FilePath, RevitFileItem File, List<View3DItem?> Views) group,
            int totalTasks)
        {
            Document? doc = null;
            bool shouldClose = false;

            try
            {
                if (!File.Exists(group.FilePath))
                {
                    foreach (var _ in group.Views)
                        _result.Errors.Add($"{group.File.FileName}: file path is invalid or file does not exist.");
                    _completedTaskCount += group.Views.Count;
                    return;
                }

                // Use cached doc (from LoadViews) → active doc → open fresh
                doc = TryGetCachedDoc(group.FilePath)
                    ?? TryGetActiveDoc(uiApp.ActiveUIDocument?.Document, group.FilePath)
                    ?? OpenDocument(uiApp, group.FilePath, out shouldClose);

                if (doc == null)
                {
                    foreach (var _ in group.Views)
                        _result.Errors.Add($"{group.File.FileName}: failed to open document.");
                    _completedTaskCount += group.Views.Count;
                    return;
                }

                // Export each view (or the whole model) without re-opening the file
                foreach (var view in group.Views)
                {
                    _completedTaskCount++;

                    try
                    {
                        string targetFolder = GetProjectOutputFolder(OutputPath, group.File, Settings!);
                        string name = BuildFileName(group.File, view);

                        // Ensure target folder exists before exporting
                        if (!System.IO.Directory.Exists(targetFolder))
                            System.IO.Directory.CreateDirectory(targetFolder);

                        // doc.Export() throws on failure; success = no exception thrown
                        doc.Export(targetFolder, name, BuildOptions(Settings!, view));
                        _result.SuccessCount++;
                    }
                    catch (Exception ex)
                    {
                        _result.Errors.Add($"{group.File.FileName} [{view?.Name ?? "Whole Model"}]: {ex.Message}");
                    }

                    double percent = totalTasks > 0 ? (_completedTaskCount * 100.0 / totalTasks) : 0;
                    OnProgress?.Invoke(_completedTaskCount, totalTasks,
                        $"Exporting {_completedTaskCount} of {totalTasks} ({percent:0}%)");
                }
            }
            catch (Exception ex)
            {
                _result.Errors.Add($"{group.File.FileName}: {ex.Message}");
            }
            finally
            {
                // Close only if we opened it — not if it was the already-active document
                if (doc != null && shouldClose)
                    doc.Close(false);
            }
        }

        // =====================================================================
        // Private Methods — Document Handling
        // =====================================================================

        private Document? TryGetCachedDoc(string filePath)
        {
            if (OpenDocumentsCache == null) return null;
            OpenDocumentsCache.TryGetValue(filePath, out var doc);
            return doc;
        }

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

        private Document OpenDocument(UIApplication uiApp, string selectedPath, out bool shouldClose)
        {
            shouldClose = false;
            uiApp.DialogBoxShowing += OnDialogBoxShowing;

            try
            {
                ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(selectedPath);
                var openOptions = new OpenOptions();
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
        // Private Methods — Export Helpers
        // =====================================================================

        private static string BuildFileName(RevitFileItem file, View3DItem? view)
        {
            string baseName = Path.GetFileNameWithoutExtension(file.FileName);
            return view == null
                ? $"{baseName}.nwc"
                : $"{baseName}_{SanitizeName(view.Name)}.nwc";
        }

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

        private static string SanitizeName(string name)
            => string.Concat(name.Split(Path.GetInvalidFileNameChars()));

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