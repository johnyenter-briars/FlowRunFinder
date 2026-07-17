using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FlowRunFinderV2.Core.Auth;
using FlowRunFinderV2.Core.Client;
using FlowRunFinderV2.Core.Configuration;
using FlowRunFinderV2.Core.Logging;
using FlowRunFinderV2.Core.Model;
using FlowRunFinderV2.Core.Query;
using McTools.Xrm.Connection;
using Microsoft.Xrm.Sdk;
using XrmToolBox.Extensibility;
using CoreSettingsManager = FlowRunFinderV2.Core.Configuration.SettingsManager;

namespace FlowRunFinder
{
    public partial class FlowRunFinderControl : PluginControlBase
    {
        private const int FixedRunColumnCount = 4;

        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowRunFinder");
        private static readonly string LogsFolder = Path.Combine(AppDataFolder, "logs");
        private static readonly string ConnectionsFolder = Path.Combine(AppDataFolder, "connections");

        private readonly CoreSettingsManager _settingsManager;
        private readonly AppLogger _logger;
        private readonly List<CloudFlow> _flows = new List<CloudFlow>();
        private readonly List<FlowRun> _runs = new List<FlowRun>();
        private readonly SortedSet<string> _knownTriggerKeys = new SortedSet<string>(AttributeNameComparer.Instance);
        private readonly HashSet<string> _selectedTriggerColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly BindingList<RunGridRow> _gridRows = new BindingList<RunGridRow>();
        private readonly Dictionary<Guid, AdvancedSearchState> _advancedSearchStateByFlowId = new Dictionary<Guid, AdvancedSearchState>();

        private AppSettings _settings = new AppSettings();
        private DataverseClient _dataverseClient;
        private DataverseAuthService _dataverseAuthService;
        private PowerAutomateAuthService _powerAutomateAuthService;
        private CloudFlow _selectedFlow;
        private Uri _environmentUrl;
        private ConnectionProfile _currentConnection;
        private string _deviceVerificationUrl;
        private string _deviceUserCode;
        private int _busyDepth;
        private FlowRunQuerySession _activeQuerySession;
        private System.Windows.Forms.Timer _toastTimer;
        private bool _suppressTriggerColumnEvents;

        public FlowRunFinderControl()
        {
            Directory.CreateDirectory(AppDataFolder);
            Directory.CreateDirectory(LogsFolder);
            Directory.CreateDirectory(ConnectionsFolder);

            _settingsManager = new CoreSettingsManager(AppDataFolder);
            _logger = new AppLogger(LogsFolder);

            InitializeComponent();

            dgvRuns.DataSource = _gridRows;
            btnReloadFlows.Enabled = false;
            btnRefreshRuns.Enabled = false;
            btnTriggerColumns.Enabled = false;
            btnAdvancedSearch.Enabled = false;

            _toastTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _toastTimer.Tick += delegate
            {
                _toastTimer.Stop();
                toastPanel.Visible = false;
            };

            Load += FlowRunFinderControl_Load;
        }

        private async void FlowRunFinderControl_Load(object sender, EventArgs e)
        {
            try
            {
                _settings = await _settingsManager.LoadAsync().ConfigureAwait(true);
                NormalizeSettings();
                await _settingsManager.SaveAsync(_settings).ConfigureAwait(true);
                _logger.SetVerbosity(_settings.LogVerbosity);
            }
            catch (Exception ex)
            {
                SetStatus("Could not load cached settings: " + ex.Message);
                _logger.Error("Settings load failed.", ex);
            }
        }

        public override void UpdateConnection(
            IOrganizationService newService,
            ConnectionDetail detail,
            string actionName,
            object parameter)
        {
            base.UpdateConnection(newService, detail, actionName, parameter);

            ResetConnectionState();

            var rawUrl = detail != null ? detail.WebApplicationUrl : null;
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                lblConnection.Text = "No XrmToolBox connection selected.";
                SetStatus("Connect to an environment to begin.");
                return;
            }

            _environmentUrl = new Uri(rawUrl.TrimEnd('/'));
            _currentConnection = new ConnectionProfile
            {
                Id = CreateStableConnectionId(_environmentUrl),
                Name = _environmentUrl.Host,
                EnvironmentUrl = _environmentUrl.GetLeftPart(UriPartial.Authority),
                CreatedOnUtc = DateTimeOffset.UtcNow
            };

            _logger.SetConnection(_currentConnection);
            ConfigureAuthServices(_currentConnection);

            lblConnection.Text = _currentConnection.Name + " - " + _currentConnection.EnvironmentUrl;
            btnReloadFlows.Enabled = true;
            SetStatus("Connected. Click Reload Flows to load cloud flows.");
        }

        private void ResetConnectionState()
        {
            _activeQuerySession?.Cancel();

            if (_dataverseClient != null)
            {
                _dataverseClient.Dispose();
                _dataverseClient = null;
            }

            _dataverseAuthService = null;
            _powerAutomateAuthService = null;
            _selectedFlow = null;
            _environmentUrl = null;
            _currentConnection = null;
            _flows.Clear();
            _runs.Clear();
            _knownTriggerKeys.Clear();
            _selectedTriggerColumns.Clear();
            _gridRows.Clear();
            ResetRunColumns();
            ResetTriggerColumnOptions();
            btnFlowPicker.Text = "Select a flow";
            btnReloadFlows.Enabled = false;
            btnRefreshRuns.Enabled = false;
            btnTriggerColumns.Enabled = false;
            btnAdvancedSearch.Enabled = false;
            flowPickerPanel.Visible = false;
            triggerColumnsPanel.Visible = false;
            deviceCodePanel.Visible = false;
        }

        private async void btnReloadFlows_Click(object sender, EventArgs e)
        {
            await OpenCoreConnectionAndLoadFlowsAsync().ConfigureAwait(true);
        }

        private async Task OpenCoreConnectionAndLoadFlowsAsync()
        {
            if (_environmentUrl == null || _dataverseAuthService == null)
            {
                SetStatus("Connect to an environment to begin.");
                return;
            }

            await RunUiActionAsync(async cancellationToken =>
            {
                SetStatus("Authenticating to Dataverse...");
                var token = await _dataverseAuthService.GetTokenAsync(
                    _environmentUrl,
                    ShowDeviceCodePrompt,
                    cancellationToken).ConfigureAwait(true);
                ClearDeviceCodePrompt();

                if (_dataverseClient != null)
                {
                    _dataverseClient.Dispose();
                }

                _dataverseClient = new DataverseClient(_environmentUrl, token.AccessToken);
                _logger.Info("Dataverse authentication succeeded.");
                await LoadFlowsAsync(cancellationToken).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }

        private async Task LoadFlowsAsync(CancellationToken cancellationToken)
        {
            if (_dataverseClient == null)
            {
                return;
            }

            _flows.Clear();
            _runs.Clear();
            _gridRows.Clear();
            _knownTriggerKeys.Clear();
            _selectedTriggerColumns.Clear();
            ResetRunColumns();
            ResetTriggerColumnOptions();
            ClearSelectedFlow();

            SetStatus("Loading flows...");
            var flows = await _dataverseClient.GetCloudFlowsAsync(cancellationToken).ConfigureAwait(true);
            foreach (var flow in flows.OrderBy(flow => flow.Name, StringComparer.OrdinalIgnoreCase))
            {
                _flows.Add(flow);
            }

            RefreshFilteredFlows();
            SetStatus("Loaded " + _flows.Count + " cloud flows. Pick a flow to load the latest " + _settings.DefaultRunCount + " runs.");
        }

        private void btnFlowPicker_Click(object sender, EventArgs e)
        {
            if (_flows.Count == 0)
            {
                SetStatus("Reload flows before picking a flow.");
                return;
            }

            txtFlowSearch.Text = string.Empty;
            RefreshFilteredFlows();
            PositionPopupPanel(flowPickerPanel, btnFlowPicker);
            flowPickerPanel.Visible = !flowPickerPanel.Visible;
            if (flowPickerPanel.Visible)
            {
                flowPickerPanel.BringToFront();
                txtFlowSearch.Focus();
            }
        }

        private void txtFlowSearch_TextChanged(object sender, EventArgs e)
        {
            RefreshFilteredFlows();
        }

        private async void lstFlows_SelectedIndexChanged(object sender, EventArgs e)
        {
            var flow = lstFlows.SelectedItem as CloudFlow;
            if (flow == null)
            {
                return;
            }

            _selectedFlow = flow;
            btnFlowPicker.Text = flow.Name;
            flowPickerPanel.Visible = false;
            lstFlows.ClearSelected();
            await LoadSelectedFlowRunsAsync().ConfigureAwait(true);
        }

        private async void btnRefreshRuns_Click(object sender, EventArgs e)
        {
            await LoadSelectedFlowRunsAsync().ConfigureAwait(true);
        }

        private async Task LoadSelectedFlowRunsAsync()
        {
            if (_selectedFlow == null || _environmentUrl == null || _powerAutomateAuthService == null)
            {
                return;
            }

            await RunUiActionAsync(async cancellationToken =>
            {
                _runs.Clear();
                _gridRows.Clear();
                ResetRunColumns();
                ResetTriggerColumnOptions();
                SetStatus("Loading latest " + _settings.DefaultRunCount + " runs for " + _selectedFlow.Name + "...");

                var paToken = await _powerAutomateAuthService.GetTokenAsync(
                    ShowDeviceCodePrompt,
                    cancellationToken).ConfigureAwait(true);
                ClearDeviceCodePrompt();

                using (var queryEngine = new FlowRunQueryEngine(paToken.AccessToken, _dataverseClient, _logger))
                {
                    var environmentId = await queryEngine.DetectEnvironmentIdAsync(_environmentUrl, cancellationToken).ConfigureAwait(true);
                    if (string.IsNullOrWhiteSpace(environmentId))
                    {
                        SetStatus("Could not detect the matching Power Automate environment id.");
                        return;
                    }

                    var runs = await queryEngine.GetLatestRunsAsync(
                        new LatestFlowRunsRequest(
                            environmentId,
                            _selectedFlow.WorkflowId,
                            _settings.DefaultRunCount,
                            _settings.UseFlowRunHistoryTable),
                        cancellationToken).ConfigureAwait(true);

                    ApplyRuns(runs);
                    SetTriggerColumnOptions(_selectedFlow, runs.SelectMany(run => run.TriggerInputs.Keys));
                    SetStatus("Loaded " + _runs.Count + " runs for " + _selectedFlow.Name + ".");
                }
            }).ConfigureAwait(true);
        }

        private async void btnAdvancedSearch_Click(object sender, EventArgs e)
        {
            if (_selectedFlow == null || _knownTriggerKeys.Count == 0)
            {
                return;
            }

            AdvancedSearchState initialState;
            _advancedSearchStateByFlowId.TryGetValue(_selectedFlow.WorkflowId, out initialState);
            using (var dialog = new AdvancedSearchDialog(_knownTriggerKeys, initialState))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                CacheAdvancedSearchState(_selectedFlow.WorkflowId, dialog.Request);
                await RunAdvancedSearchAsync(_selectedFlow, dialog.Request).ConfigureAwait(true);
            }
        }

        private async Task RunAdvancedSearchAsync(CloudFlow flow, AdvancedSearchRequest request)
        {
            if (_environmentUrl == null || _powerAutomateAuthService == null)
            {
                return;
            }

            await RunUiActionAsync(async cancellationToken =>
            {
                _runs.Clear();
                _gridRows.Clear();
                ResetRunColumns();
                SetStatus("Searching runs for " + flow.Name + "...");
                ShowAdvancedSearchProgress();
                var progress = new Progress<FlowRunQueryProgress>(UpdateAdvancedSearchProgress);

                try
                {
                    var paToken = await _powerAutomateAuthService.GetTokenAsync(
                        ShowDeviceCodePrompt,
                        cancellationToken).ConfigureAwait(true);
                    ClearDeviceCodePrompt();

                    using (var queryEngine = new FlowRunQueryEngine(paToken.AccessToken, _dataverseClient, _logger))
                    {
                        var environmentId = await queryEngine.DetectEnvironmentIdAsync(_environmentUrl, cancellationToken).ConfigureAwait(true);
                        if (string.IsNullOrWhiteSpace(environmentId))
                        {
                            SetStatus("Could not detect the matching Power Automate environment id.");
                            return;
                        }

                        var runs = await queryEngine.SearchRunsAsync(
                            new FlowRunSearchRequest(
                                environmentId,
                                flow.WorkflowId,
                                request.StartUtc,
                                request.EndUtc,
                                request.Filter,
                                _settings.MaxRunsToQuery,
                                _settings.UseFlowRunHistoryTable,
                                progress),
                            cancellationToken).ConfigureAwait(true);

                        ApplyRuns(runs);
                        SetTriggerColumnOptions(flow, runs.SelectMany(run => run.TriggerInputs.Keys));
                        SetStatus("Found " + _runs.Count + " runs for " + flow.Name + ".");
                    }
                }
                finally
                {
                    HideAdvancedSearchProgress();
                }
            }, canCancel: true).ConfigureAwait(true);
        }

        private void btnTriggerColumns_Click(object sender, EventArgs e)
        {
            if (_knownTriggerKeys.Count == 0)
            {
                return;
            }

            txtTriggerColumnSearch.Text = string.Empty;
            RefreshFilteredTriggerColumnOptions();
            PositionPopupPanel(triggerColumnsPanel, btnTriggerColumns);
            triggerColumnsPanel.Visible = !triggerColumnsPanel.Visible;
            if (triggerColumnsPanel.Visible)
            {
                triggerColumnsPanel.BringToFront();
                txtTriggerColumnSearch.Focus();
            }
        }

        private void txtTriggerColumnSearch_TextChanged(object sender, EventArgs e)
        {
            RefreshFilteredTriggerColumnOptions();
        }

        private async void clbTriggerColumns_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_suppressTriggerColumnEvents)
            {
                return;
            }

            var key = clbTriggerColumns.Items[e.Index] as string;
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (e.NewValue == CheckState.Checked)
            {
                _selectedTriggerColumns.Add(key);
            }
            else
            {
                _selectedTriggerColumns.Remove(key);
            }

            ApplySelectedTriggerColumns();
            await SaveSelectedTriggerColumnsAsync().ConfigureAwait(true);
        }

        private async void btnSettings_Click(object sender, EventArgs e)
        {
            using (var dialog = new SettingsDialog(_settings))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                await _settingsManager.UpdateAsync(settings =>
                {
                    settings.DefaultRunCount = dialog.DefaultRunCount;
                    settings.MaxRunsToQuery = dialog.MaxRunsToQuery;
                    settings.UseFlowRunHistoryTable = dialog.UseFlowRunHistoryTable;
                    settings.AuthenticationFlow = dialog.AuthenticationFlow;
                    settings.DataverseClientId = dialog.DataverseClientId;
                    settings.PowerAutomateClientId = dialog.PowerAutomateClientId;
                    settings.LogVerbosity = dialog.LogVerbosity;
                }).ConfigureAwait(true);

                _settings = _settingsManager.Current;
                NormalizeSettings();
                if (_currentConnection != null)
                {
                    ConfigureAuthServices(_currentConnection);
                }

                _logger.SetVerbosity(_settings.LogVerbosity);
                SetStatus("Settings saved. Default run count is " + _settings.DefaultRunCount + "; max runs to query is " + _settings.MaxRunsToQuery + ".");
            }
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            CloseTool();
        }

        private void btnCopyDeviceUrl_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_deviceVerificationUrl))
            {
                Clipboard.SetText(_deviceVerificationUrl);
                ShowToast("Copied to clipboard");
                SetStatus("Device login URL copied.");
            }
        }

        private void btnCopyDeviceCode_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_deviceUserCode))
            {
                Clipboard.SetText(_deviceUserCode);
                ShowToast("Copied to clipboard");
                SetStatus("Device code copied.");
            }
        }

        private void dgvRuns_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || dgvRuns.Columns[e.ColumnIndex].Name != "colRunId")
            {
                return;
            }

            var row = dgvRuns.Rows[e.RowIndex].DataBoundItem as RunGridRow;
            if (row == null || string.IsNullOrWhiteSpace(row.RunUrl))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = row.RunUrl,
                UseShellExecute = true
            });
        }

        private void dgvRuns_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Button != MouseButtons.Right)
            {
                return;
            }

            var row = dgvRuns.Rows[e.RowIndex].DataBoundItem as RunGridRow;
            if (row == null)
            {
                return;
            }

            string textToCopy;
            if (dgvRuns.Columns[e.ColumnIndex].Name == "colRunId")
            {
                textToCopy = row.RunUrl;
                if (string.IsNullOrWhiteSpace(textToCopy))
                {
                    textToCopy = row.Name;
                }
            }
            else
            {
                var value = dgvRuns.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
                textToCopy = value == null ? string.Empty : value.ToString();
            }

            if (string.IsNullOrWhiteSpace(textToCopy))
            {
                return;
            }

            Clipboard.SetText(textToCopy);
            ShowToast("Copied to clipboard");
            SetStatus(dgvRuns.Columns[e.ColumnIndex].Name == "colRunId" ? "Run URL copied." : "Cell value copied.");
        }

        private void ApplyRuns(IEnumerable<FlowRun> runs)
        {
            _runs.Clear();
            _gridRows.Clear();
            foreach (var run in runs)
            {
                _runs.Add(run);
                _gridRows.Add(new RunGridRow(run));
            }
        }

        private void SetTriggerColumnOptions(CloudFlow flow, IEnumerable<string> triggerKeys)
        {
            foreach (var key in triggerKeys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    _knownTriggerKeys.Add(key);
                }
            }

            RestoreTriggerColumnOptions(flow);
        }

        private void RestoreTriggerColumnOptions(CloudFlow flow)
        {
            _selectedTriggerColumns.Clear();
            List<string> cached;
            if (_settings.SelectedTriggerColumnsByFlowId.TryGetValue(flow.WorkflowId.ToString("D"), out cached))
            {
                foreach (var key in cached)
                {
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        _selectedTriggerColumns.Add(key);
                    }
                }
            }

            RefreshFilteredTriggerColumnOptions();
            btnTriggerColumns.Enabled = _knownTriggerKeys.Count > 0;
            btnAdvancedSearch.Enabled = _knownTriggerKeys.Count > 0;
            ApplySelectedTriggerColumns();
        }

        private void RefreshFilteredFlows()
        {
            var searchText = txtFlowSearch.Text.Trim();
            var filtered = string.IsNullOrWhiteSpace(searchText)
                ? _flows
                : _flows.Where(flow =>
                    ContainsIgnoreCase(flow.Name, searchText) ||
                    ContainsIgnoreCase(flow.WorkflowId.ToString("D"), searchText)).ToList();

            lstFlows.BeginUpdate();
            try
            {
                lstFlows.Items.Clear();
                foreach (var flow in filtered.OrderBy(flow => flow.Name, StringComparer.OrdinalIgnoreCase))
                {
                    lstFlows.Items.Add(flow);
                }
            }
            finally
            {
                lstFlows.EndUpdate();
            }
        }

        private void RefreshFilteredTriggerColumnOptions()
        {
            var selected = GetSelectedTriggerColumns();
            var searchText = txtTriggerColumnSearch.Text.Trim();
            var filtered = string.IsNullOrWhiteSpace(searchText)
                ? _knownTriggerKeys
                : _knownTriggerKeys.Where(key => ContainsIgnoreCase(key, searchText));

            _suppressTriggerColumnEvents = true;
            clbTriggerColumns.BeginUpdate();
            try
            {
                clbTriggerColumns.Items.Clear();
                foreach (var key in filtered.OrderBy(key => key, AttributeNameComparer.Instance))
                {
                    clbTriggerColumns.Items.Add(key, selected.Contains(key));
                }
            }
            finally
            {
                clbTriggerColumns.EndUpdate();
                _suppressTriggerColumnEvents = false;
            }
        }

        private void ApplySelectedTriggerColumns()
        {
            ResetRunColumns();

            foreach (var key in GetSelectedTriggerColumns().OrderBy(key => key, AttributeNameComparer.Instance))
            {
                var column = new DataGridViewTextBoxColumn
                {
                    Name = "trigger_" + key,
                    HeaderText = key,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells
                };
                dgvRuns.Columns.Add(column);
            }

            foreach (DataGridViewRow row in dgvRuns.Rows)
            {
                var gridRow = row.DataBoundItem as RunGridRow;
                if (gridRow == null)
                {
                    continue;
                }

                foreach (var key in GetSelectedTriggerColumns())
                {
                    var columnName = "trigger_" + key;
                    if (dgvRuns.Columns.Contains(columnName))
                    {
                        row.Cells[columnName].Value = gridRow.GetTriggerValue(key);
                    }
                }
            }
        }

        private async Task SaveSelectedTriggerColumnsAsync()
        {
            if (_selectedFlow == null)
            {
                return;
            }

            var selected = GetSelectedTriggerColumns().ToList();
            await _settingsManager.UpdateAsync(settings =>
            {
                var flowId = _selectedFlow.WorkflowId.ToString("D");
                if (selected.Count == 0)
                {
                    settings.SelectedTriggerColumnsByFlowId.Remove(flowId);
                }
                else
                {
                    settings.SelectedTriggerColumnsByFlowId[flowId] = selected;
                }
            }).ConfigureAwait(true);

            _settings = _settingsManager.Current;
        }

        private HashSet<string> GetSelectedTriggerColumns()
        {
            return new HashSet<string>(_selectedTriggerColumns, StringComparer.OrdinalIgnoreCase);
        }

        private void ResetRunColumns()
        {
            while (dgvRuns.Columns.Count > FixedRunColumnCount)
            {
                dgvRuns.Columns.RemoveAt(dgvRuns.Columns.Count - 1);
            }
        }

        private void ResetTriggerColumnOptions()
        {
            _suppressTriggerColumnEvents = true;
            clbTriggerColumns.Items.Clear();
            _selectedTriggerColumns.Clear();
            _suppressTriggerColumnEvents = false;
            triggerColumnsPanel.Visible = false;
            btnTriggerColumns.Enabled = false;
            btnAdvancedSearch.Enabled = false;
        }

        private void ClearSelectedFlow()
        {
            _selectedFlow = null;
            btnFlowPicker.Text = "Select a flow";
            btnRefreshRuns.Enabled = false;
        }

        private async Task RunUiActionAsync(Func<CancellationToken, Task> action, bool canCancel = false)
        {
            btnReloadFlows.Enabled = false;
            btnSettings.Enabled = false;
            btnRefreshRuns.Enabled = false;
            btnTriggerColumns.Enabled = false;
            btnAdvancedSearch.Enabled = false;
            BeginBusy();

            FlowRunQuerySession querySession = null;
            try
            {
                if (canCancel)
                {
                    querySession = new FlowRunQuerySession();
                    _activeQuerySession = querySession;
                    btnCancelBusyAction.Visible = true;
                    btnCancelBusyAction.Enabled = true;
                }

                await action(querySession != null ? querySession.CancellationToken : CancellationToken.None).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (querySession != null && querySession.IsCancellationRequested)
            {
                SetStatus("Query canceled.");
                _logger.Info("Query canceled by user.");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                _logger.Error("UI action failed.", ex);
            }
            finally
            {
                EndBusy();
                btnReloadFlows.Enabled = _environmentUrl != null;
                btnSettings.Enabled = true;
                btnRefreshRuns.Enabled = _selectedFlow != null;
                btnTriggerColumns.Enabled = _knownTriggerKeys.Count > 0;
                btnAdvancedSearch.Enabled = _knownTriggerKeys.Count > 0;
                btnCancelBusyAction.Visible = false;
                btnCancelBusyAction.Enabled = false;
                if (ReferenceEquals(_activeQuerySession, querySession))
                {
                    _activeQuerySession = null;
                }

                if (querySession != null)
                {
                    querySession.Dispose();
                }
            }
        }

        private void btnCancelBusyAction_Click(object sender, EventArgs e)
        {
            if (_activeQuerySession == null || _activeQuerySession.IsCancellationRequested)
            {
                return;
            }

            btnCancelBusyAction.Enabled = false;
            SetStatus("Canceling query...");
            _activeQuerySession.Cancel();
            _logger.Info("Cancel requested for active query.");
        }

        private void ShowDeviceCodePrompt(DeviceCodePrompt prompt)
        {
            SafeUi(() =>
            {
                _deviceVerificationUrl = prompt.VerificationUrl;
                _deviceUserCode = prompt.UserCode;
                lblDeviceMessage.Text = "Open the login URL in your browser and enter the device code.";
                lblDeviceUrl.Text = prompt.VerificationUrl;
                lblDeviceCode.Text = prompt.UserCode;
                deviceCodePanel.Visible = true;
            });
        }

        private void ClearDeviceCodePrompt()
        {
            SafeUi(() =>
            {
                _deviceVerificationUrl = null;
                _deviceUserCode = null;
                lblDeviceMessage.Text = string.Empty;
                lblDeviceUrl.Text = string.Empty;
                lblDeviceCode.Text = string.Empty;
                deviceCodePanel.Visible = false;
            });
        }

        private void CacheAdvancedSearchState(Guid workflowId, AdvancedSearchRequest request)
        {
            _advancedSearchStateByFlowId[workflowId] = new AdvancedSearchState
            {
                StartUtc = request.StartUtc,
                EndUtc = request.EndUtc,
                Filter = (request.Filter.Clone() as AdvancedSearchGroup) ?? new AdvancedSearchGroup()
            };
        }

        private void NormalizeSettings()
        {
            if (_settings.DefaultRunCount < 1) _settings.DefaultRunCount = 1;
            if (_settings.DefaultRunCount > 100) _settings.DefaultRunCount = 100;
            if (_settings.MaxRunsToQuery < 1) _settings.MaxRunsToQuery = 1;
            if (!Guid.TryParse(_settings.DataverseClientId, out _))
            {
                _settings.DataverseClientId = AuthenticationClientIds.PowerAutomate;
            }

            if (!Guid.TryParse(_settings.PowerAutomateClientId, out _))
            {
                _settings.PowerAutomateClientId = AuthenticationClientIds.PowerAutomate;
            }

            if (!Enum.IsDefined(typeof(LogVerbosity), _settings.LogVerbosity))
            {
                _settings.LogVerbosity = LogVerbosity.Info;
            }

            if (!Enum.IsDefined(typeof(AuthenticationFlow), _settings.AuthenticationFlow))
            {
                _settings.AuthenticationFlow = AuthenticationFlow.InteractiveBrowser;
            }

            if (_settings.SelectedTriggerColumnsByFlowId == null)
            {
                _settings.SelectedTriggerColumnsByFlowId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void ConfigureAuthServices(ConnectionProfile connection)
        {
            var tokenCacheOptions = new TokenCacheOptions(GetConnectionFolder(connection.Id));
            _dataverseAuthService = new DataverseAuthService(
                tokenCacheOptions,
                _settings.DataverseClientId,
                _settings.AuthenticationFlow);
            _powerAutomateAuthService = new PowerAutomateAuthService(
                tokenCacheOptions,
                _settings.PowerAutomateClientId,
                _settings.AuthenticationFlow);
        }

        private static string GetConnectionFolder(Guid connectionId)
        {
            return Path.Combine(ConnectionsFolder, connectionId.ToString("D"));
        }

        private void BeginBusy()
        {
            _busyDepth++;
            lblBusy.Text = string.IsNullOrWhiteSpace(lblStatus.Text) ? "Working..." : lblStatus.Text;
            busyPanel.Visible = true;
            busyPanel.BringToFront();
        }

        private void EndBusy()
        {
            _busyDepth = Math.Max(0, _busyDepth - 1);
            busyPanel.Visible = _busyDepth > 0;
        }

        private void ShowAdvancedSearchProgress()
        {
            lblAdvancedSearchProgress.Text = "Candidate records: 0. 0% scanned, 0 matches";
            progressAdvancedSearch.Value = 0;
            advancedSearchProgressPanel.Visible = true;
            advancedSearchProgressPanel.BringToFront();
        }

        private void UpdateAdvancedSearchProgress(FlowRunQueryProgress progress)
        {
            SafeUi(() =>
            {
                lblAdvancedSearchProgress.Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "Candidate records: {0:N0}. {1}% scanned, {2:N0} matches",
                    progress.CandidateRecordCount,
                    progress.PercentScanned,
                    progress.MatchCount);
                progressAdvancedSearch.Value = progress.PercentScanned;
            });
        }

        private void HideAdvancedSearchProgress()
        {
            advancedSearchProgressPanel.Visible = false;
        }

        private void SetStatus(string message)
        {
            lblStatus.Text = message;
            if (_busyDepth > 0)
            {
                lblBusy.Text = message;
            }
        }

        private void ShowToast(string message)
        {
            lblToast.Text = message;
            toastPanel.Visible = true;
            toastPanel.BringToFront();
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void PositionPopupPanel(Panel panel, Control target)
        {
            var targetBottomLeft = PointToClient(target.Parent.PointToScreen(new Point(target.Left, target.Bottom + 4)));
            panel.Left = targetBottomLeft.X;
            panel.Top = targetBottomLeft.Y;
            if (panel.Right > Width)
            {
                panel.Left = Math.Max(0, Width - panel.Width - 24);
            }
        }

        private void SafeUi(Action action)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(action);
                return;
            }

            action();
        }

        private static Guid CreateStableConnectionId(Uri environmentUrl)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(environmentUrl.GetLeftPart(UriPartial.Authority).ToLowerInvariant()));
                return new Guid(bytes);
            }
        }

        private static bool ContainsIgnoreCase(string value, string searchText)
        {
            return value != null && value.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    internal sealed class RunGridRow
    {
        private readonly FlowRun _run;

        public RunGridRow(FlowRun run)
        {
            _run = run;
        }

        public string StartedDisplay => _run.StartedOn.HasValue ? _run.StartedOn.Value.LocalDateTime.ToString("g", CultureInfo.CurrentCulture) : string.Empty;
        public string EndedDisplay => _run.EndedOn.HasValue ? _run.EndedOn.Value.LocalDateTime.ToString("g", CultureInfo.CurrentCulture) : string.Empty;
        public string Status => _run.Status ?? string.Empty;
        public string Name => _run.Name ?? _run.RunId ?? string.Empty;
        public string RunUrl => _run.RunUrl;

        public string GetTriggerValue(string key)
        {
            string value;
            return _run.TriggerInputs.TryGetValue(key, out value) ? value : string.Empty;
        }
    }

    internal sealed class AttributeNameComparer : IComparer<string>
    {
        public static readonly AttributeNameComparer Instance = new AttributeNameComparer();

        public int Compare(string x, string y)
        {
            var normalizedCompare = StringComparer.OrdinalIgnoreCase.Compare(Normalize(x), Normalize(y));
            return normalizedCompare != 0
                ? normalizedCompare
                : StringComparer.OrdinalIgnoreCase.Compare(x, y);
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).TrimStart('_');
        }
    }

}
