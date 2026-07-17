# Changelog

## 1.1.0

- Refactored the XrmToolBox tool to use the shared `FlowRunFinderV2.Core` clients and query engine.
- Aligned `Microsoft.Identity.Client` with the Core dependency version.
- Added an explicit `System.Text.Json` package reference to align with the Core DLL.
- Updated authentication setup to pass explicit token cache paths and configured client IDs into the Core auth services.
- Retained configurable token-cache locations and explicit Dataverse and Power Automate client IDs for the XrmToolBox host.
- Added support for V2 settings:
  - Default run count
  - Max runs to query
  - Flow run history table toggle
  - Dataverse client ID
  - Power Automate client ID
  - Log verbosity
- Added interactive browser authentication as the default sign-in flow, while keeping device-code authentication available in Settings.
- Added advanced search support through the shared Core query models.
- Added cancellation for long-running advanced searches.
- Added live advanced-search progress reporting for candidate records, scanned records, scan percentage, and matches.
- Updated advanced-search logging to include the criteria used when runs match or are rejected.
- Reworked the WinForms UI to mirror the FlowRunFinder V2 layout: connection header, flow picker, run refresh, trigger columns, advanced search, status, busy indicator, and toast feedback.
- Moved cache, settings, logs, and token storage for this XrmToolBox tool under `%LOCALAPPDATA%\FlowRunFinder`.
- Added right-click copy behavior for run URLs and grid cell values.
- Updated ILRepack to merge `FlowRunFinderV2.Core.dll` and remove loose merged dependency DLLs from Release output.
- Removed the legacy column picker dialog and old embedded Dataverse/Power Automate implementation.

## 1.0.1

- Added package icon and embedded README.
