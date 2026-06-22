using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Identity.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using XrmToolBox.Extensibility;

namespace FlowRunFinder
{
    public partial class FlowRunFinderControl : PluginControlBase
    {
        // ── Dataverse entity / attribute names ──────────────────────────────
        private const string FlowEntity   = "workflow";
        private const string FlowId       = "workflowid";
        private const string FlowName     = "name";
        private const int    FlowCategory = 5;

        private const string RunEntity    = "flowrun";
        private const string RunId        = "flowrunid";
        private const string RunName      = "name";
        private const string RunStarted   = "starttime";
        private const string RunCompleted = "endtime";
        private const string RunStatus    = "status";
        private const string RunFlowRef   = "workflowid";

        // ── State ────────────────────────────────────────────────────────────
        private List<FlowItem>             _flows    = new List<FlowItem>();
        private string                     _environmentId;
        private DataCollection<Entity>     _lastRuns;
        private FlowItem                   _lastFlow;
        // Keyed by run NAME (e.g. "08584252…") — matches flowrun.name and PA API
        private Dictionary<string, string> _lastWebApiInputs = new Dictionary<string, string>();

        // ── Trigger-data column state ────────────────────────────────────────
        private readonly Dictionary<int, Dictionary<string, string>> _runTriggerData
            = new Dictionary<int, Dictionary<string, string>>();
        private List<string> _availableExtraColumns = new List<string>();
        private List<string> _selectedExtraColumns  = new List<string>();

        // ── Query mode ───────────────────────────────────────────────────────────
        private bool _flowsLoaded  = false;   // enables Find Runs once flows are ready
        private bool _allFlowsMode = false;   // true when no specific flow is selected

        // ── Combo-box filtering guard (prevents re-entrant TextChanged) ────────
        private bool _cmbFlowUpdating = false;

        // ── Power Automate token state ────────────────────────────────────────
        // _paToken: short-lived access token (1 h), kept in memory only.
        // _msalApp: MSAL public client — holds an encrypted on-disk token cache so
        //   silent re-acquisition (no browser, no prompt) works across restarts.
        private string _paToken;
        private IPublicClientApplication _msalApp;

        // DPAPI-encrypted MSAL V3 token cache (survives XrmToolBox restarts).
        private static readonly string TokenFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowRunFinder", "msalcache.dat");

        // ── Environment ID cache ──────────────────────────────────────────────
        // XrmToolBox sometimes provides the wrong environment ID (it gives the
        // Dataverse OrganizationId, not the Power Platform environment name GUID).
        // We persist the correct ID (from the PA environments API) keyed by org URL
        // so it survives restarts without needing a fresh PA token each time.
        private static readonly string EnvIdCachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowRunFinder", "envids.json");

        private string GetCachedEnvId()
        {
            try
            {
                var orgUrl = ConnectionDetail?.WebApplicationUrl?.TrimEnd('/').ToLowerInvariant();
                if (string.IsNullOrEmpty(orgUrl) || !File.Exists(EnvIdCachePath)) return null;
                var cache  = new JavaScriptSerializer()
                    .Deserialize<Dictionary<string, string>>(File.ReadAllText(EnvIdCachePath));
                return cache != null && cache.TryGetValue(orgUrl, out var id) ? id : null;
            }
            catch { return null; }
        }

        private void SaveDetectedEnvId(string envId)
        {
            try
            {
                var orgUrl = ConnectionDetail?.WebApplicationUrl?.TrimEnd('/').ToLowerInvariant();
                if (string.IsNullOrEmpty(orgUrl) || string.IsNullOrEmpty(envId)) return;
                Dictionary<string, string> cache;
                try
                {
                    cache = File.Exists(EnvIdCachePath)
                        ? new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(
                              File.ReadAllText(EnvIdCachePath))
                          ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                catch { cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
                cache[orgUrl] = envId;
                Directory.CreateDirectory(Path.GetDirectoryName(EnvIdCachePath));
                File.WriteAllText(EnvIdCachePath, new JavaScriptSerializer().Serialize(cache));
            }
            catch { /* non-fatal */ }
        }

        // ── Constructor ─────────────────────────────────────────────────────
        public FlowRunFinderControl()
        {
            InitializeComponent();
            dtpStartDate.Value = DateTime.Today;
            dtpStartTime.Value = DateTime.Today;
            dtpEndDate.Value   = DateTime.Today;
            dtpEndTime.Value   = DateTime.Now;

            // Use a very light blue for row selection so text and links remain readable
            dgvResults.DefaultCellStyle.SelectionBackColor = Color.FromArgb(210, 228, 255);
            dgvResults.DefaultCellStyle.SelectionForeColor = Color.Black;
        }

        // ── XrmToolBox events ────────────────────────────────────────────────
        public override void UpdateConnection(IOrganizationService newService,
            McTools.Xrm.Connection.ConnectionDetail detail, string actionName, object parameter)
        {
            base.UpdateConnection(newService, detail, actionName, parameter);
            _paToken     = null;    // clear cached access token when switching environments
            _msalApp     = null;    // rebuild MSAL app with potentially new tenant on next use
            _flowsLoaded = false;

            // Only use the cached env ID (verified via PA API).
            // XrmToolBox's detail.EnvironmentId is unreliable — it returns the Dataverse
            // OrganizationId GUID which is different from the Power Platform environment
            // name GUID used in Power Automate URLs. Trusting it silently produces broken links.
            var cachedEnvId = GetCachedEnvId();
            _environmentId        = cachedEnvId;
            txtEnvironmentId.Text = cachedEnvId ?? string.Empty;

            lblEnvironment.Text = detail?.OrganizationFriendlyName ?? "Connected";
            ClearResults();
            LoadFlows();
        }

        // ── Load Flows ───────────────────────────────────────────────────────
        private void LoadFlows()
        {
            if (Service == null) return;
            SetWorkingState(true, "Loading cloud flows…");
            _cmbFlowUpdating = true;
            cmbFlow.Items.Clear();
            cmbFlow.Text     = string.Empty;
            _cmbFlowUpdating = false;
            btnFindRuns.Enabled = false;

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading cloud flows from environment…",
                Work = (worker, args) =>
                {
                    var query = new QueryExpression(FlowEntity)
                    {
                        ColumnSet = new ColumnSet(FlowId, FlowName),
                        Orders    = { new OrderExpression(FlowName, OrderType.Ascending) },
                        PageInfo  = new PagingInfo { Count = 5000, PageNumber = 1 }
                    };
                    query.Criteria.AddCondition("category", ConditionOperator.Equal, FlowCategory);

                    var flows = new List<FlowItem>();
                    EntityCollection result;
                    do
                    {
                        result = Service.RetrieveMultiple(query);
                        foreach (var e in result.Entities)
                            flows.Add(new FlowItem(e.Id, e.GetAttributeValue<string>(FlowName) ?? e.Id.ToString()));
                        query.PageInfo.PageNumber++;
                        query.PageInfo.PagingCookie = result.PagingCookie;
                    }
                    while (result.MoreRecords);

                    // ── Auto-detect the Power Platform environment ID ────────────────────
                    // Tries multiple approaches in order, all using auth we already have.
                    string envIdFromDv = null;

                    // Approach 1: Dataverse Web API — query organization record and check
                    // several candidate field names (different Dataverse versions use different names).
                    // Uses the existing Dataverse Bearer token — no extra sign-in required.
                    try
                    {
                        var webBase  = GetWebApiBaseUrl();
                        var dvToken  = TryGetAccessToken();
                        if (!string.IsNullOrEmpty(webBase) && !string.IsNullOrEmpty(dvToken))
                        {
                            using (var http = new HttpClient())
                            {
                                http.DefaultRequestHeaders.Authorization =
                                    new AuthenticationHeaderValue("Bearer", dvToken);
                                http.DefaultRequestHeaders.Accept.Add(
                                    new MediaTypeWithQualityHeaderValue("application/json"));
                                http.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
                                http.DefaultRequestHeaders.Add("OData-Version",    "4.0");

                                // _environmentid_value = OData lookup field convention
                                var resp = http.GetAsync(
                                    $"{webBase}/organizations?$select=organizationid,_environmentid_value,msdyn_environmentid,environmentid")
                                    .GetAwaiter().GetResult();

                                if (resp.IsSuccessStatusCode)
                                {
                                    var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                    var ser  = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                                    var root = ser.Deserialize<Dictionary<string, object>>(json);

                                    if (root?.TryGetValue("value", out var valObj) == true &&
                                        valObj is System.Collections.ArrayList list && list.Count > 0 &&
                                        list[0] is Dictionary<string, object> orgRec)
                                    {
                                        var orgId = orgRec.TryGetValue("organizationid", out var oid)
                                            ? oid?.ToString() : null;

                                        foreach (var key in new[]
                                            { "_environmentid_value", "msdyn_environmentid", "environmentid" })
                                        {
                                            if (orgRec.TryGetValue(key, out var v) && v != null)
                                            {
                                                var id = v.ToString();
                                                if (Guid.TryParse(id, out _)
                                                    && !string.Equals(id, orgId, StringComparison.OrdinalIgnoreCase)
                                                    && id != Guid.Empty.ToString("D"))
                                                {
                                                    envIdFromDv = id;
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    // Approach 2: BAP (Business App Platform) API with MSAL silent.
                    // Uses a different resource scope than the PA API — may succeed silently
                    // even when the PA API scope requires interactive MFA.
                    if (envIdFromDv == null)
                    {
                        try
                        {
                            var orgUrl = ConnectionDetail?.WebApplicationUrl
                                ?.TrimEnd('/').ToLowerInvariant();
                            if (!string.IsNullOrEmpty(orgUrl))
                            {
                                var app  = GetOrCreateMsalApp();
                                var dvTk = TryGetAccessToken();
                                var upn  = GetClaimFromToken(dvTk, "upn")
                                        ?? GetClaimFromToken(dvTk, "unique_name");
                                string bapToken = null;

                                // (a) MSAL cache
                                var accounts = app.GetAccountsAsync().GetAwaiter().GetResult();
                                var hint = string.IsNullOrEmpty(upn)
                                    ? accounts.FirstOrDefault()
                                    : accounts.FirstOrDefault(a => a.Username.Equals(
                                          upn, StringComparison.OrdinalIgnoreCase))
                                      ?? accounts.FirstOrDefault();
                                if (hint != null)
                                {
                                    try
                                    {
                                        var r = app.AcquireTokenSilent(
                                            new[] { "https://api.bap.microsoft.com/.default" }, hint)
                                            .ExecuteAsync().GetAwaiter().GetResult();
                                        bapToken = r.AccessToken;
                                    }
                                    catch { }
                                }

                                // (b) Windows Integrated Auth
                                if (bapToken == null)
                                {
                                    try
                                    {
                                        var iwa = app.AcquireTokenByIntegratedWindowsAuth(
                                            new[] { "https://api.bap.microsoft.com/.default" });
                                        if (!string.IsNullOrEmpty(upn)) iwa = iwa.WithUsername(upn);
                                        var r = iwa.ExecuteAsync().GetAwaiter().GetResult();
                                        bapToken = r.AccessToken;
                                    }
                                    catch { }
                                }

                                if (!string.IsNullOrEmpty(bapToken))
                                {
                                    using (var http = new HttpClient())
                                    {
                                        http.DefaultRequestHeaders.Authorization =
                                            new AuthenticationHeaderValue("Bearer", bapToken);
                                        http.DefaultRequestHeaders.Accept.Add(
                                            new MediaTypeWithQualityHeaderValue("application/json"));

                                        var resp = http.GetAsync(
                                            "https://api.bap.microsoft.com/providers/" +
                                            "Microsoft.BusinessAppPlatform/environments" +
                                            "?api-version=2020-10-01")
                                            .GetAwaiter().GetResult();

                                        if (resp.IsSuccessStatusCode)
                                        {
                                            var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                            var ser  = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                                            var obj  = ser.Deserialize<Dictionary<string, object>>(json);

                                            if (obj?.TryGetValue("value", out var valObj) == true &&
                                                valObj is System.Collections.ArrayList envList)
                                            {
                                                foreach (Dictionary<string, object> env in envList)
                                                {
                                                    if (!env.TryGetValue("name", out var nameObj)) continue;
                                                    if (env.TryGetValue("properties", out var propsObj) &&
                                                        propsObj is Dictionary<string, object> props &&
                                                        props.TryGetValue("linkedEnvironmentMetadata", out var metaObj) &&
                                                        metaObj is Dictionary<string, object> meta &&
                                                        meta.TryGetValue("instanceUrl", out var urlObj))
                                                    {
                                                        var instanceUrl = urlObj?.ToString()
                                                            ?.TrimEnd('/').ToLowerInvariant();
                                                        if (instanceUrl == orgUrl)
                                                        {
                                                            envIdFromDv = nameObj.ToString();
                                                            break;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    args.Result = new object[] { flows, envIdFromDv };
                },
                PostWorkCallBack = args =>
                {
                    SetWorkingState(false);
                    if (args.Error != null) { ShowError("Failed to load flows", args.Error); return; }

                    var data    = (object[])args.Result;
                    _flows      = (List<FlowItem>)data[0];
                    var envIdFromDv = (string)data[1];

                    // Use the Dataverse-sourced env ID if we don't already have a cached one.
                    // Validate it looks like a GUID before trusting it.
                    if (!string.IsNullOrEmpty(envIdFromDv)
                        && Guid.TryParse(envIdFromDv, out _)
                        && string.IsNullOrEmpty(GetCachedEnvId()))
                    {
                        _environmentId        = envIdFromDv;
                        txtEnvironmentId.Text = envIdFromDv;
                        SaveDetectedEnvId(envIdFromDv);
                    }

                    _flowsLoaded = true;
                    RebuildFlowDropdown(_flows);
                    lblFlowCount.Text   = $"{_flows.Count} flows";
                    btnFindRuns.Enabled = true;
                }
            });
        }

        private void RebuildFlowDropdown(IEnumerable<FlowItem> flows)
        {
            _cmbFlowUpdating = true;
            try
            {
                cmbFlow.Items.Clear();
                foreach (var f in flows) cmbFlow.Items.Add(f);
                cmbFlow.SelectedIndex = -1;
                cmbFlow.Text = string.Empty;
            }
            finally { _cmbFlowUpdating = false; }
        }

        // ── Flow combo: live filter as the user types ────────────────────────
        private void cmbFlow_TextChanged(object sender, EventArgs e)
        {
            if (_cmbFlowUpdating) return;
            _cmbFlowUpdating = true;
            try
            {
                var q   = cmbFlow.Text;
                var filtered = string.IsNullOrEmpty(q)
                    ? _flows
                    : _flows.Where(f => f.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                cmbFlow.Items.Clear();
                foreach (var f in filtered) cmbFlow.Items.Add(f);

                // Restore typed text and keep cursor at the end
                cmbFlow.Text             = q;
                cmbFlow.SelectionStart   = q.Length;
                cmbFlow.SelectionLength  = 0;

                if (!string.IsNullOrEmpty(q) && cmbFlow.Items.Count > 0)
                    cmbFlow.DroppedDown = true;

                // Keep Find Runs enabled for all-flows mode even without a selection
                btnFindRuns.Enabled = _flowsLoaded;
            }
            finally { _cmbFlowUpdating = false; }
        }

        private void cmbFlow_SelectedIndexChanged(object sender, EventArgs e)
            => btnFindRuns.Enabled = _flowsLoaded;

        // ── Date helpers ─────────────────────────────────────────────────────
        private void btnToday_Click(object sender, EventArgs e)
        {
            dtpStartDate.Value = DateTime.Today;
            dtpStartTime.Value = DateTime.Today;
            dtpEndDate.Value   = DateTime.Today;
            dtpEndTime.Value   = DateTime.Now;
        }

        private void btnLastHour_Click(object sender, EventArgs e)
        {
            var now = DateTime.Now;
            dtpStartDate.Value = now.AddHours(-1);
            dtpStartTime.Value = now.AddHours(-1);
            dtpEndDate.Value   = now;
            dtpEndTime.Value   = now;
        }

        // ── Find Runs ────────────────────────────────────────────────────────
        // async void is correct for WinForms event handlers — exceptions are caught internally.
        private async void btnFindRuns_Click(object sender, EventArgs e)
        {
            var selectedFlow = cmbFlow.SelectedItem as FlowItem;   // null = all flows
            _allFlowsMode    = selectedFlow == null;

            var startDate = dtpStartDate.Value.Date + dtpStartTime.Value.TimeOfDay;
            var endDate   = dtpEndDate.Value.Date   + dtpEndTime.Value.TimeOfDay;

            if (startDate >= endDate)
            {
                MessageBox.Show("'From' must be earlier than 'To'.", "Invalid Range",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // ── Ensure PA token (single-flow mode) ───────────────────────────────
            // The PA token is used to auto-detect the correct environment ID (needed
            // for "Go to flow" links) and to fetch trigger output columns (Add Columns).
            // MSAL caches the token to disk after the first sign-in, so the browser
            // prompt only ever appears once — all future sessions are fully silent.
            if (!_allFlowsMode && string.IsNullOrEmpty(_paToken))
            {
                try   { _paToken = await EnsurePaTokenAsync(); }
                catch { _paToken = null; }
            }

            // ── Auto-detect environment ID ────────────────────────────────────────
            if (!_allFlowsMode && !string.IsNullOrEmpty(_paToken)
                && string.IsNullOrEmpty(txtEnvironmentId.Text.Trim()))
            {
                lblStatus.Text = "Detecting environment ID…";
                try
                {
                    var detected = await AutoDetectEnvironmentIdAsync(_paToken);
                    if (!string.IsNullOrEmpty(detected))
                    {
                        _environmentId        = detected;
                        txtEnvironmentId.Text = detected;
                        SaveDetectedEnvId(detected);
                    }
                }
                catch { }
            }

            // ── Capture UI values after auto-detection ────────────────────────
            var envId = txtEnvironmentId.Text.Trim();
            // If the user manually typed/pasted a correct env ID, remember it permanently
            if (!string.IsNullOrEmpty(envId) && GetCachedEnvId() != envId)
                SaveDetectedEnvId(envId);
            var capturedPaToken = _paToken;

            ClearResults();
            SetWorkingState(true, "Searching for flow runs…");

            WorkAsync(new WorkAsyncInfo
            {
                Message = $"Searching runs for '{selectedFlow.Name}'…",
                Work = (worker, args) =>
                {
                    // 1. SDK: run metadata
                    var query = new QueryExpression(RunEntity)
                    {
                        ColumnSet = new ColumnSet(true),
                        Orders    = { new OrderExpression(RunStarted, OrderType.Descending) },
                        TopCount  = _allFlowsMode ? 500 : 50
                    };
                    // In single-flow mode filter by flow; all-flows mode uses only date range
                    if (selectedFlow != null)
                        query.Criteria.AddCondition(RunFlowRef, ConditionOperator.Equal, selectedFlow.Id);
                    query.Criteria.AddCondition(RunStarted, ConditionOperator.GreaterEqual, startDate.ToUniversalTime());
                    query.Criteria.AddCondition(RunStarted, ConditionOperator.LessEqual,    endDate.ToUniversalTime());

                    var sdkResult = Service.RetrieveMultiple(query);

                    // 2. Power Automate REST API: trigger outputs (single-flow mode only)
                    string webApiDiag = null;
                    var webApiInputs  = new Dictionary<string, string>();
                    if (!_allFlowsMode && !string.IsNullOrEmpty(capturedPaToken))
                        webApiInputs = FetchTriggerOutputsFromPowerAutomateApi(
                            envId, selectedFlow.Id, capturedPaToken, out webApiDiag);

                    args.Result = new object[] { sdkResult, selectedFlow, webApiInputs, webApiDiag };
                },
                PostWorkCallBack = args =>
                {
                    SetWorkingState(false);
                    if (args.Error != null) { ShowError("Failed to retrieve flow runs", args.Error); return; }

                    var data         = (object[])args.Result;
                    var result       = (EntityCollection)data[0];
                    var flow         = (FlowItem)data[1];
                    var webApiInputs = (Dictionary<string, string>)data[2];
                    var webApiDiag   = (string)data[3];

                    _lastRuns         = result.Entities;
                    _lastFlow         = flow;
                    _lastWebApiInputs = webApiInputs;
                    RenderResults(result.Entities, flow, webApiInputs);

                    // Token is cleared inside FetchTriggerOutputs on 401/403; nothing extra needed here.

                    if (!string.IsNullOrEmpty(webApiDiag))
                        lblStatus.Text = webApiDiag;
                }
            });
        }

        // ── Power Automate token acquisition via MSAL ────────────────────────
        // Azure PowerShell — a well-known public client pre-approved in every tenant.
        private const string PaClientId = "1950a258-227b-4e31-a9cf-717495945fc2";
        private static readonly string[] PaScopes =
            { "https://service.flow.microsoft.com/user_impersonation" };

        // Returns (or lazily creates) the MSAL public client app.
        // A DPAPI-encrypted token cache is wired up so tokens survive restarts —
        // no sign-in prompt after the very first use.
        private IPublicClientApplication GetOrCreateMsalApp()
        {
            if (_msalApp != null) return _msalApp;

            var dvToken  = TryGetAccessToken();
            var tenantId = GetClaimFromToken(dvToken, "tid") ?? "common";

            _msalApp = PublicClientApplicationBuilder.Create(PaClientId)
                .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
                .WithRedirectUri("http://localhost")
                .Build();

            // ── Persist token cache across sessions (DPAPI-encrypted) ─────────
            _msalApp.UserTokenCache.SetBeforeAccess(notif =>
            {
                try
                {
                    if (File.Exists(TokenFilePath))
                    {
                        var enc = File.ReadAllBytes(TokenFilePath);
                        var dec = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
                        notif.TokenCache.DeserializeMsalV3(dec);
                    }
                }
                catch { /* corrupt cache — start fresh */ }
            });

            _msalApp.UserTokenCache.SetAfterAccess(notif =>
            {
                if (!notif.HasStateChanged) return;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(TokenFilePath));
                    var data = notif.TokenCache.SerializeMsalV3();
                    var enc  = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                    File.WriteAllBytes(TokenFilePath, enc);
                }
                catch { /* non-fatal */ }
            });

            return _msalApp;
        }

        private async Task<string> EnsurePaTokenAsync()
        {
            // 1. In-memory access token still valid — fastest path
            if (!string.IsNullOrEmpty(_paToken)) return _paToken;

            var app     = GetOrCreateMsalApp();
            var dvToken = TryGetAccessToken();
            var upn     = GetClaimFromToken(dvToken, "upn")
                       ?? GetClaimFromToken(dvToken, "unique_name");

            // 2. MSAL token cache — silent, no UI.
            //    Succeeds on every run after the first successful authentication
            //    because the token cache is persisted to disk (DPAPI-encrypted).
            try
            {
                var accounts = await app.GetAccountsAsync();
                var hint = string.IsNullOrEmpty(upn)
                    ? accounts.FirstOrDefault()
                    : accounts.FirstOrDefault(a =>
                          a.Username.Equals(upn, StringComparison.OrdinalIgnoreCase))
                      ?? accounts.FirstOrDefault();

                if (hint != null)
                {
                    var r = await app.AcquireTokenSilent(PaScopes, hint).ExecuteAsync();
                    _paToken = r.AccessToken;
                    return _paToken;
                }
            }
            catch (MsalUiRequiredException) { }
            catch { }

            // 3. Windows Integrated Authentication — completely silent, no browser, no prompt.
            //    Uses the current Windows / Kerberos session (same credentials XrmToolBox used).
            //    Works on: domain-joined machines, AAD-joined machines, federated identity orgs.
            //    Fails silently for: cloud-only AAD accounts or when per-request MFA is enforced.
            try
            {
                var iwa = app.AcquireTokenByIntegratedWindowsAuth(PaScopes);
                if (!string.IsNullOrEmpty(upn)) iwa = iwa.WithUsername(upn);
                var r = await iwa.ExecuteAsync();
                _paToken = r.AccessToken;
                return _paToken;
            }
            catch (MsalUiRequiredException) { }   // IWA not available — fall through
            catch (MsalServiceException)    { }   // e.g. MFA required — fall through
            catch { }

            // 4. Interactive browser — last resort.
            //    Only reached on pure cloud-AAD accounts or when Conditional Access
            //    requires explicit MFA. After this one sign-in the token cache means
            //    steps 2 or 3 will handle every future run silently.
            var ask = MessageBox.Show(
                "A one-time sign-in to Power Automate is required.\n\n" +
                "A browser window will open. Sign in with the same account used for " +
                "this XrmToolBox connection. After that, this tool signs in " +
                "silently on every future use — this prompt will not appear again.\n\n" +
                "Would you like to sign in now?",
                "Power Automate Sign-in Required",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);

            if (ask != DialogResult.Yes) return null;

            try
            {
                var builder = app.AcquireTokenInteractive(PaScopes)
                    .WithUseEmbeddedWebView(false);
                if (!string.IsNullOrEmpty(upn))
                    builder = builder.WithLoginHint(upn);

                var r = await builder.ExecuteAsync();
                _paToken = r.AccessToken;
                return _paToken;
            }
            catch (Exception ex)
            {
                throw new Exception($"Power Automate sign-in failed: {ex.Message}", ex);
            }
        }

        // ── Environment ID auto-detection ────────────────────────────────────
        // Calls the PA environments list, finds the one whose instanceUrl matches
        // the currently connected Dataverse org, and returns its environment name GUID.
        private async Task<string> AutoDetectEnvironmentIdAsync(string paToken)
        {
            // Derive the org base URL from the connection
            var orgUrl = ConnectionDetail?.WebApplicationUrl?.TrimEnd('/').ToLowerInvariant();
            if (string.IsNullOrEmpty(orgUrl))
            {
                var soap = ConnectionDetail?.OrganizationServiceUrl;
                if (soap != null)
                {
                    var idx = soap.IndexOf("/XRMServices", StringComparison.OrdinalIgnoreCase);
                    if (idx > 0) orgUrl = soap.Substring(0, idx).TrimEnd('/').ToLowerInvariant();
                }
            }
            if (string.IsNullOrEmpty(orgUrl)) return null;

            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", paToken);
                http.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));

                var resp = await http.GetAsync(
                    "https://api.flow.microsoft.com/providers/Microsoft.ProcessSimple" +
                    "/environments?api-version=2016-11-01");
                if (!resp.IsSuccessStatusCode) return null;

                var json = await resp.Content.ReadAsStringAsync();
                var ser  = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var obj  = ser.Deserialize<Dictionary<string, object>>(json);

                if (obj?.TryGetValue("value", out var valObj) == true &&
                    valObj is System.Collections.ArrayList envList)
                {
                    foreach (Dictionary<string, object> env in envList)
                    {
                        if (!env.TryGetValue("name", out var nameObj)) continue;

                        if (env.TryGetValue("properties", out var propsObj) &&
                            propsObj is Dictionary<string, object> props &&
                            props.TryGetValue("linkedEnvironmentMetadata", out var metaObj) &&
                            metaObj is Dictionary<string, object> meta &&
                            meta.TryGetValue("instanceUrl", out var urlObj))
                        {
                            var instanceUrl = urlObj?.ToString()?.TrimEnd('/').ToLowerInvariant();
                            if (instanceUrl == orgUrl)
                                return nameObj.ToString();
                        }
                    }
                }
            }
            return null;
        }


        // ── Power Automate REST API call ─────────────────────────────────────
        // For Dataverse-triggered flows the trigger body is NOT inline in the runs
        // list — it is stored externally and referenced by outputsLink.uri (a SAS URL).
        // We follow that link (no auth header — the SAS signature is in the URL itself).
        private Dictionary<string, string> FetchTriggerOutputsFromPowerAutomateApi(
            string envId, Guid flowId, string paToken, out string diagnostic)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(paToken))
            {
                diagnostic = "ⓘ Add Columns: sign in to Power Automate (click Find Runs) to enable trigger output columns.";
                return result;
            }
            if (string.IsNullOrEmpty(envId))
            {
                diagnostic = "ⓘ Add Columns: paste the environment ID into the field above.";
                return result;
            }

            try
            {
                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

                // http  — calls the PA management API (needs Bearer token)
                // httpSas — fetches SAS blob URLs (must NOT send Authorization header)
                using (var http    = new HttpClient())
                using (var httpSas = new HttpClient())
                {
                    http.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", paToken);
                    http.DefaultRequestHeaders.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("application/json"));
                    httpSas.DefaultRequestHeaders.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("application/json"));

                    // ── Step 1: Get the runs list ────────────────────────────────
                    var url = "https://api.flow.microsoft.com/providers/Microsoft.ProcessSimple" +
                              $"/environments/{envId}/flows/{flowId:D}/runs" +
                              "?api-version=2016-11-01&$top=50";

                    var resp = http.GetAsync(url).GetAwaiter().GetResult();

                    if ((int)resp.StatusCode == 401)
                    {
                        // Access token expired — ask MSAL for a fresh one silently.
                        // We're on the background thread so GetAwaiter().GetResult() is safe.
                        _paToken = null;
                        try
                        {
                            var msalApp  = GetOrCreateMsalApp();
                            var accounts = msalApp.GetAccountsAsync().GetAwaiter().GetResult();
                            var acct     = accounts.FirstOrDefault();
                            if (acct != null)
                            {
                                var r = msalApp.AcquireTokenSilent(PaScopes, acct)
                                               .ExecuteAsync().GetAwaiter().GetResult();
                                _paToken = r.AccessToken;
                                http.DefaultRequestHeaders.Authorization =
                                    new AuthenticationHeaderValue("Bearer", _paToken);
                                resp = http.GetAsync(url).GetAwaiter().GetResult();
                            }
                        }
                        catch { /* silent refresh failed — fall through to error */ }

                        if ((int)resp.StatusCode == 401)
                        {
                            diagnostic = "ⓘ Add Columns: PA token expired — click Find Runs to sign in again.";
                            return result;
                        }
                    }

                    if ((int)resp.StatusCode == 403)
                    {
                        _paToken = null;
                        var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        var snippet = body?.Length > 200 ? body.Substring(0, 200) + "…" : body;
                        diagnostic = $"ⓘ Add Columns: PA API 403 — click Find Runs to sign in again. Detail: {snippet}";
                        return result;
                    }
                    if (!resp.IsSuccessStatusCode)
                    {
                        var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        var snippet = body?.Length > 200 ? body.Substring(0, 200) + "…" : body;
                        diagnostic = $"ⓘ Add Columns: Power Automate API {(int)resp.StatusCode} — {resp.ReasonPhrase}. Detail: {snippet}";
                        return result;
                    }

                    var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    var obj  = ser.Deserialize<Dictionary<string, object>>(json);

                    int withData    = 0;
                    int linkFetches = 0;
                    const int MaxLinkFetches = 25; // cap secondary requests

                    if (obj?.TryGetValue("value", out var valObj) == true &&
                        valObj is System.Collections.ArrayList runList)
                    {
                        foreach (Dictionary<string, object> run in runList)
                        {
                            if (!run.TryGetValue("name", out var nameObj)) continue;
                            var runName = nameObj?.ToString();
                            if (string.IsNullOrEmpty(runName)) continue;

                            if (!run.TryGetValue("properties", out var propsObj) ||
                                !(propsObj is Dictionary<string, object> props)) continue;
                            if (!props.TryGetValue("trigger", out var trigObj) ||
                                !(trigObj is Dictionary<string, object> trig)) continue;

                            // ── (a) Inline outputs (uncommon for Dataverse triggers) ──
                            if (trig.TryGetValue("outputs", out var outObj) &&
                                outObj is Dictionary<string, object> outputs &&
                                outputs.Count > 0)
                            {
                                result[runName] = ser.Serialize(outputs);
                                withData++;
                                continue;
                            }

                            // ── (b) outputsLink — SAS URL pointing at the payload ────
                            // Azure Storage returns 400 if an Authorization header is sent
                            // alongside a SAS signature, so we use httpSas (no auth).
                            if (linkFetches < MaxLinkFetches &&
                                trig.TryGetValue("outputsLink", out var outLinkObj) &&
                                outLinkObj is Dictionary<string, object> outLink &&
                                outLink.TryGetValue("uri", out var uriObj) &&
                                uriObj != null)
                            {
                                var sasUri = uriObj.ToString();
                                if (!string.IsNullOrEmpty(sasUri))
                                {
                                    try
                                    {
                                        var linkResp = httpSas.GetAsync(sasUri).GetAwaiter().GetResult();
                                        if (linkResp.IsSuccessStatusCode)
                                        {
                                            var linkJson = linkResp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                            if (!string.IsNullOrWhiteSpace(linkJson))
                                            {
                                                result[runName] = linkJson;
                                                withData++;
                                            }
                                        }
                                    }
                                    catch { /* non-fatal — skip this run */ }
                                    linkFetches++;
                                }
                            }
                        }
                    }

                    diagnostic = withData > 0
                        ? $"Ready — trigger outputs loaded for {withData} run{(withData == 1 ? "" : "s")}."
                        : "ⓘ Add Columns: Power Automate API succeeded but returned no trigger outputs for these runs.";
                }
            }
            catch (Exception ex)
            {
                diagnostic = $"ⓘ Add Columns: Power Automate API error — {ex.Message}";
            }

            return result;
        }

        // ── Render results ───────────────────────────────────────────────────
        private void RenderResults(DataCollection<Entity> runs, FlowItem flow,
            Dictionary<string, string> webApiInputs)
        {
            dgvResults.Rows.Clear();

            // Show/hide flow name column based on mode
            dgvResults.Columns["colFlow"].Visible = _allFlowsMode;

            if (runs.Count == 0)
            {
                lblResultCount.Text   = "No runs found in the selected range.";
                btnAddColumns.Enabled = false;
                btnSelectAll.Enabled  = false;
                return;
            }

            var cap     = _allFlowsMode ? 500 : 50;
            var capped  = runs.Count == cap;
            lblResultCount.Text = $"{runs.Count} run{(runs.Count == 1 ? "" : "s")} found" +
                                  (capped ? $" (first {cap} shown — narrow the date range to see more)" : "");
            if (capped) lblResultCount.ForeColor = Color.FromArgb(164, 90, 0);

            if (!_allFlowsMode)
            {
                ParseAllTriggerData(runs, webApiInputs);
                _selectedExtraColumns = _selectedExtraColumns
                    .Where(c => _availableExtraColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }

            btnAddColumns.Enabled = !_allFlowsMode && runs.Count > 0;
            btnSelectAll.Enabled  = true;
            PopulateGrid(runs, flow);
        }

        // ── Trigger data: merge SDK attrs + PA API trigger outputs ───────────
        private void ParseAllTriggerData(DataCollection<Entity> runs,
            Dictionary<string, string> webApiInputs)
        {
            _runTriggerData.Clear();
            var allKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var fixedAttrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { RunId, RunName, RunStarted, RunCompleted, RunStatus, RunFlowRef };

            for (int i = 0; i < runs.Count; i++)
            {
                var rowData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var runName = runs[i].GetAttributeValue<string>(RunName) ?? runs[i].Id.ToString();

                // (a) SDK flowrun entity attributes
                foreach (var attr in runs[i].Attributes)
                {
                    if (fixedAttrs.Contains(attr.Key)) continue;
                    if (attr.Value is string s && s.TrimStart().StartsWith("{"))
                    {
                        var exp = ExpandTriggerJson(s);
                        if (exp.Count > 0) { foreach (var kv in exp) rowData[kv.Key] = kv.Value; continue; }
                    }
                    rowData[attr.Key] = AttributeToString(attr.Value);
                }

                // (b) PA API trigger outputs (the record that fired the flow)
                if (webApiInputs.TryGetValue(runName, out var trigJson))
                {
                    var exp = ExpandTriggerJson(trigJson);
                    foreach (var kv in exp) rowData[kv.Key] = kv.Value;
                }

                if (rowData.Count > 0)
                {
                    _runTriggerData[i] = rowData;
                    foreach (var k in rowData.Keys) allKeys.Add(k);
                }
            }

            _availableExtraColumns = allKeys.OrderBy(k => k).ToList();
        }

        private static Dictionary<string, string> ExpandTriggerJson(string json)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json)) return result;
            try
            {
                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var obj = ser.Deserialize<Dictionary<string, object>>(json);
                if (obj == null) return result;

                Dictionary<string, object> source = obj;
                if (obj.TryGetValue("body", out var bodyVal) && bodyVal is Dictionary<string, object> bodyDict)
                {
                    if (bodyDict.TryGetValue("BusinessEntity", out var beVal) &&
                        beVal is Dictionary<string, object> beDict)
                    {
                        source = beDict;
                        if (bodyDict.TryGetValue("FormattedValues", out var fvVal) &&
                            fvVal is Dictionary<string, object> fvDict)
                            FlattenTo(fvDict, result);
                    }
                    else { source = bodyDict; }
                }
                FlattenTo(source, result);
            }
            catch { }
            return result;
        }

        private static void FlattenTo(Dictionary<string, object> src, Dictionary<string, string> dst)
        {
            foreach (var kv in src)
            {
                if (kv.Key.StartsWith("@")) continue;
                if (kv.Value == null)                               dst[kv.Key] = string.Empty;
                else if (kv.Value is Dictionary<string, object>)   dst[kv.Key] = "[object]";
                else if (kv.Value is System.Collections.ArrayList a) dst[kv.Key] = $"[{a.Count} items]";
                else                                                dst[kv.Key] = Convert.ToString(kv.Value);
            }
        }

        private static string AttributeToString(object value)
        {
            if (value == null)               return string.Empty;
            if (value is EntityReference er) return er.Name ?? er.Id.ToString();
            if (value is OptionSetValue osv) return osv.Value.ToString();
            if (value is Money money)        return money.Value.ToString("F2");
            if (value is DateTime dt)        return dt.ToLocalTime().ToString("g");
            if (value is bool b)             return b ? "Yes" : "No";
            return Convert.ToString(value);
        }

        // ── Grid population ──────────────────────────────────────────────────
        // Fixed column indices (colCheck=0, colFlow=1, colStatus=2, colStarted=3, colDuration=4, colLink=5)
        private const int ColCheckIdx  = 0;
        private const int ColFlowIdx   = 1;
        private const int ColStatusIdx = 2;
        private const int ExtraColStart = 6;  // extra columns begin after colLink

        private void PopulateGrid(DataCollection<Entity> runs, FlowItem flow)
        {
            dgvResults.Rows.Clear();
            while (dgvResults.Columns.Count > ExtraColStart) dgvResults.Columns.RemoveAt(ExtraColStart);

            if (!_allFlowsMode)
                foreach (var col in _selectedExtraColumns)
                    dgvResults.Columns.Add(new DataGridViewTextBoxColumn
                        { Name = "colExtra_" + col, HeaderText = col, FillWeight = 20 });

            for (int i = 0; i < runs.Count; i++)
            {
                var run       = runs[i];
                var started   = run.GetAttributeValue<DateTime?>(RunStarted);
                var completed = run.GetAttributeValue<DateTime?>(RunCompleted);
                var status    = run.GetAttributeValue<string>(RunStatus) ?? "Unknown";
                var runName   = run.GetAttributeValue<string>(RunName) ?? run.Id.ToString();

                var duration = (started.HasValue && completed.HasValue)
                    ? FormatDuration(completed.Value - started.Value)
                    : (status.Equals("Running", StringComparison.OrdinalIgnoreCase) ? "Running…" : "—");

                // workflowid on flowrun can be EntityReference, Guid, or string depending
                // on the SDK version / ColumnSet — handle all three.
                var flowRefId = Guid.Empty;
                if (run.Attributes.TryGetValue(RunFlowRef, out var flowRefRaw))
                {
                    if      (flowRefRaw is EntityReference er2)                    flowRefId = er2.Id;
                    else if (flowRefRaw is Guid g2)                                flowRefId = g2;
                    else if (flowRefRaw is string s2 && Guid.TryParse(s2, out var p)) flowRefId = p;
                }

                var flowName = flow?.Name
                    ?? _flows.FirstOrDefault(f => f.Id == flowRefId)?.Name
                    ?? (flowRefId != Guid.Empty ? flowRefId.ToString() : "Unknown");

                var urlFlowId = flow != null ? flow.Id : flowRefId;
                var url       = urlFlowId != Guid.Empty ? BuildRunUrl(urlFlowId, runName) : string.Empty;

                // colCheck | colFlow | colStatus | colStarted | colDuration | colLink
                var rowIdx = dgvResults.Rows.Add(
                    false,      // colCheck — unchecked
                    flowName,   // colFlow
                    status,
                    started.HasValue ? started.Value.ToLocalTime().ToString("g") : "—",
                    duration,
                    "Go to flow");

                var row = dgvResults.Rows[rowIdx];
                // Tag stores entity ID (for deletion) + URL (for link click) + PA API delete fields
                row.Tag = new RunRowData { EntityId = run.Id, Url = url, FlowId = urlFlowId, RunName = runName };

                var sc = row.Cells[ColStatusIdx];
                switch (status.ToLower())
                {
                    case "succeeded": sc.Style.ForeColor = Color.FromArgb(16, 124, 16);  break;
                    case "failed":    sc.Style.ForeColor = Color.FromArgb(164, 38, 44);  break;
                    case "running":   sc.Style.ForeColor = Color.FromArgb(0, 120, 212);  break;
                    default:          sc.Style.ForeColor = Color.Gray; break;
                }
                sc.Style.Font = new Font(dgvResults.Font, FontStyle.Bold);

                if (!_allFlowsMode && _runTriggerData.TryGetValue(i, out var trigData))
                    for (int c = 0; c < _selectedExtraColumns.Count; c++)
                        row.Cells[ExtraColStart + c].Value =
                            trigData.TryGetValue(_selectedExtraColumns[c], out var val) ? val : string.Empty;
            }
        }

        // ── Grid events ──────────────────────────────────────────────────────
        // Checkbox toggle — grid is ReadOnly so we set the value programmatically
        private void dgvResults_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != ColCheckIdx) return;
            var cell  = dgvResults.Rows[e.RowIndex].Cells[ColCheckIdx];
            cell.Value = !(cell.Value as bool? ?? false);
            dgvResults.InvalidateCell(cell);
            dgvResults.Rows[e.RowIndex].Selected = false;   // checkbox is the selection — no row highlight
            UpdateDeleteButton();
        }

        private void dgvResults_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != dgvResults.Columns["colLink"].Index) return;
            var tag = dgvResults.Rows[e.RowIndex].Tag as RunRowData;
            if (tag == null) return;

            // Build the URL at click time so we always use the latest environment ID
            // (it may have been auto-detected after the grid was first populated)
            var url = tag.FlowId != Guid.Empty && !string.IsNullOrEmpty(tag.RunName)
                ? BuildRunUrl(tag.FlowId, tag.RunName)
                : tag.Url;

            if (!string.IsNullOrEmpty(url)) { lblStatus.Text = $"Opening: {url}"; OpenUrl(url); }
            dgvResults.Rows[e.RowIndex].Selected = false;   // don't highlight row when clicking a link
        }

        private void dgvResults_CellMouseEnter(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex != dgvResults.Columns["colLink"].Index || e.RowIndex < 0) return;
            dgvResults.Cursor = Cursors.Hand;

            // Show the target URL in the status bar on hover for easy verification
            var tag = dgvResults.Rows[e.RowIndex].Tag as RunRowData;
            if (tag != null && tag.FlowId != Guid.Empty)
                lblStatus.Text = BuildRunUrl(tag.FlowId, tag.RunName);
        }

        private void dgvResults_CellMouseLeave(object sender, DataGridViewCellEventArgs e)
            => dgvResults.Cursor = Cursors.Default;

        // ── Toolbar ──────────────────────────────────────────────────────────
        private void tsbReloadFlows_Click(object sender, EventArgs e) => LoadFlows();
        private void tsbClose_Click(object sender, EventArgs e)       => CloseTool();

        // ── Select All / Delete Selected ─────────────────────────────────────
        private void btnSelectAll_Click(object sender, EventArgs e)
        {
            bool anyUnchecked = dgvResults.Rows.Cast<DataGridViewRow>()
                .Any(r => !(r.Cells[ColCheckIdx].Value as bool? ?? false));
            bool newValue = anyUnchecked; // if anything unchecked → check all; else → uncheck all

            foreach (DataGridViewRow row in dgvResults.Rows)
                row.Cells[ColCheckIdx].Value = newValue;
            dgvResults.Invalidate();

            btnSelectAll.Text = newValue ? "Select None" : "Select All";
            UpdateDeleteButton();
        }

        private async void btnDeleteSelected_Click(object sender, EventArgs e)
        {
            // Collect checked rows
            var toDelete = dgvResults.Rows.Cast<DataGridViewRow>()
                .Where(r => r.Cells[ColCheckIdx].Value as bool? ?? false)
                .Select(r => new
                {
                    Tag    = r.Tag as RunRowData,
                    Status = r.Cells[ColStatusIdx].Value?.ToString() ?? ""
                })
                .Where(x => x.Tag != null)
                .ToList();

            if (toDelete.Count == 0) return;

            // Warn about in-progress runs
            var running = toDelete.Count(x => x.Status.Equals("Running", StringComparison.OrdinalIgnoreCase));
            if (running > 0)
            {
                var warn = MessageBox.Show(
                    $"{running} of the selected run{(running == 1 ? " is" : "s are")} still Running.\n\n" +
                    "Deleting an active run record will not stop the flow — it will only remove the history record.\n\n" +
                    "Continue anyway?",
                    "Active Runs Selected",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (warn != DialogResult.Yes) return;
            }

            // Final confirmation
            var confirm = MessageBox.Show(
                $"Permanently delete {toDelete.Count} flow run record{(toDelete.Count == 1 ? "" : "s")}?\n\n" +
                "This cannot be undone.",
                "Confirm Delete",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;

            // ── Ensure PA token (needed to delete via PA REST API) ────────────
            // The flowrun table is owned by a service principal; Dataverse SDK delete
            // fails for human users even with the System Administrator role.
            // The PA REST API runs as that service principal and can always delete.
            if (string.IsNullOrEmpty(_paToken))
            {
                try   { _paToken = await EnsurePaTokenAsync(); }
                catch { _paToken = null; }
            }

            // Auto-detect environment ID if still blank
            if (!string.IsNullOrEmpty(_paToken) && string.IsNullOrEmpty(txtEnvironmentId.Text.Trim()))
            {
                lblStatus.Text = "Detecting environment ID…";
                try
                {
                    var detected = await AutoDetectEnvironmentIdAsync(_paToken);
                    if (!string.IsNullOrEmpty(detected))
                    {
                        _environmentId        = detected;
                        txtEnvironmentId.Text = detected;
                        SaveDetectedEnvId(detected);
                    }
                }
                catch { /* non-fatal */ }
            }

            // ── Capture UI-thread values before handing off to WorkAsync ──────
            string capturedPaToken = _paToken;
            string capturedEnvId   = txtEnvironmentId.Text.Trim();
            var    toDeletePairs   = toDelete.Select(x => x.Tag).ToList();  // List<RunRowData>

            SetWorkingState(true, $"Deleting {toDeletePairs.Count} run{(toDeletePairs.Count == 1 ? "" : "s")}…");

            WorkAsync(new WorkAsyncInfo
            {
                Work = (worker, args) =>
                {
                    var deletedIds = new List<Guid>();
                    int failed     = 0;

                    // Prefer PA REST API deletion — it runs as the service principal that
                    // owns the flowrun records, so it succeeds regardless of Dataverse row ownership.
                    // Fall back to Dataverse SDK delete if PA token / env ID is unavailable.
                    bool usePaApi = !string.IsNullOrEmpty(capturedPaToken) &&
                                    !string.IsNullOrEmpty(capturedEnvId);

                    using (var http = usePaApi ? new HttpClient() : null)
                    {
                        if (usePaApi)
                        {
                            http.DefaultRequestHeaders.Authorization =
                                new AuthenticationHeaderValue("Bearer", capturedPaToken);
                        }

                        for (int i = 0; i < toDeletePairs.Count; i++)
                        {
                            var rowData = toDeletePairs[i];
                            bool ok     = false;
                            try
                            {
                                if (usePaApi && rowData.FlowId != Guid.Empty && !string.IsNullOrEmpty(rowData.RunName))
                                {
                                    // DELETE /environments/{env}/flows/{flow}/runs/{run}
                                    var url = "https://api.flow.microsoft.com/providers/Microsoft.ProcessSimple" +
                                              $"/environments/{capturedEnvId}/flows/{rowData.FlowId:D}/runs/{rowData.RunName}" +
                                              "?api-version=2016-11-01";
                                    var resp = http.DeleteAsync(url).GetAwaiter().GetResult();
                                    ok = resp.IsSuccessStatusCode || (int)resp.StatusCode == 404;
                                }

                                if (!ok)
                                {
                                    // Fallback: Dataverse SDK (works when user owns the record
                                    // or has org-level Delete on flowrun)
                                    Service.Delete(RunEntity, rowData.EntityId);
                                    ok = true;
                                }
                            }
                            catch { ok = false; }

                            if (ok) deletedIds.Add(rowData.EntityId);
                            else    failed++;
                            worker.ReportProgress((i + 1) * 100 / toDeletePairs.Count);
                        }
                    }
                    args.Result = new object[] { deletedIds, failed };
                },
                ProgressChanged = pe =>
                    lblStatus.Text = $"Deleting… {pe.ProgressPercentage}%",
                PostWorkCallBack = args =>
                {
                    SetWorkingState(false);
                    if (args.Error != null) { ShowError("Delete failed", args.Error); return; }

                    var data       = (object[])args.Result;
                    var deletedIds = (List<Guid>)data[0];
                    int failed     = (int)data[1];

                    // Only remove rows that were ACTUALLY deleted — failed rows stay in the grid
                    var deletedSet = new HashSet<Guid>(deletedIds);
                    for (int i = dgvResults.Rows.Count - 1; i >= 0; i--)
                    {
                        var tag = dgvResults.Rows[i].Tag as RunRowData;
                        if (tag != null && deletedSet.Contains(tag.EntityId))
                            dgvResults.Rows.RemoveAt(i);
                    }

                    int remaining = dgvResults.Rows.Count;
                    lblResultCount.Text      = $"{remaining} run{(remaining == 1 ? "" : "s")} remaining";
                    lblResultCount.ForeColor = SystemColors.ControlText;
                    btnSelectAll.Text        = "Select All";
                    UpdateDeleteButton();

                    if (failed > 0)
                        MessageBox.Show(
                            $"Deleted {deletedIds.Count}, failed to delete {failed}.\n\n" +
                            "The failed records were kept in the list.\n\n" +
                            "The tool tried the Power Automate REST API first, then fell back to the " +
                            "Dataverse SDK. If the PA token has expired, click Find Runs on a specific " +
                            "flow to refresh it, then try deleting again.",
                            "Partial Delete", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    else
                        lblStatus.Text = $"Deleted {deletedIds.Count} run{(deletedIds.Count == 1 ? "" : "s")} successfully.";
                }
            });
        }

        private void UpdateDeleteButton()
        {
            int count = dgvResults.Rows.Cast<DataGridViewRow>()
                .Count(r => r.Cells[ColCheckIdx].Value as bool? ?? false);
            btnDeleteSelected.Enabled = count > 0;
            btnDeleteSelected.Text    = count > 0 ? $"Delete ({count})" : "Delete Selected";
        }

        private async void btnAddColumns_Click(object sender, EventArgs e)
        {
            // ── Acquire PA token if we don't have one yet ─────────────────────
            // This is the only place a sign-in dialog can appear.  Find Runs never
            // shows a prompt; it just skips trigger columns if silent auth fails.
            if (string.IsNullOrEmpty(_paToken))
            {
                try   { _paToken = await EnsurePaTokenAsync(); }
                catch { _paToken = null; }
                if (string.IsNullOrEmpty(_paToken)) return;   // user declined

                // Auto-detect environment ID now that we have a token
                if (string.IsNullOrEmpty(txtEnvironmentId.Text.Trim()))
                {
                    try
                    {
                        var detected = await AutoDetectEnvironmentIdAsync(_paToken);
                        if (!string.IsNullOrEmpty(detected))
                        {
                            _environmentId        = detected;
                            txtEnvironmentId.Text = detected;
                        }
                    }
                    catch { }
                }

                // Re-fetch trigger data — it was skipped during Find Runs
                if (_lastRuns != null && _lastFlow != null)
                {
                    var envId  = txtEnvironmentId.Text.Trim();
                    var token  = _paToken;
                    var flow   = _lastFlow;
                    var runs   = _lastRuns;
                    SetWorkingState(true, "Loading trigger output columns…");
                    WorkAsync(new WorkAsyncInfo
                    {
                        Work = (worker, args) =>
                            args.Result = FetchTriggerOutputsFromPowerAutomateApi(
                                envId, flow.Id, token, out _),
                        PostWorkCallBack = args =>
                        {
                            SetWorkingState(false);
                            if (args.Error != null || args.Result == null) return;
                            _lastWebApiInputs = (Dictionary<string, string>)args.Result;
                            ParseAllTriggerData(runs, _lastWebApiInputs);
                            ShowColumnPicker();
                        }
                    });
                    return;   // picker shown from PostWorkCallBack above
                }
            }

            ShowColumnPicker();
        }

        private void ShowColumnPicker()
        {
            using (var dlg = new ColumnPickerDialog(_availableExtraColumns, _selectedExtraColumns))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _selectedExtraColumns = dlg.SelectedColumns;
                if (_lastRuns != null && _lastFlow != null)
                    PopulateGrid(_lastRuns, _lastFlow);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────
        private string BuildRunUrl(Guid workflowId, string runName)
        {
            var envId = txtEnvironmentId.Text.Trim();
            if (string.IsNullOrEmpty(envId)) return "https://make.powerautomate.com";
            // Deep-link to the specific run. Requires a valid environment GUID
            // (auto-detected via PA environments API and cached to disk).
            if (!string.IsNullOrEmpty(runName))
                return $"https://make.powerautomate.com/environments/{envId}/flows/{workflowId:D}/runs/{runName}";
            return $"https://make.powerautomate.com/environments/{envId}/flows/{workflowId:D}/runs";
        }

        private static string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalSeconds < 1) return "<1s";
            if (ts.TotalMinutes < 1) return $"{(int)ts.TotalSeconds}s";
            if (ts.TotalHours   < 1) return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        }

        private void SetWorkingState(bool working, string message = "")
        {
            tsbReloadFlows.Enabled    = !working;
            btnFindRuns.Enabled       = !working && _flowsLoaded;
            btnDeleteSelected.Enabled = !working && (btnDeleteSelected.Text != "Delete Selected");
            lblStatus.Text            = working ? message : "Ready";
        }

        private void ClearResults()
        {
            dgvResults.Rows.Clear();
            while (dgvResults.Columns.Count > ExtraColStart) dgvResults.Columns.RemoveAt(ExtraColStart);
            lblResultCount.Text      = string.Empty;
            lblResultCount.ForeColor = SystemColors.ControlText;
            _runTriggerData.Clear();
            _availableExtraColumns.Clear();
            _lastWebApiInputs.Clear();
            _lastRuns             = null;
            _lastFlow             = null;
            btnAddColumns.Enabled    = false;
            btnSelectAll.Enabled     = false;
            btnSelectAll.Text        = "Select All";
            btnDeleteSelected.Enabled = false;
            btnDeleteSelected.Text   = "Delete Selected";
        }

        private void ShowError(string context, Exception ex)
        {
            lblStatus.Text = "Error — see details below";
            MessageBox.Show($"{context}:\n\n{ex.Message}", "Flow Run Finder Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }

        // ── OAuth / JWT helpers ───────────────────────────────────────────────
        // Extracts the Dataverse bearer token from the service client via reflection
        private string TryGetAccessToken()
        {
            if (Service == null) return null;
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
            foreach (var name in new[] { "CurrentAccessToken", "AccessToken" })
            {
                try
                {
                    var val = Service.GetType().GetProperty(name, flags)?.GetValue(Service) as string;
                    if (!string.IsNullOrEmpty(val)) return val;
                }
                catch { }
            }
            return null;
        }

        // Decodes a JWT payload claim (base64url → JSON → value)
        private static string GetClaimFromToken(string jwt, string claim)
        {
            if (string.IsNullOrEmpty(jwt)) return null;
            try
            {
                var parts = jwt.Split('.');
                if (parts.Length < 2) return null;
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                while (payload.Length % 4 != 0) payload += "=";
                var json   = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                var claims = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                return claims.TryGetValue(claim, out var v) ? v?.ToString() : null;
            }
            catch { return null; }
        }

        private string GetWebApiBaseUrl()
        {
            var web = ConnectionDetail?.WebApplicationUrl?.TrimEnd('/');
            if (!string.IsNullOrEmpty(web)) return $"{web}/api/data/v9.2";
            var soap = ConnectionDetail?.OrganizationServiceUrl;
            if (soap != null)
            {
                var idx = soap.IndexOf("/XRMServices", StringComparison.OrdinalIgnoreCase);
                if (idx > 0) return $"{soap.Substring(0, idx)}/api/data/v9.2";
            }
            return null;
        }
    }

    // ── Row tag: carries entity ID (for deletion) and URL (for link click) ──
    internal class RunRowData
    {
        public Guid   EntityId { get; set; }
        public string Url      { get; set; }
        public Guid   FlowId   { get; set; }   // needed for PA REST API delete
        public string RunName  { get; set; }   // needed for PA REST API delete
    }

    // ── Data model ───────────────────────────────────────────────────────────
    internal class FlowItem
    {
        public Guid   Id   { get; }
        public string Name { get; }
        public FlowItem(Guid id, string name) { Id = id; Name = name; }
        public override string ToString() => Name;
    }
}
