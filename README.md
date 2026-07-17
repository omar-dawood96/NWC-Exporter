# NWC Exporter

A Revit plugin that automates batch export of NWC files for Navisworks — supporting multiple Revit files, per-file 3D view selection, and full Navisworks export settings control.

---

## Table of Contents

- [Overview](#overview)
- [Features](#features)
- [Requirements](#requirements)
- [Installation](#installation)
- [Usage](#usage)
- [Project Structure](#project-structure)
- [Architecture](#architecture)
- [Build Configuration](#build-configuration)
- [Known Behaviors](#known-behaviors)

---

## Overview

NWC Exporter adds a button to the **KAITECH-BD-R10** Revit ribbon tab. Clicking it opens a WPF dialog where you can:

- Add multiple `.rvt` files to a batch export queue
- Choose between exporting the whole model or specific 3D views
- Configure all Navisworks export settings in one place
- Run the export for all selected files in a single click

The plugin is built for Revit 2022, 2023, and 2024 using a single codebase with conditional compilation.

---

## Features

| Feature | Description |
|---|---|
| **Multi-file batch export** | Add any number of `.rvt` files; each has its own checkbox to include or skip |
| **Custom view selection** | Load all 3D views from each file and pick exactly which ones to export |
| **Whole model export** | Export the entire model geometry without selecting individual views |
| **Sub-folder scanning** | Automatically discover all `.rvt` files in a folder and its sub-folders |
| **Folder per project** | Optionally create a separate output sub-folder for each Revit file |
| **Full Navisworks settings** | Control coordinates, faceting, textures, levels, parameters, and more |
| **Live progress bar** | Track export progress with step-by-step updates and a percentage counter |
| **Detailed error reporting** | Shows exactly which files or views failed and why — never silent failures |
| **Light / Dark theme** | Toggle between themes at any time with the Toggle Theme button |
| **Revit 2022–2024 support** | Single codebase targeting all three versions via compile-time constants |

---

## Requirements

| Requirement | Version |
|---|---|
| Autodesk Revit | 2022, 2023,2024,2025 or 2026 |
| .NET Framework | 4.8 || NET 8 |
| Windows | 64-bit |
| Visual Studio | 2022 (for building) |

The `RevitAPI.dll` and `RevitAPIUI.dll` references are resolved from the default Revit installation paths:

```
C:\Program Files\Autodesk\Revit <version>\RevitAPI.dll
C:\Program Files\Autodesk\Revit <version>\RevitAPIUI.dll
```

---

## Installation

### Build from source

1. Clone or download the repository.
2. Open `NWCExporter.csproj` in Visual Studio 2022.
3. Select the build configuration matching your Revit version:
   - `Revit2022`
   - `Revit2023`
   - `Revit2024`
   - `Revit2025`
   - `Revit2026`
   
4. Build the project — the output is `NWCExporter.dll`.

### Register with Revit

Create a `.addin` manifest file and place it in:

```
%APPDATA%\Autodesk\Revit\Addins\<year>\NWCExporter.addin
```

Manifest contents:

```xml
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>NWC Exporter</Name>
    <Assembly>C:\path\to\NWCExporter.dll</Assembly>
    <FullClassName>NWCExporter.Revit.App</FullClassName>
    <ClientId>YOUR-GUID-HERE</ClientId>
    <VendorId>KAITECH</VendorId>
    <VendorDescription>NWC Exporter by Omar Ali</VendorDescription>
  </AddIn>
</RevitAddIns>
```

Replace `YOUR-GUID-HERE` with a new GUID (generate one in Visual Studio with **Tools → Create GUID**).

---

## Usage

### Basic export

1. Open Revit and load any project.
2. Go to the **KAITECH-BD-R10** ribbon tab → **Export** panel → click **NWC Exporter**.
3. Set the **Output Path** where NWC files should be saved.
4. The current document is pre-loaded in the **Selected Files** list. Add more files with **+ Add**.
5. Select **Whole Model** or **Custom Views** under Export Scope.
6. Click **Export**.

### Custom view export

1. Switch Export Scope to **Custom Views**.
2. The plugin opens each file and loads all available 3D views.
3. Check the views you want to export — selections persist as you switch between files.
4. Click **Export** — the button activates only when at least one view is checked.

### Multiple files

- Use **+ Add** to append more `.rvt` files from any location.
- Enable **Include Sub Folders** to auto-scan an entire directory tree for `.rvt` files.
- Each file has its own checkbox — uncheck to skip without removing from the list.
- Enable **Folder per project** to create a named sub-folder per file in the output directory.

### Navisworks settings

Switch to the **Navisworks Settings** tab to configure:

- Coordinate system (Shared / Internal)
- Element IDs, parameters, and properties
- Linked files and CAD formats
- Room geometry and attributes
- Textures, levels, and faceting factor
- Click **Defaults** to reset everything to recommended values

---

## Project Structure

```
NWCExporter-Net4.8/
│
├── Revit/
│   └── App.cs                      # IExternalApplication — registers ribbon button
│
├── Commands/
│   ├── OpenExporterCommand.cs       # IExternalCommand — opens the main window
│   └── RelayCommand.cs              # Generic WPF command (ICommand implementation)
│
├── Models/
│   ├── NwcExportSettings.cs         # All Navisworks export settings as a POCO
│   ├── RevitFileItem.cs             # Represents a .rvt file in the selected files list
│   ├── View3DItem.cs                # Represents a 3D view with INotifyPropertyChanged
│   ├── ExportTaskItem.cs            # A single export task (file + optional view)
│   └── ExportResult.cs             # Holds success count and error messages
│
├── ViewModels/
│   ├── BaseViewModel.cs             # INotifyPropertyChanged base with SetProperty<T>
│   ├── MainViewModel.cs             # Primary ViewModel — file list, export logic, progress
│   └── SettingsViewModel.cs        # Wraps NwcExportSettings for two-way XAML binding
│
├── Services/
│   ├── INwcExportService.cs         # Interface for NWC export operations
│   ├── NwcExportService.cs          # Implements export: open doc → build options → Export()
│   ├── IRevitDocumentService.cs     # Interface for Revit document queries
│   └── RevitDocumentService.cs      # Returns current document path and 3D views
│
├── EventHandler/
│   ├── ExportEventHandler.cs        # Revit external event — runs batch export on Revit thread
│   └── LoadViewsEventHandler.cs    # Revit external event — loads 3D views from a file
│
├── Views/
│   ├── MainWindow.xaml              # WPF layout — Source & Output tab + Navisworks Settings tab
│   └── MainWindow.xaml.cs          # Code-behind — theme toggle only
│
├── Helpers/
│   └── ThemeManager.cs             # Swaps between Light and Dark resource dictionaries
│
├── Converters/
│   └── BoolToVisibilityConverter.cs # WPF value converter (bool → Visibility)
│
├── Themes/
│   ├── ThemeLight.xaml
│   └── ThemeDark.xaml
│
├── Resources/
│   ├── NWC-16.png                   # Ribbon button icon (16×16)
│   └── NWC-32.png                   # Ribbon button icon (32×32)
│
└── NWCExporter.csproj
```

---

## Architecture

The plugin follows the **MVVM pattern** with Revit external events for thread safety.

```
Revit API Thread                    UI Thread (WPF Dispatcher)
─────────────────                   ──────────────────────────
ExportEventHandler.Execute()   ←──  MainViewModel raises ExternalEvent
  │                                   │
  ├── TryGetCachedDoc()               ├── IsBusy = true
  ├── OpenDocument() if needed        ├── ProgressText updates
  ├── doc.Export(options)             └── OnCompleted → show result dialog
  └── _result.SuccessCount++

LoadViewsEventHandler.Execute() ←── MainViewModel raises ExternalEvent
  │                                   │
  ├── OpenDocument()                  └── OnCompleted(views, doc)
  ├── CollectViews()                        │
  └── OnCompleted(views, doc)              ├── _viewsCache[path] = views
                                           └── _openDocumentsCache[path] = doc
```

### Key design decisions

**Document caching** — After `LoadViewsEventHandler` opens a file to load its 3D views, the `Document` is kept open and stored in `_openDocumentsCache`. When Export runs, `ExportEventHandler` checks this cache first and reuses the already-open document. This prevents the model-upgrade dialog from appearing twice for Revit 2023 files opened in Revit 2024.

**Task grouping** — `ExportEventHandler` groups tasks by file path before processing. All views for the same file are exported in a single open/close cycle, so each file is opened exactly once regardless of how many views are selected.

**External events** — All Revit API calls are made inside `IExternalEventHandler.Execute()`, which Revit invokes on its own thread. The UI thread communicates via `Dispatcher.BeginInvoke` callbacks passed through `Action` delegates on the handler.

**Dependency injection** — `INwcExportService` and `IRevitDocumentService` are constructor-injected into `MainViewModel` and `ExportEventHandler`, making both testable without a live Revit session.

---

## Build Configuration

The `.csproj` defines five configurations:

| Configuration | `DefineConstants` | Revit DLL path |
|---|---|---|
| `Debug` | _(none)_ | Revit 2024 |
| `Release` | _(none)_ | Revit 2024 |
| `Revit2022` | `REVIT2022` | Revit 2022 |
| `Revit2023` | `REVIT2023` | Revit 2023 |
| `Revit2024` | `REVIT2025` | Revit 2025 |
| `Revit2024` | `REVIT2026` | Revit 2026 |

The constants gate two differences in the code:

```csharp
// ElementId API changed in Revit 2024 (int → long)
#if REVIT2022 || REVIT2023
    ElementId = v.Id.IntegerValue,   // returns int
#else
    ElementId = v.Id.Value,          // returns long
#endif

// ViewId constructor also uses int vs long
#if REVIT2022 || REVIT2023
    opts.ViewId = new ElementId((int)view.ElementId);
#else
    opts.ViewId = new ElementId(view.ElementId);
#endif
```

---

## Known Behaviors

**Model upgrade prompt** — When a Revit 2023 file is opened in Revit 2024, Revit shows a "Model Upgrade" dialog. The plugin suppresses this automatically by subscribing to `DialogBoxShowing` and overriding the result. The file is opened only once per export session regardless of how many views are selected.

**Export button state** — In Custom Views mode, the Export button stays disabled until at least one view checkbox is checked. The button re-evaluates its state immediately on every checkbox change via `INotifyPropertyChanged` on `View3DItem`.

**Sub-folder scanning** — When Include Sub Folders is enabled, the file list is rebuilt from scratch by scanning the directory of the current source path. Any manually added files from other locations are replaced.

**Output folder** — If Folder per Project is enabled, a sub-folder named after each `.rvt` file is created automatically inside the output path if it does not already exist.

---

## Author

**Omar Ali**  
Built as part of the KAITECH BIM toolkit.
