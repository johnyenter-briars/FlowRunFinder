# Flow Run Finder

An [XrmToolBox](https://www.xrmtoolbox.com) plugin that lets you filter Power Automate cloud flow runs by date and time, and navigate directly to any run in the Power Automate portal with a single click.

---

## Features

- **Filter by date/time range** — query flow runs across any time window, not just the default 28-day view in Power Automate
- **Single flow or All Flows mode** — search runs for a specific flow, or scan across every flow in the environment
- **Live flow search** — type to filter the flow list as you go
- **Deep-link navigation** — click any run to open it directly in the Power Automate portal
- **Add Columns** — fetch trigger output fields (e.g. Case Number, Subject) from the PA REST API and display them as extra columns in the results grid
- **Delete runs** — select one or more runs and delete them via the Power Automate REST API
- **Silent authentication** — signs in once via a browser prompt, then caches the token (DPAPI-encrypted) so subsequent sessions need no interaction
- **Auto-detect environment ID** — resolves the correct Power Platform environment GUID automatically from your Dataverse connection

---

## Installation

1. Open **XrmToolBox**
2. Click **Plugin Store** in the toolbar
3. Search for **Flow Run Finder**
4. Click **Install**
5. Restart XrmToolBox

---

## Usage

1. Connect XrmToolBox to your Dataverse / Dynamics 365 environment as normal
2. Open **Flow Run Finder** from the plugin list
3. Click **Reload Flows** to load all cloud flows from the environment
4. Select a flow from the dropdown (or leave it blank to search all flows)
5. Set a **From** and **To** date/time range
6. Click **Find Runs**
7. Click any row to open that run directly in Power Automate

### Add Columns
Click **Add Columns** after results load to fetch trigger output data (e.g. fields from a Dataverse record that triggered the flow) and display them as additional columns in the grid.

### Delete Runs
Check the rows you want to remove and click **Delete Selected**. Deletion uses the Power Automate REST API.

---

## Requirements

- [XrmToolBox](https://www.xrmtoolbox.com) v1.2025.10.74 or later
- A Power Automate / Power Platform environment connected via XrmToolBox
- Internet access for the Power Automate REST API and first-time authentication

---

## Authentication

The plugin uses [MSAL](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet) with the Azure PowerShell public client ID — a Microsoft-managed app registration that is pre-approved in all tenants and requires no admin consent.

On first use you will be prompted to sign in via a browser window. After that, the token is cached in `%LOCALAPPDATA%\FlowRunFinder\msalcache.dat` (DPAPI-encrypted, per-user) and silently refreshed on subsequent sessions.

To force re-authentication, delete `%LOCALAPPDATA%\FlowRunFinder\msalcache.dat`.

---

## License

MIT — see [LICENSE](LICENSE)
