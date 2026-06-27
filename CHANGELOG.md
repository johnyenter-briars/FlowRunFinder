# Changelog

## 1.0.2

- Refactored the XrmToolBox tool to use the shared `FlowRunFinderV2.Core` clients and query engine.
- Reworked the WinForms UI to mirror the FlowRunFinder V2 layout: connection header, flow picker, run refresh, trigger columns, advanced search, status, busy indicator, and toast feedback.
- Added support for V2 settings:
  - Default run count
  - Max runs to query
  - Flow run history table toggle
  - Dataverse client ID
  - Power Automate client ID
  - Log verbosity
- Updated authentication setup to pass explicit token cache paths and configured client IDs into the Core auth services.
- Moved cache, settings, logs, and token storage for this XrmToolBox tool under `%LOCALAPPDATA%\FlowRunFinder`.
- Added right-click copy behavior for run URLs and grid cell values.
- Added advanced search support through the shared Core query models.
- Added direct reference support for the `FlowRunFinderV2.Core` DLL.
- Aligned `Microsoft.Identity.Client` with the Core dependency version.
- Added an explicit `System.Text.Json` package reference to align with the Core DLL.
- Updated ILRepack to merge `FlowRunFinderV2.Core.dll` and remove loose merged dependency DLLs from Release output.
- Removed the legacy column picker dialog and old embedded Dataverse/Power Automate implementation.

## 1.0.1

- Added package icon and embedded README.
