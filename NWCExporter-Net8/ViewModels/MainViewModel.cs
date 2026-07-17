using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using NWCExporter.Commands;
using NWCExporter.ExternalEvents;
using NWCExporter.Models;
using NWCExporter.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace NWCExporter.ViewModels
{
    /// <summary>
    /// Primary ViewModel for the NWC Exporter main window.
    /// Manages file selection, export mode, 3D view loading, progress reporting,
    /// and orchestrates the Revit external event–based export pipeline.
    /// </summary>
    public class MainViewModel : BaseViewModel
    {
        // =====================================================================
        // Fields — Dependencies
        // =====================================================================

        private readonly UIApplication _uiApp;
        private readonly IRevitDocumentService _revitService;
        private readonly NwcExportSettings _settings;
        private readonly Dispatcher _dispatcher;

        // =====================================================================
        // Fields — Caches
        // =====================================================================

        /// <summary>
        /// Cache of loaded <see cref="View3DItem"/> lists keyed by normalized file path.
        /// Avoids redundant Revit API calls when switching between files.
        /// </summary>
        private readonly Dictionary<string, List<View3DItem>> _viewsCache =
            new Dictionary<string, List<View3DItem>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Cache of user-selected view element IDs keyed by normalized file path.
        /// Persists view selections when the user switches the selected file item.
        /// </summary>
        private readonly Dictionary<string, HashSet<long>> _selectedViewsCache =
            new Dictionary<string, HashSet<long>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Keeps Revit documents open after LoadViews so Export can reuse them
        /// without triggering a second model-upgrade prompt.
        /// Documents here were opened by us (not the active doc) and must be
        /// closed when no longer needed (on ClearAllFiles or window close).
        /// </summary>
        private readonly Dictionary<string, Document> _openDocumentsCache =
            new Dictionary<string, Document>(StringComparer.OrdinalIgnoreCase);

        // =====================================================================
        // Fields — External Events
        // =====================================================================

        private readonly ExportEventHandler _exportHandler;
        private readonly ExternalEvent _exportEvent;

        private readonly LoadViewsEventHandler _loadViewsHandler;
        private readonly ExternalEvent _loadViewsEvent;

        // =====================================================================
        // Fields — State
        // =====================================================================

        private string _sourcePath = string.Empty;
        private string _outputPath = string.Empty;
        private RevitFileItem? _selectedFileItem;
        private bool _includeSubFolders;
        private bool _isWholeModel = true;
        private bool _isCustomViews;
        private bool _exportDone;
        private string _statusMessage = string.Empty;

        // =====================================================================
        // Fields — Progress
        // =====================================================================

        private bool _isBusy;
        private double _progressValue;
        private double _progressMaximum = 1;
        private string _progressText = string.Empty;

        // =====================================================================
        // Constructor
        // =====================================================================

        /// <summary>
        /// Initializes a new instance of <see cref="MainViewModel"/>.
        /// Sets up commands, external events, child ViewModels, and pre-populates
        /// the file list with the currently active Revit document (if any).
        /// </summary>
        /// <param name="uiApp">
        /// The active Revit <see cref="UIApplication"/>, used for document access and external event raising.
        /// </param>
        /// <param name="revitService">
        /// Service for querying Revit document metadata such as the current document path.
        /// </param>
        /// <param name="exportService">
        /// Service that performs the actual NWC export operations (passed through to export handlers).
        /// </param>
        public MainViewModel(
            UIApplication uiApp,
            IRevitDocumentService revitService,
            INwcExportService exportService)
        {
            _uiApp = uiApp;
            _revitService = revitService;
            _dispatcher = Dispatcher.CurrentDispatcher;

            _settings = new NwcExportSettings();
            SettingsVM = new SettingsViewModel(_settings);

            SelectedFiles = new ObservableCollection<RevitFileItem>();
            AvailableViews = new ObservableCollection<View3DItem>();

            _exportHandler = new ExportEventHandler(exportService);
            _exportEvent = ExternalEvent.Create(_exportHandler);

            // Subscribe to export handler events once; Reset() clears state between runs.
            _exportHandler.OnProgress += (current, total, text) =>
            {
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    ProgressMaximum = Math.Max(1, total);
                    ProgressValue = current;
                    ProgressText = text;
                }), DispatcherPriority.Background);
            };

            _exportHandler.OnCompleted += result =>
            {
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    IsBusy = false;
                    ProgressText = string.Empty;
                    ProgressValue = 0;
                    ProgressMaximum = 1;

                    // Close all documents we held open for export
                    CloseAllCachedDocuments();

                    bool hasErrors = result.Errors.Count > 0;
                    bool hasSuccess = result.SuccessCount > 0;

                    if (!hasSuccess && !hasErrors)
                    {
                        // Nothing was exported and no error was reported — likely all files
                        // were skipped silently (e.g. no views matched). Show a clear warning.
                        ExportDone = false;
                        MessageBox.Show(
                            "No files were exported. Please make sure at least one file and view are selected, and that the output folder is accessible.",
                            "Export Warning",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    else if (!hasSuccess && hasErrors)
                    {
                        // Every file failed
                        ExportDone = false;
                        string details = string.Join("\n", result.Errors);
                        MessageBox.Show(
                            $"Export failed. No files were exported successfully.\n\nDetails:\n{details}",
                            "Export Failed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                    else
                    {
                        // At least some files exported successfully
                        ExportDone = true;
                        StatusMessage = $"Export complete — {result.SuccessCount} file{(result.SuccessCount == 1 ? "" : "s")} exported successfully";

                        if (hasErrors)
                        {
                            string details = string.Join("\n", result.Errors);
                            MessageBox.Show(
                                $"Some files failed to export.\n\nSuccessful: {result.SuccessCount}\nFailed: {result.Errors.Count}\n\nDetails:\n{details}",
                                "Partial Export",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    }
                }), DispatcherPriority.Background);
            };

            _exportHandler.RequestNextStep += () =>
            {
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        _exportEvent.Raise();
                    }
                    catch (Exception ex)
                    {
                        IsBusy = false;
                        ProgressText = string.Empty;
                        ProgressValue = 0;
                        ProgressMaximum = 1;

                        MessageBox.Show(
                            ex.Message,
                            "Export Failed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }), DispatcherPriority.Background);
            };

            _loadViewsHandler = new LoadViewsEventHandler();
            _loadViewsEvent = ExternalEvent.Create(_loadViewsHandler);

            var docPath = _revitService.GetDocumentPath();
            if (!string.IsNullOrWhiteSpace(docPath))
            {
                _sourcePath = docPath;
                SelectedFiles.Add(new RevitFileItem
                {
                    FilePath = docPath,
                    IsSelected = true
                });
            }

            BrowseSourceCommand = new RelayCommand(BrowseSource, () => !IsBusy);
            BrowseOutputCommand = new RelayCommand(BrowseOutput, () => !IsBusy);
            AddFileCommand = new RelayCommand(AddFile, () => !IsBusy);
            RemoveFileCommand = new RelayCommand(RemoveSelectedFile, () => !IsBusy && SelectedFileItem is not null);
            ClearAllFilesCommand = new RelayCommand(ClearAllFiles, () => !IsBusy && SelectedFiles.Any());
            ExportCommand = new RelayCommand(RunExport, CanExport);
            CloseCommand = new RelayCommand<Window>(w => w?.Close());
            ResetDefaultsCommand = new RelayCommand(() => SettingsVM.ResetDefaults(), () => !IsBusy);

            SelectedFileItem = SelectedFiles.FirstOrDefault();
        }

        // =====================================================================
        // Properties — Child ViewModels & Collections
        // =====================================================================

        /// <summary>
        /// Gets the <see cref="SettingsViewModel"/> that exposes and manages <see cref="NwcExportSettings"/>.
        /// </summary>
        public SettingsViewModel SettingsVM { get; }

        /// <summary>
        /// Gets the observable collection of Revit files currently added to the export list.
        /// </summary>
        public ObservableCollection<RevitFileItem> SelectedFiles { get; }

        /// <summary>
        /// Gets the observable collection of 3D views available for the currently selected file.
        /// Populated only when <see cref="IsCustomViews"/> is <c>true</c>.
        /// </summary>
        public ObservableCollection<View3DItem> AvailableViews { get; }

        // =====================================================================
        // Properties — Path & Selection
        // =====================================================================

        /// <summary>
        /// Gets or sets the source file or folder path used to populate the file list.
        /// Changing this value triggers <see cref="SyncSourcePathToFileList"/>.
        /// </summary>
        public string SourcePath
        {
            get => _sourcePath;
            set
            {
                if (_sourcePath == value) return;

                _sourcePath = value;
                OnPropertyChanged();
                SyncSourcePathToFileList();
                RaiseCommandStates();
            }
        }

        /// <summary>
        /// Gets or sets the root output directory where exported NWC files will be written.
        /// </summary>
        public string OutputPath
        {
            get => _outputPath;
            set
            {
                if (!SetProperty(ref _outputPath, value)) return;

                RaiseCommandStates();
            }
        }

        /// <summary>
        /// Gets or sets the currently selected <see cref="RevitFileItem"/> in the file list.
        /// Changing the selection saves view selections for the previous file and,
        /// if <see cref="IsCustomViews"/> is active, loads views for the newly selected file.
        /// </summary>
        public RevitFileItem? SelectedFileItem
        {
            get => _selectedFileItem;
            set
            {
                if (_selectedFileItem == value) return;

                SaveCurrentViewSelections();

                _selectedFileItem = value;
                OnPropertyChanged();
                RemoveFileCommand.RaiseCanExecuteChanged();

                if (IsCustomViews)
                    LoadViewsForSelectedFile();

                RaiseCommandStates();
            }
        }

        /// <summary>
        /// Gets or sets whether subdirectories of the source folder are scanned for <c>.rvt</c> files.
        /// When changed, refreshes the file list from the current source path.
        /// </summary>
        public bool IncludeSubFolders
        {
            get => _includeSubFolders;
            set
            {
                if (_includeSubFolders == value) return;

                _includeSubFolders = value;
                OnPropertyChanged();

                if (!string.IsNullOrWhiteSpace(SourcePath))
                    RefreshFilesFromSource();

                RaiseCommandStates();
            }
        }

        // =====================================================================
        // Properties — Export Mode
        // =====================================================================

        /// <summary>
        /// Gets or sets whether the export mode is set to whole-model export.
        /// Setting this to <c>true</c> disables custom view export and clears <see cref="AvailableViews"/>.
        /// Mutually exclusive with <see cref="IsCustomViews"/>.
        /// </summary>
        public bool IsWholeModel
        {
            get => _isWholeModel;
            set
            {
                if (_isWholeModel == value) return;

                _isWholeModel = value;

                if (value)
                {
                    _isCustomViews = false;
                    AvailableViews.Clear();
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCustomViews));
                OnPropertyChanged(nameof(ViewsListVisibility));
                RaiseCommandStates();
            }
        }

        /// <summary>
        /// Gets or sets whether the export mode is set to per-view export.
        /// Setting this to <c>true</c> triggers loading of 3D views for the selected file.
        /// Mutually exclusive with <see cref="IsWholeModel"/>.
        /// </summary>
        public bool IsCustomViews
        {
            get => _isCustomViews;
            set
            {
                if (_isCustomViews == value) return;

                if (_isCustomViews && !value)
                    SaveCurrentViewSelections();

                _isCustomViews = value;

                if (value)
                {
                    _isWholeModel = false;
                    LoadViewsForSelectedFile();
                }
                else
                {
                    AvailableViews.Clear();
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsWholeModel));
                OnPropertyChanged(nameof(ViewsListVisibility));
                RaiseCommandStates();
            }
        }

        // =====================================================================
        // Properties — Visibility (computed)
        // =====================================================================

        /// <summary>
        /// Gets the visibility of the 3D views list panel.
        /// Returns <see cref="Visibility.Visible"/> only when <see cref="IsCustomViews"/> is <c>true</c>.
        /// </summary>
        public System.Windows.Visibility ViewsListVisibility =>
            IsCustomViews ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        /// <summary>
        /// Gets the visibility of the export status message.
        /// Returns <see cref="Visibility.Visible"/> only when <see cref="ExportDone"/> is <c>true</c>.
        /// </summary>
        public System.Windows.Visibility StatusVisibility =>
            ExportDone ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        /// <summary>
        /// Gets the visibility of the progress bar and progress text.
        /// Returns <see cref="Visibility.Visible"/> only when <see cref="IsBusy"/> is <c>true</c>.
        /// </summary>
        public System.Windows.Visibility ProgressVisibility =>
            IsBusy ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        // =====================================================================
        // Properties — Status & Progress
        // =====================================================================

        /// <summary>
        /// Gets or sets whether the most recent export operation has completed.
        /// Also triggers a change notification for <see cref="StatusVisibility"/>.
        /// </summary>
        public bool ExportDone
        {
            get => _exportDone;
            set
            {
                if (!SetProperty(ref _exportDone, value)) return;

                OnPropertyChanged(nameof(StatusVisibility));
            }
        }

        /// <summary>
        /// Gets or sets the status message displayed after an export completes.
        /// Typically summarises the number of files exported successfully.
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// Gets or sets whether a long-running operation (export or view loading) is in progress.
        /// While <c>true</c>, most commands are disabled and the progress panel is shown.
        /// Also triggers a change notification for <see cref="ProgressVisibility"/>.
        /// </summary>
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (!SetProperty(ref _isBusy, value)) return;

                OnPropertyChanged(nameof(ProgressVisibility));
                RaiseCommandStates();
            }
        }

        /// <summary>
        /// Gets or sets the current progress value for the active operation.
        /// Updates are suppressed when the change is smaller than 0.001 to reduce UI churn.
        /// </summary>
        public double ProgressValue
        {
            get => _progressValue;
            set
            {
                if (Math.Abs(_progressValue - value) < 0.001) return;

                _progressValue = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Gets or sets the maximum value for the progress bar, representing the total number of steps.
        /// Defaults to <c>1</c> to prevent division-by-zero in progress calculations.
        /// </summary>
        public double ProgressMaximum
        {
            get => _progressMaximum;
            set
            {
                if (Math.Abs(_progressMaximum - value) < 0.001) return;

                _progressMaximum = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Gets or sets the human-readable progress label shown alongside the progress bar
        /// (e.g., "Exporting 3 of 10...").
        /// </summary>
        public string ProgressText
        {
            get => _progressText;
            set => SetProperty(ref _progressText, value);
        }

        // =====================================================================
        // Commands
        // =====================================================================

        /// <summary>Opens a file dialog to select a source Revit file or folder.</summary>
        public RelayCommand BrowseSourceCommand { get; }

        /// <summary>Opens a folder picker dialog to select the NWC output directory.</summary>
        public RelayCommand BrowseOutputCommand { get; }

        /// <summary>Opens a file dialog to append additional <c>.rvt</c> files to the list.</summary>
        public RelayCommand AddFileCommand { get; }

        /// <summary>Removes the currently selected file from <see cref="SelectedFiles"/>.</summary>
        public RelayCommand RemoveFileCommand { get; }

        /// <summary>Clears all files, views, caches, and resets path fields.</summary>
        public RelayCommand ClearAllFilesCommand { get; }

        /// <summary>Validates inputs and triggers the NWC export pipeline.</summary>
        public RelayCommand ExportCommand { get; }

        /// <summary>Closes the provided <see cref="Window"/> instance.</summary>
        public RelayCommand<Window> CloseCommand { get; }

        /// <summary>Resets all export settings in <see cref="SettingsVM"/> to their defaults.</summary>
        public RelayCommand ResetDefaultsCommand { get; }

        // =====================================================================
        // Methods — Command Handlers (Browse / File Management)
        // =====================================================================

        /// <summary>
        /// Handles <see cref="BrowseSourceCommand"/>.
        /// Presents a multi-select file dialog filtered to <c>.rvt</c> files,
        /// replaces <see cref="SelectedFiles"/> with the chosen files,
        /// and updates <see cref="SourcePath"/> to the first selected path.
        /// </summary>
        private void BrowseSource()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Revit source file(s)",
                Filter = "Revit Files (*.rvt)|*.rvt",
                Multiselect = true
            };

            if (dlg.ShowDialog() != true || dlg.FileNames.Length == 0)
                return;

            SourcePath = dlg.FileNames[0];
            SelectedFiles.Clear();

            foreach (var file in dlg.FileNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                SelectedFiles.Add(new RevitFileItem { FilePath = file, IsSelected = true });
            }

            SelectedFileItem = SelectedFiles.FirstOrDefault();
            RaiseCommandStates();
        }

        /// <summary>
        /// Handles <see cref="BrowseOutputCommand"/>.
        /// Uses a <see cref="SaveFileDialog"/> workaround to allow folder selection,
        /// then sets <see cref="OutputPath"/> to the chosen directory.
        /// </summary>
        private void BrowseOutput()
        {
            var dlg = new SaveFileDialog
            {
                Title = "Select output folder",
                Filter = "Folder|*.folder",
                FileName = "Select Folder",
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true
            };

            if (dlg.ShowDialog() != true) return;

            string? folder = Path.GetDirectoryName(dlg.FileName);
            if (!string.IsNullOrWhiteSpace(folder))
                OutputPath = folder;
        }

        /// <summary>
        /// Handles <see cref="AddFileCommand"/>.
        /// Appends new <c>.rvt</c> files chosen via a multi-select dialog to <see cref="SelectedFiles"/>,
        /// skipping duplicates. Selects the last added file after completion.
        /// </summary>
        private void AddFile()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Add Revit files",
                Filter = "Revit Files (*.rvt)|*.rvt",
                Multiselect = true
            };

            if (dlg.ShowDialog() != true) return;

            foreach (var file in dlg.FileNames)
            {
                bool exists = SelectedFiles.Any(x =>
                    string.Equals(
                        Path.GetFullPath(x.FilePath),
                        Path.GetFullPath(file),
                        StringComparison.OrdinalIgnoreCase));

                if (!exists)
                    SelectedFiles.Add(new RevitFileItem { FilePath = file, IsSelected = true });
            }

            if (string.IsNullOrWhiteSpace(SourcePath) && SelectedFiles.Any())
                SourcePath = SelectedFiles.First().FilePath;

            if (dlg.FileNames.Length > 0)
            {
                string lastAdded = dlg.FileNames.Last();
                SelectedFileItem = SelectedFiles.FirstOrDefault(x =>
                    string.Equals(
                        Path.GetFullPath(x.FilePath),
                        Path.GetFullPath(lastAdded),
                        StringComparison.OrdinalIgnoreCase));
            }

            RaiseCommandStates();
        }

        /// <summary>
        /// Handles <see cref="RemoveFileCommand"/>.
        /// Removes <see cref="SelectedFileItem"/> from the list. If the removed file was the
        /// current source, <see cref="SourcePath"/> is updated to the first remaining file.
        /// </summary>
        private void RemoveSelectedFile()
        {
            if (SelectedFileItem is null) return;

            var toRemove = SelectedFileItem;

            bool removedWasSource = !string.IsNullOrWhiteSpace(SourcePath) &&
                string.Equals(
                    Path.GetFullPath(toRemove.FilePath),
                    Path.GetFullPath(SourcePath),
                    StringComparison.OrdinalIgnoreCase);

            SelectedFiles.Remove(toRemove);

            if (removedWasSource)
                SourcePath = SelectedFiles.FirstOrDefault()?.FilePath ?? string.Empty;

            SelectedFileItem = SelectedFiles.FirstOrDefault();

            RaiseCommandStates();
        }

        /// <summary>
        /// Handles <see cref="ClearAllFilesCommand"/>.
        /// Removes all files from <see cref="SelectedFiles"/>, clears view data and both caches,
        /// and resets path fields and the selected item.
        /// </summary>
        private void ClearAllFiles()
        {
            SelectedFiles.Clear();
            AvailableViews.Clear();
            SelectedFileItem = null;
            SourcePath = string.Empty;
            _viewsCache.Clear();
            _selectedViewsCache.Clear();
            CloseAllCachedDocuments();
            RaiseCommandStates();
        }

        /// <summary>
        /// Closes all Revit documents that were opened by LoadViews and are
        /// being held open in <see cref="_openDocumentsCache"/>.
        /// </summary>
        private void CloseAllCachedDocuments()
        {
            foreach (var doc in _openDocumentsCache.Values)
            {
                try { doc.Close(false); } catch { }
            }
            _openDocumentsCache.Clear();
        }

        // =====================================================================
        // Methods — File List Sync
        // =====================================================================

        /// <summary>
        /// Synchronises <see cref="SelectedFiles"/> with the current <see cref="SourcePath"/>.
        /// If <see cref="IncludeSubFolders"/> is active, delegates to <see cref="RefreshFilesFromSource"/>.
        /// Otherwise, either highlights an existing matching entry or inserts/replaces the first entry.
        /// </summary>
        private void SyncSourcePathToFileList()
        {
            if (string.IsNullOrWhiteSpace(SourcePath)) return;

            string normalizedSource = Path.GetFullPath(SourcePath);

            if (IncludeSubFolders)
            {
                RefreshFilesFromSource();
                return;
            }

            var existing = SelectedFiles.FirstOrDefault(x =>
                string.Equals(
                    Path.GetFullPath(x.FilePath),
                    normalizedSource,
                    StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.IsSelected = true;
                SelectedFileItem = existing;
                OnPropertyChanged(nameof(SelectedFiles));
                return;
            }

            if (SelectedFiles.Count == 0)
            {
                SelectedFiles.Add(new RevitFileItem { FilePath = SourcePath, IsSelected = true });
                SelectedFileItem = SelectedFiles.FirstOrDefault();
            }
            else
            {
                SelectedFiles[0] = new RevitFileItem { FilePath = SourcePath, IsSelected = true };
                SelectedFileItem = SelectedFiles[0];
            }

            OnPropertyChanged(nameof(SelectedFiles));
        }

        /// <summary>
        /// Rebuilds <see cref="SelectedFiles"/> by scanning the directory derived from <see cref="SourcePath"/>
        /// for <c>.rvt</c> files, honouring the <see cref="IncludeSubFolders"/> flag.
        /// All files found are added with <c>IsSelected = true</c>.
        /// </summary>
        private void RefreshFilesFromSource()
        {
            string folder = File.Exists(SourcePath)
                ? Path.GetDirectoryName(SourcePath) ?? string.Empty
                : SourcePath;

            if (!Directory.Exists(folder)) return;

            var option = IncludeSubFolders
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            SelectedFiles.Clear();

            foreach (var file in Directory.GetFiles(folder, "*.rvt", option))
                SelectedFiles.Add(new RevitFileItem { FilePath = file, IsSelected = true });

            SelectedFileItem = SelectedFiles.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedFiles));
        }

        // =====================================================================
        // Methods — Views Loading
        // =====================================================================

        /// <summary>
        /// Asynchronously loads the 3D views for <see cref="SelectedFileItem"/> via the Revit external event
        /// <see cref="_loadViewsEvent"/>, then populates <see cref="AvailableViews"/>.
        /// Restores any previously saved view selections from <see cref="_selectedViewsCache"/>.
        /// If views for the file are already in <see cref="_viewsCache"/>, they are used immediately
        /// without raising the external event.
        /// </summary>
        /// <remarks>
        /// Progress, completion, and failure callbacks are dispatched back to the UI thread via
        /// <see cref="_dispatcher"/> at <see cref="DispatcherPriority.Background"/>.
        /// On failure, a warning <see cref="MessageBox"/> is shown and <see cref="IsBusy"/> is reset.
        /// </remarks>
        private void LoadViewsForSelectedFile()
        {
            AvailableViews.Clear();

            if (!IsCustomViews) return;
            if (SelectedFileItem == null || string.IsNullOrWhiteSpace(SelectedFileItem.FilePath)) return;

            string selectedPath = Path.GetFullPath(SelectedFileItem.FilePath);

            _selectedViewsCache.TryGetValue(selectedPath, out var savedSelectedIds);
            savedSelectedIds ??= new HashSet<long>();

            if (_viewsCache.TryGetValue(selectedPath, out var cachedViews))
            {
                foreach (var v in cachedViews)
                {
                    var item = new View3DItem
                    {
                        ElementId = v.ElementId,
                        Name = v.Name,
                        IsSelected = savedSelectedIds.Contains(v.ElementId)
                    };
                    item.PropertyChanged += OnViewSelectionChanged;
                    AvailableViews.Add(item);
                }
                return;
            }

            try
            {
                IsBusy = true;
                ProgressText = "Reading 3D views... (0%)";
                ProgressValue = 0;
                ProgressMaximum = 3;
                DoEvents();

                _loadViewsHandler.FilePath = selectedPath;

                _loadViewsHandler.OnProgress = (current, total, text) =>
                {
                    _dispatcher.BeginInvoke(new Action(() =>
                    {
                        ProgressMaximum = Math.Max(1, total);
                        ProgressValue = current;
                        double percent = total > 0 ? (current * 100.0 / total) : 0;
                        ProgressText = $"{text} ({percent:0}%)";
                    }), DispatcherPriority.Background);
                };

                _loadViewsHandler.OnCompleted = (views, doc) =>
                {
                    _dispatcher.BeginInvoke(new Action(() =>
                    {
                        _viewsCache[selectedPath] = views;

                        // Keep the document open so Export reuses it without
                        // triggering a second model-upgrade prompt.
                        // Close any previously cached doc for this path first.
                        if (_openDocumentsCache.TryGetValue(selectedPath, out var oldDoc))
                        {
                            try { oldDoc.Close(false); } catch { }
                            _openDocumentsCache.Remove(selectedPath);
                        }

                        // Only cache docs we opened ourselves (not the active document)
                        var activeDoc = _uiApp.ActiveUIDocument?.Document;
                        bool isActiveDoc = activeDoc != null
                            && string.Equals(
                                System.IO.Path.GetFullPath(activeDoc.PathName ?? string.Empty),
                                selectedPath,
                                StringComparison.OrdinalIgnoreCase);

                        if (!isActiveDoc)
                            _openDocumentsCache[selectedPath] = doc;

                        AvailableViews.Clear();

                        foreach (var v in views)
                        {
                            var item = new View3DItem
                            {
                                ElementId = v.ElementId,
                                Name = v.Name,
                                IsSelected = savedSelectedIds.Contains(v.ElementId)
                            };
                            item.PropertyChanged += OnViewSelectionChanged;
                            AvailableViews.Add(item);
                        }

                        IsBusy = false;
                        ProgressText = string.Empty;
                        ProgressValue = 0;
                        ProgressMaximum = 1;
                        RaiseCommandStates();
                    }), DispatcherPriority.Background);
                };

                _loadViewsHandler.OnFailed = error =>
                {
                    _dispatcher.BeginInvoke(new Action(() =>
                    {
                        IsBusy = false;
                        ProgressText = string.Empty;
                        ProgressValue = 0;
                        ProgressMaximum = 1;

                        MessageBox.Show(
                            $"Failed to load 3D views for the selected file.\n\n{error}",
                            "NWC Exporter",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        RaiseCommandStates();
                    }), DispatcherPriority.Background);
                };

                _loadViewsEvent.Raise();
            }
            catch (Exception ex)
            {
                IsBusy = false;
                ProgressText = string.Empty;
                ProgressValue = 0;
                ProgressMaximum = 1;

                MessageBox.Show(
                    $"Failed to load 3D views for the selected file.\n\n{ex.Message}",
                    "NWC Exporter",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Persists the currently checked views in <see cref="AvailableViews"/> to
        /// <see cref="_selectedViewsCache"/> for the currently selected file.
        /// Does nothing if <see cref="IsCustomViews"/> is <c>false</c> or no file is selected.
        /// </summary>
        private void SaveCurrentViewSelections()
        {
            if (!IsCustomViews) return;
            if (_selectedFileItem == null || string.IsNullOrWhiteSpace(_selectedFileItem.FilePath)) return;

            string selectedPath = Path.GetFullPath(_selectedFileItem.FilePath);

            var selectedIds = AvailableViews
                .Where(v => v.IsSelected)
                .Select(v => v.ElementId)
                .ToHashSet();

            _selectedViewsCache[selectedPath] = selectedIds;
        }

        // =====================================================================
        // Methods — Export Helpers
        // =====================================================================

        /// <summary>
        /// Returns the list of user-selected <see cref="View3DItem"/> objects for a given file path
        /// by cross-referencing <see cref="_selectedViewsCache"/> with <see cref="_viewsCache"/>.
        /// </summary>
        /// <param name="filePath">The file path whose selected views should be retrieved.</param>
        /// <returns>
        /// A list of <see cref="View3DItem"/> objects with <c>IsSelected = true</c>,
        /// or an empty list if either cache has no entry for <paramref name="filePath"/>.
        /// </returns>
        private List<View3DItem> GetSelectedViewsForFile(string filePath)
        {
            string normalized = Path.GetFullPath(filePath);

            if (_selectedViewsCache.TryGetValue(normalized, out var savedSelectedIds) &&
                _viewsCache.TryGetValue(normalized, out var cachedViews))
            {
                return cachedViews
                    .Where(v => savedSelectedIds.Contains(v.ElementId))
                    .Select(v => new View3DItem
                    {
                        ElementId = v.ElementId,
                        Name = v.Name,
                        IsSelected = true
                    })
                    .ToList();
            }

            return new List<View3DItem>();
        }

        /// <summary>
        /// Builds the flat list of <see cref="ExportTaskItem"/> objects that drives the export pipeline.
        /// In whole-model mode, one task is created per file (with <c>View = null</c>).
        /// In custom-view mode, one task is created per selected view per file.
        /// </summary>
        /// <param name="filesToExport">The subset of <see cref="SelectedFiles"/> that are checked for export.</param>
        /// <returns>A list of <see cref="ExportTaskItem"/> ready to be handed to <see cref="_exportHandler"/>.</returns>
        private List<ExportTaskItem> BuildExportTasks(List<RevitFileItem> filesToExport)
        {
            var tasks = new List<ExportTaskItem>();

            if (IsCustomViews)
            {
                foreach (var file in filesToExport)
                {
                    string normalized = Path.GetFullPath(file.FilePath);
                    var views = GetSelectedViewsForFile(normalized);

                    foreach (var view in views)
                        tasks.Add(new ExportTaskItem { File = file, View = view });
                }
            }
            else
            {
                foreach (var file in filesToExport)
                    tasks.Add(new ExportTaskItem { File = file, View = null });
            }

            return tasks;
        }
        private void OnViewSelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(View3DItem.IsSelected)) return;
            SaveCurrentViewSelections();
            RaiseCommandStates();
        }

        /// <summary>
        /// Determines whether <see cref="ExportCommand"/> can execute.
        /// Returns <c>false</c> if the application is busy, no output path is set,
        /// or no files (and, in custom-view mode, no views) are selected.
        /// </summary>
        /// <returns><c>true</c> if the export can proceed; otherwise <c>false</c>.</returns>
        private bool CanExport()
        {
            if (IsBusy) return false;
            if (string.IsNullOrWhiteSpace(OutputPath)) return false;

            if (IsCustomViews)
                return SelectedFiles.Any(f => f.IsSelected) && _selectedViewsCache.Any(kv => kv.Value.Count > 0);

            return SelectedFiles.Any(f => f.IsSelected);
        }

        // =====================================================================
        // Methods — Export Execution
        // =====================================================================

        /// <summary>
        /// Handles <see cref="ExportCommand"/>.
        /// Validates inputs, constructs the task list, configures <see cref="_exportHandler"/>,
        /// and raises <see cref="_exportEvent"/> to start the Revit-thread export pipeline.
        /// Progress, completion, and per-step continuation are all handled via dispatcher callbacks.
        /// </summary>
        /// <remarks>
        /// The export runs step-by-step: after each task the handler invokes
        /// <c>RequestNextStep</c>, which re-raises the external event for the next task.
        /// On partial failure, a warning dialog lists all error messages from <see cref="ExportResult.Errors"/>.
        /// </remarks>
        private void RunExport()
        {
            ExportDone = false;
            StatusMessage = string.Empty;

            SaveCurrentViewSelections();

            List<RevitFileItem> filesToExport = SelectedFiles
                .Where(f => f.IsSelected)
                .ToList();

            if (!filesToExport.Any())
            {
                MessageBox.Show(
                    "Please select at least one file.",
                    "NWC Exporter",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var tasks = BuildExportTasks(filesToExport);

            if (IsCustomViews && tasks.Count == 0)
            {
                MessageBox.Show(
                    "Please select at least one 3D view across the selected models.",
                    "NWC Exporter",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                IsBusy = true;
                ProgressValue = 0;
                ProgressMaximum = Math.Max(1, tasks.Count);
                ProgressText = "Starting export...";
                DoEvents();

                _exportHandler.Reset();
                _exportHandler.Tasks = tasks;
                _exportHandler.OutputPath = OutputPath;
                _exportHandler.Settings = _settings;
                _exportHandler.OpenDocumentsCache = _openDocumentsCache;

                _exportEvent.Raise();
            }
            catch (Exception ex)
            {
                ExportDone = false;
                StatusMessage = string.Empty;
                IsBusy = false;
                ProgressText = string.Empty;
                ProgressValue = 0;
                ProgressMaximum = 1;

                MessageBox.Show(
                    ex.Message,
                    "Export Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // =====================================================================
        // Methods — Utilities
        // =====================================================================

        /// <summary>
        /// Processes all pending dispatcher operations at <see cref="DispatcherPriority.Background"/>
        /// to keep the UI responsive during synchronous setup phases (e.g., before raising an external event).
        /// </summary>
        private void DoEvents()
        {
            _dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        }

        /// <summary>
        /// Notifies all command objects to re-evaluate their <c>CanExecute</c> state.
        /// Should be called after any state change that may affect command availability.
        /// </summary>
        private void RaiseCommandStates()
        {
            BrowseSourceCommand.RaiseCanExecuteChanged();
            BrowseOutputCommand.RaiseCanExecuteChanged();
            AddFileCommand.RaiseCanExecuteChanged();
            RemoveFileCommand.RaiseCanExecuteChanged();
            ClearAllFilesCommand.RaiseCanExecuteChanged();
            ExportCommand.RaiseCanExecuteChanged();
            ResetDefaultsCommand.RaiseCanExecuteChanged();
        }
    }
}