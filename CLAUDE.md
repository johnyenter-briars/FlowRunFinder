# FlowRunFinder — Claude Session Context

## Project Overview
An XrmToolBox plugin that lets users filter Power Automate cloud flow runs by date/time and navigate directly to specific runs in the Power Automate portal. Built in C# targeting .NET Framework 4.8 with WinForms.

**Developer:** Mark Mitrovic  
**Project folder:** `C:\Users\mmitrovic\FlowRunFinder\`  
**Installed plugin folder:** `C:\Users\mmitrovic\AppData\Roaming\MscrmTools\XrmToolBox\Plugins\`

---

## Current State — WORKING ✅
The plugin is fully functional and installed locally. All core features work:
- Filter flow runs by date/time range
- Single flow mode and "All Flows" mode
- "Go to flow" links navigate directly to the specific run (deep-link)
- Auto-detect environment ID via PA environments API (cached to `%LOCALAPPDATA%\FlowRunFinder\envids.json`)
- Add Columns button — fetches trigger output columns (Case fields etc.) via PA REST API
- Delete selected runs via PA REST API with Dataverse SDK fallback
- One-time MSAL browser sign-in, permanently cached to `%LOCALAPPDATA%\FlowRunFinder\msalcache.dat` (DPAPI-encrypted)
- Row selection UI: light blue highlight, link clicks and checkbox clicks don't leave row highlighted

---

## Key Technical Decisions

### Authentication
- XrmToolBox handles Dataverse connection automatically
- Power Automate REST API requires a **separate OAuth token** acquired via MSAL
- **Client ID used:** `1950a258-227b-4e31-a9cf-717495945fc2` (Azure PowerShell public client — pre-approved in all tenants, no admin consent needed)
- We attempted registering our own Azure AD app (`4862b59a-f568-413d-bae0-dff96a89a5c5` in Reply tenant) but enterprise tenants like Sirva require admin consent for new apps — too much friction for public distribution. Reverted to Azure PowerShell client ID.
- Scopes: `https://service.flow.microsoft.com/user_impersonation`
- MSAL flow: silent (in-memory) → silent (disk cache) → IWA → interactive browser (one-time only)

### Environment ID
- `detail.EnvironmentId` from XrmToolBox is UNRELIABLE — returns Dataverse OrganizationId, not the Power Platform environment GUID
- Correct env ID detected via: `GET https://api.flow.microsoft.com/providers/Microsoft.ProcessSimple/environments?api-version=2016-11-01` — matches by `instanceUrl` against Dataverse org URL
- Cached permanently to `%LOCALAPPDATA%\FlowRunFinder\envids.json` keyed by org URL

### "Go to flow" URL format
```
https://make.powerautomate.com/environments/{envId}/flows/{flowId}/runs/{runName}
```
- `runName` = the `name` attribute of the `flowrun` Dataverse entity (a GUID-like string)
- URL built at click time (not population time) so env ID is always current

### Delete flow runs
- PA REST API: `DELETE https://api.flow.microsoft.com/providers/Microsoft.ProcessSimple/environments/{env}/flows/{flow}/runs/{runName}?api-version=2016-11-01`
- Falls back to Dataverse SDK if PA API unavailable
- Note: flowrun records are owned by a service principal — human users cannot delete them via Dataverse SDK even with System Administrator role. PA REST API is the correct path.

### Dataverse entity
- Entity: `flowrun`
- Key fields: `flowrunid` (ID), `name` (run name/identifier), `starttime`, `endtime`, `status`, `workflowid` (flow reference, returns as string GUID)

---

## File Structure
```
C:\Users\mmitrovic\FlowRunFinder\
├── FlowRunFinder.csproj          — project + NuGet metadata
├── FlowRunFinderPlugin.cs        — MEF export, metadata, images
├── FlowRunFinderControl.cs       — all UI and business logic (~1500 lines)
├── FlowRunFinderControl.Designer.cs — WinForms designer
├── CLAUDE.md                     — this file
├── lib\
│   ├── XrmToolBox.Extensibility.dll  (v1.2025.10.74)
│   └── McTools.Xrm.Connection.dll
├── images\
│   ├── big_new.png               — 80x80 source icon
│   ├── small_new.png             — 32x32 source icon
│   ├── big_b64.txt               — Base64 of big icon (used in FlowRunFinderPlugin.cs)
│   └── small_b64.txt             — Base64 of small icon (used in FlowRunFinderPlugin.cs)
└── client_id.txt                 — Azure AD app client ID (not used — reverted to AzPS)
```

---

## Build & Install
```powershell
cd C:\Users\mmitrovic\FlowRunFinder
dotnet build -c Release
Copy-Item "bin\Release\net48\FlowRunFinder.dll" "$env:APPDATA\MscrmTools\XrmToolBox\Plugins\" -Force
# Also copy MSAL DLLs if not already present (see below)
Remove-Item "$env:APPDATA\MscrmTools\XrmToolBox\Plugins\manifest.json" -Force -ErrorAction SilentlyContinue
```

### MSAL DLLs that must be copied alongside FlowRunFinder.dll
These are currently copied manually to the Plugins folder:
- `Microsoft.Identity.Client.dll`
- `Microsoft.IdentityModel.Abstractions.dll`
- `Microsoft.Bcl.AsyncInterfaces.dll`
- `System.Runtime.CompilerServices.Unsafe.dll`
- `System.Threading.Tasks.Extensions.dll`
- `System.Memory.dll`
- `System.Buffers.dll`
- `System.Numerics.Vectors.dll`

---

## NuGet / Public Publishing — MOSTLY DONE ✅

### What the XrmToolBox docs require (from GitHub wiki)
Source: https://github.com/MscrmTools/XrmToolBox/wiki/Distribute-your-plugins-through-XrmToolBox-and-NuGet

1. ✅ Tags start with "XrmToolBox" (`XrmToolBox PowerAutomate FlowRuns Dynamics365 PowerPlatform`)
2. ✅ Metadata: Title, Version, Authors, Description, Copyright all set
3. ✅ Version consistency — AssemblyVersion `1.0.0.0` matches package Version `1.0.0`
4. ✅ All files go under `lib\net452\Plugins\` — `.nuspec` produces correct structure
5. ✅ Package declares dependency on `XrmToolBox 1.2025.10.74`
6. ✅ Third-party libs (MSAL + BCL backports) merged into single DLL via ILRepack

### How builds work now
- `dotnet build -c Release` → compiles, ILRepack merges 12 dependency DLLs, deletes loose copies
- `FlowRunFinder.dll` output is ~2.5 MB, fully self-contained
- `.\publish.ps1` → builds Release, downloads `nuget.exe` if needed, packs `FlowRunFinder.1.0.0.nupkg`
- `.\publish.ps1 -Push` → also pushes to NuGet.org (prompts for API key)

### Key files added for publishing
- `FlowRunFinder.nuspec` — NuGet package definition with correct `lib\net452\Plugins` structure
- `ILRepack.targets` — custom MSBuild target file that merges MSAL DLLs on every Release build
- `publish.ps1` — one-shot build + pack + push script
- `tools\nuget.exe` — downloaded automatically by publish.ps1 on first run

### Remaining tasks for publishing

#### 1. Create GitHub repository (NEXT STEP)
- Create a public repo at github.com (e.g. `github.com/MarkMitrovic/FlowRunFinder`)
- Update two placeholders in `FlowRunFinder.csproj`:
  ```xml
  <PackageProjectUrl>https://github.com/markmitrovic/FlowRunFinder</PackageProjectUrl>
  <RepositoryUrl>https://github.com/markmitrovic/FlowRunFinder</RepositoryUrl>
  ```
- Update `projectUrl` in `FlowRunFinder.nuspec` to match
- Push source code to repo

#### 2. NuGet account + publish
- Create account at nuget.org
- Generate API key
- Run: `.\publish.ps1 -Push`

#### 3. PluginIcon / TrayIcon (optional, cosmetic)
- The XrmToolBox docs mention setting PluginIcon and TrayIcon in the WinForms Designer
- These show in XrmToolBox's dedicated window titlebar and tab dropdown
- Not required for the Plugin Store listing

---

## Important Warnings
- **DO NOT use Early Bound Entities** — XrmToolBox requirement, causes conflicts with other tools. We don't use them. ✅
- **Do not include CRM SDK assemblies** in the NuGet package — XrmToolBox already includes them
- **XrmToolBox manifest.json** caches plugin metadata. Delete it after updates: `Remove-Item "$env:APPDATA\MscrmTools\XrmToolBox\Plugins\manifest.json" -Force`
- **MSAL cache** at `%LOCALAPPDATA%\FlowRunFinder\msalcache.dat` — delete this to force re-authentication
- **Env ID cache** at `%LOCALAPPDATA%\FlowRunFinder\envids.json` — delete to force re-detection
- **Version updates**: When releasing a new version, update BOTH `<Version>` AND `<AssemblyVersion>` in `FlowRunFinder.csproj` AND `<version>` in `FlowRunFinder.nuspec` to the same value — XrmToolBox uses the assembly version to detect updates.

---

## Next Session Starting Point
The plugin is ready to publish. The next tasks are:
1. Create the GitHub repo and push the code
2. Update the two GitHub URL placeholders in `FlowRunFinder.csproj` and `FlowRunFinder.nuspec`
3. Create a nuget.org account + API key
4. Run `.\publish.ps1 -Push`
