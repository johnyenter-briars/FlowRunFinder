using System.Drawing;
using System.Windows.Forms;

namespace FlowRunFinder
{
    partial class FlowRunFinderControl
    {
        private System.ComponentModel.IContainer components = null;

        private TableLayoutPanel rootLayout;
        private Panel connectionPanel;
        private Label lblConnectionCaption;
        private Label lblConnection;
        private Button btnReloadFlows;
        private Button btnSettings;
        private Button btnClose;

        private Panel commandPanel;
        private Label lblFlowCaption;
        private Button btnFlowPicker;
        private Button btnRefreshRuns;
        private Button btnTriggerColumns;
        private Button btnAdvancedSearch;

        private Panel flowPickerPanel;
        private TextBox txtFlowSearch;
        private ListBox lstFlows;

        private Panel triggerColumnsPanel;
        private Label lblTriggerColumnsCaption;
        private TextBox txtTriggerColumnSearch;
        private CheckedListBox clbTriggerColumns;

        private Panel deviceCodePanel;
        private Label lblDeviceMessage;
        private Label lblDeviceUrlCaption;
        private Label lblDeviceUrl;
        private Button btnCopyDeviceUrl;
        private Label lblDeviceCodeCaption;
        private Label lblDeviceCode;
        private Button btnCopyDeviceCode;

        private Label lblStatus;
        private Panel resultsPanel;
        private DataGridView dgvRuns;
        private Panel busyPanel;
        private ProgressBar progressBusy;
        private Label lblBusy;
        private Button btnCancelBusyAction;
        private Panel advancedSearchProgressPanel;
        private Label lblAdvancedSearchProgress;
        private ProgressBar progressAdvancedSearch;
        private Panel toastPanel;
        private Label lblToast;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_dataverseClient != null) _dataverseClient.Dispose();
                if (_activeQuerySession != null) _activeQuerySession.Cancel();
                if (_toastTimer != null) _toastTimer.Dispose();
                if (components != null) components.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();

            rootLayout = new TableLayoutPanel();
            connectionPanel = new Panel();
            lblConnectionCaption = MakeCaption("Connection");
            lblConnection = MakeBodyLabel("No XrmToolBox connection selected.");
            btnReloadFlows = MakeButton("Reload Flows", true);
            btnSettings = MakeButton("Settings", false);
            btnClose = MakeButton("Close", false);

            commandPanel = new Panel();
            lblFlowCaption = MakeCaption("Flow");
            btnFlowPicker = MakeButton("Select a flow", false);
            btnRefreshRuns = MakeButton("Refresh Runs", true);
            btnTriggerColumns = MakeButton("Trigger Columns", false);
            btnAdvancedSearch = MakeButton("Advanced Search", false);

            flowPickerPanel = new Panel();
            txtFlowSearch = new TextBox();
            lstFlows = new ListBox();

            triggerColumnsPanel = new Panel();
            lblTriggerColumnsCaption = MakeBodyLabel("Trigger input columns");
            txtTriggerColumnSearch = new TextBox();
            clbTriggerColumns = new CheckedListBox();

            deviceCodePanel = new Panel();
            lblDeviceMessage = MakeBodyLabel("");
            lblDeviceUrlCaption = MakeCaption("URL");
            lblDeviceUrl = MakeBodyLabel("");
            btnCopyDeviceUrl = MakeButton("Copy", false);
            lblDeviceCodeCaption = MakeCaption("Code");
            lblDeviceCode = MakeBodyLabel("");
            btnCopyDeviceCode = MakeButton("Copy", false);

            lblStatus = MakeMutedLabel("Not connected.");
            resultsPanel = new Panel();
            dgvRuns = new DataGridView();
            busyPanel = new Panel();
            progressBusy = new ProgressBar();
            lblBusy = MakeBodyLabel("Working...");
            btnCancelBusyAction = MakeButton("Cancel", false);
            advancedSearchProgressPanel = new Panel();
            lblAdvancedSearchProgress = MakeBodyLabel("Candidate records: 0. 0% scanned, 0 matches");
            progressAdvancedSearch = new ProgressBar();
            toastPanel = new Panel();
            lblToast = MakeBodyLabel("Copied to clipboard");

            SuspendLayout();

            rootLayout.Dock = DockStyle.Fill;
            rootLayout.BackColor = Color.FromArgb(250, 250, 250);
            rootLayout.Padding = new Padding(20);
            rootLayout.RowCount = 5;
            rootLayout.ColumnCount = 1;
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            BuildConnectionPanel();
            BuildCommandPanel();
            BuildFlowPickerPanel();
            BuildTriggerColumnsPanel();
            BuildDeviceCodePanel();
            BuildResultsPanel();
            BuildBusyAndToastPanels();

            rootLayout.Controls.Add(connectionPanel, 0, 0);
            rootLayout.Controls.Add(commandPanel, 0, 1);
            rootLayout.Controls.Add(deviceCodePanel, 0, 2);
            rootLayout.Controls.Add(lblStatus, 0, 3);
            rootLayout.Controls.Add(resultsPanel, 0, 4);

            Controls.Add(rootLayout);
            Controls.Add(flowPickerPanel);
            Controls.Add(triggerColumnsPanel);
            Controls.Add(busyPanel);
            Controls.Add(advancedSearchProgressPanel);
            Controls.Add(toastPanel);
            flowPickerPanel.BringToFront();
            triggerColumnsPanel.BringToFront();
            busyPanel.BringToFront();
            advancedSearchProgressPanel.BringToFront();
            toastPanel.BringToFront();

            btnReloadFlows.Click += btnReloadFlows_Click;
            btnSettings.Click += btnSettings_Click;
            btnClose.Click += btnClose_Click;
            btnFlowPicker.Click += btnFlowPicker_Click;
            btnRefreshRuns.Click += btnRefreshRuns_Click;
            btnTriggerColumns.Click += btnTriggerColumns_Click;
            btnAdvancedSearch.Click += btnAdvancedSearch_Click;
            btnCancelBusyAction.Click += btnCancelBusyAction_Click;
            txtFlowSearch.TextChanged += txtFlowSearch_TextChanged;
            lstFlows.SelectedIndexChanged += lstFlows_SelectedIndexChanged;
            txtTriggerColumnSearch.TextChanged += txtTriggerColumnSearch_TextChanged;
            clbTriggerColumns.ItemCheck += clbTriggerColumns_ItemCheck;
            btnCopyDeviceUrl.Click += btnCopyDeviceUrl_Click;
            btnCopyDeviceCode.Click += btnCopyDeviceCode_Click;
            dgvRuns.CellContentClick += dgvRuns_CellContentClick;
            dgvRuns.CellMouseDown += dgvRuns_CellMouseDown;

            AutoScaleMode = AutoScaleMode.Font;
            ResumeLayout(false);
        }

        private void BuildConnectionPanel()
        {
            connectionPanel.Dock = DockStyle.Fill;
            connectionPanel.Padding = new Padding(14);
            connectionPanel.BackColor = Color.White;
            connectionPanel.BorderStyle = BorderStyle.FixedSingle;

            lblConnectionCaption.Location = new Point(14, 21);
            lblConnectionCaption.Size = new Size(80, 20);
            lblConnection.Location = new Point(100, 20);
            lblConnection.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            lblConnection.Size = new Size(520, 22);

            btnReloadFlows.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnReloadFlows.Location = new Point(650, 15);
            btnReloadFlows.Size = new Size(110, 32);
            btnSettings.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSettings.Location = new Point(768, 15);
            btnSettings.Size = new Size(88, 32);
            btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClose.Location = new Point(864, 15);
            btnClose.Size = new Size(74, 32);

            connectionPanel.Resize += delegate
            {
                btnClose.Left = connectionPanel.Width - 14 - btnClose.Width;
                btnSettings.Left = btnClose.Left - 8 - btnSettings.Width;
                btnReloadFlows.Left = btnSettings.Left - 8 - btnReloadFlows.Width;
                lblConnection.Width = btnReloadFlows.Left - lblConnection.Left - 12;
            };

            connectionPanel.Controls.AddRange(new Control[]
            {
                lblConnectionCaption, lblConnection, btnReloadFlows, btnSettings, btnClose
            });
        }

        private void BuildCommandPanel()
        {
            commandPanel.Dock = DockStyle.Top;
            commandPanel.Height = 70;
            commandPanel.Margin = new Padding(0, 16, 0, 0);
            commandPanel.Padding = new Padding(14);
            commandPanel.BackColor = Color.White;
            commandPanel.BorderStyle = BorderStyle.FixedSingle;

            lblFlowCaption.Location = new Point(14, 23);
            lblFlowCaption.Size = new Size(48, 20);
            btnFlowPicker.Location = new Point(68, 18);
            btnFlowPicker.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            btnFlowPicker.Size = new Size(520, 32);
            btnFlowPicker.TextAlign = ContentAlignment.MiddleLeft;

            btnRefreshRuns.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnRefreshRuns.Location = new Point(604, 18);
            btnRefreshRuns.Size = new Size(110, 32);
            btnTriggerColumns.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnTriggerColumns.Location = new Point(722, 18);
            btnTriggerColumns.Size = new Size(126, 32);
            btnAdvancedSearch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnAdvancedSearch.Location = new Point(856, 18);
            btnAdvancedSearch.Size = new Size(132, 32);

            commandPanel.Resize += delegate
            {
                btnAdvancedSearch.Left = commandPanel.Width - 14 - btnAdvancedSearch.Width;
                btnTriggerColumns.Left = btnAdvancedSearch.Left - 8 - btnTriggerColumns.Width;
                btnRefreshRuns.Left = btnTriggerColumns.Left - 8 - btnRefreshRuns.Width;
                btnFlowPicker.Width = btnRefreshRuns.Left - btnFlowPicker.Left - 12;
            };

            commandPanel.Controls.AddRange(new Control[]
            {
                lblFlowCaption, btnFlowPicker, btnRefreshRuns, btnTriggerColumns, btnAdvancedSearch
            });
        }

        private void BuildFlowPickerPanel()
        {
            flowPickerPanel.Width = 620;
            flowPickerPanel.Height = 420;
            flowPickerPanel.Padding = new Padding(12);
            flowPickerPanel.BackColor = Color.FromArgb(245, 247, 250);
            flowPickerPanel.BorderStyle = BorderStyle.FixedSingle;
            flowPickerPanel.Visible = false;

            txtFlowSearch.Dock = DockStyle.Top;
            txtFlowSearch.Height = 24;

            lstFlows.Dock = DockStyle.Fill;
            lstFlows.IntegralHeight = false;

            flowPickerPanel.Controls.Add(lstFlows);
            flowPickerPanel.Controls.Add(txtFlowSearch);
        }

        private void BuildTriggerColumnsPanel()
        {
            triggerColumnsPanel.Width = 320;
            triggerColumnsPanel.Height = 380;
            triggerColumnsPanel.Padding = new Padding(12);
            triggerColumnsPanel.BackColor = Color.FromArgb(245, 247, 250);
            triggerColumnsPanel.BorderStyle = BorderStyle.FixedSingle;
            triggerColumnsPanel.Visible = false;

            lblTriggerColumnsCaption.Dock = DockStyle.Top;
            lblTriggerColumnsCaption.Height = 24;
            lblTriggerColumnsCaption.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            txtTriggerColumnSearch.Dock = DockStyle.Top;
            txtTriggerColumnSearch.Height = 24;
            clbTriggerColumns.Dock = DockStyle.Fill;
            clbTriggerColumns.CheckOnClick = true;
            clbTriggerColumns.IntegralHeight = false;

            triggerColumnsPanel.Controls.Add(clbTriggerColumns);
            triggerColumnsPanel.Controls.Add(txtTriggerColumnSearch);
            triggerColumnsPanel.Controls.Add(lblTriggerColumnsCaption);
        }

        private void BuildDeviceCodePanel()
        {
            deviceCodePanel.Dock = DockStyle.Top;
            deviceCodePanel.Height = 96;
            deviceCodePanel.Margin = new Padding(0, 8, 0, 0);
            deviceCodePanel.Padding = new Padding(12);
            deviceCodePanel.BackColor = Color.White;
            deviceCodePanel.BorderStyle = BorderStyle.FixedSingle;
            deviceCodePanel.Visible = false;

            lblDeviceMessage.Location = new Point(12, 8);
            lblDeviceMessage.Size = new Size(760, 20);
            lblDeviceUrlCaption.Location = new Point(12, 36);
            lblDeviceUrlCaption.Size = new Size(48, 18);
            lblDeviceUrl.Location = new Point(70, 34);
            lblDeviceUrl.Size = new Size(640, 20);
            btnCopyDeviceUrl.Location = new Point(720, 30);
            btnCopyDeviceUrl.Size = new Size(70, 26);
            lblDeviceCodeCaption.Location = new Point(12, 64);
            lblDeviceCodeCaption.Size = new Size(48, 18);
            lblDeviceCode.Location = new Point(70, 62);
            lblDeviceCode.Size = new Size(640, 20);
            lblDeviceCode.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnCopyDeviceCode.Location = new Point(720, 58);
            btnCopyDeviceCode.Size = new Size(70, 26);

            deviceCodePanel.Controls.AddRange(new Control[]
            {
                lblDeviceMessage, lblDeviceUrlCaption, lblDeviceUrl, btnCopyDeviceUrl,
                lblDeviceCodeCaption, lblDeviceCode, btnCopyDeviceCode
            });

        }

        private void BuildResultsPanel()
        {
            resultsPanel.Dock = DockStyle.Fill;
            resultsPanel.BackColor = Color.White;
            resultsPanel.BorderStyle = BorderStyle.FixedSingle;
            resultsPanel.Padding = new Padding(1);

            dgvRuns.Dock = DockStyle.Fill;
            dgvRuns.AllowUserToAddRows = false;
            dgvRuns.AllowUserToDeleteRows = false;
            dgvRuns.AutoGenerateColumns = false;
            dgvRuns.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
            dgvRuns.BackgroundColor = Color.White;
            dgvRuns.BorderStyle = BorderStyle.None;
            dgvRuns.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            dgvRuns.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(243, 242, 241);
            dgvRuns.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            dgvRuns.DefaultCellStyle.SelectionBackColor = Color.FromArgb(210, 228, 255);
            dgvRuns.DefaultCellStyle.SelectionForeColor = Color.Black;
            dgvRuns.EnableHeadersVisualStyles = false;
            dgvRuns.GridColor = Color.FromArgb(237, 235, 233);
            dgvRuns.MultiSelect = false;
            dgvRuns.ReadOnly = true;
            dgvRuns.RowHeadersVisible = false;
            dgvRuns.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

            dgvRuns.Columns.Add(new DataGridViewTextBoxColumn { Name = "colStarted", HeaderText = "Started", DataPropertyName = "StartedDisplay" });
            dgvRuns.Columns.Add(new DataGridViewTextBoxColumn { Name = "colEnded", HeaderText = "Ended", DataPropertyName = "EndedDisplay" });
            dgvRuns.Columns.Add(new DataGridViewTextBoxColumn { Name = "colStatus", HeaderText = "Status", DataPropertyName = "Status" });
            dgvRuns.Columns.Add(new DataGridViewLinkColumn
            {
                Name = "colRunId",
                HeaderText = "Run Id",
                DataPropertyName = "Name",
                LinkColor = Color.FromArgb(0, 120, 212),
                TrackVisitedState = false
            });

            resultsPanel.Controls.Add(dgvRuns);
        }

        private void BuildBusyAndToastPanels()
        {
            busyPanel.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            busyPanel.Size = new Size(350, 48);
            busyPanel.BackColor = Color.FromArgb(245, 247, 250);
            busyPanel.BorderStyle = BorderStyle.FixedSingle;
            busyPanel.Visible = false;
            busyPanel.Padding = new Padding(12);
            busyPanel.Controls.Add(progressBusy);
            busyPanel.Controls.Add(lblBusy);
            busyPanel.Controls.Add(btnCancelBusyAction);

            progressBusy.Style = ProgressBarStyle.Marquee;
            progressBusy.Location = new Point(12, 18);
            progressBusy.Size = new Size(86, 8);
            lblBusy.Location = new Point(108, 14);
            lblBusy.Size = new Size(130, 20);
            btnCancelBusyAction.Location = new Point(268, 10);
            btnCancelBusyAction.Size = new Size(68, 28);
            btnCancelBusyAction.Visible = false;

            advancedSearchProgressPanel.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            advancedSearchProgressPanel.Size = new Size(350, 70);
            advancedSearchProgressPanel.BackColor = Color.FromArgb(245, 247, 250);
            advancedSearchProgressPanel.BorderStyle = BorderStyle.FixedSingle;
            advancedSearchProgressPanel.Visible = false;
            advancedSearchProgressPanel.Padding = new Padding(12);
            lblAdvancedSearchProgress.Location = new Point(12, 10);
            lblAdvancedSearchProgress.Size = new Size(324, 20);
            progressAdvancedSearch.Location = new Point(12, 38);
            progressAdvancedSearch.Size = new Size(324, 12);
            advancedSearchProgressPanel.Controls.Add(progressAdvancedSearch);
            advancedSearchProgressPanel.Controls.Add(lblAdvancedSearchProgress);

            toastPanel.Anchor = AnchorStyles.Bottom;
            toastPanel.Size = new Size(180, 36);
            toastPanel.BackColor = Color.FromArgb(245, 247, 250);
            toastPanel.BorderStyle = BorderStyle.FixedSingle;
            toastPanel.Visible = false;
            toastPanel.Padding = new Padding(12, 8, 12, 8);
            lblToast.Dock = DockStyle.Fill;
            lblToast.TextAlign = ContentAlignment.MiddleCenter;
            toastPanel.Controls.Add(lblToast);

            Resize += delegate
            {
                busyPanel.Left = Width - busyPanel.Width - 24;
                busyPanel.Top = Height - busyPanel.Height - 24;
                advancedSearchProgressPanel.Left = Width - advancedSearchProgressPanel.Width - 24;
                advancedSearchProgressPanel.Top = busyPanel.Top - advancedSearchProgressPanel.Height - 8;
                toastPanel.Left = (Width - toastPanel.Width) / 2;
                toastPanel.Top = Height - toastPanel.Height - 24;
            };
        }

        private static Label MakeCaption(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(96, 94, 92),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static Label MakeBodyLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Font = new Font("Segoe UI", 9f),
                ForeColor = SystemColors.ControlText,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static Label MakeMutedLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(96, 94, 92),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static Button MakeButton(string text, bool primary)
        {
            var button = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, primary ? FontStyle.Bold : FontStyle.Regular),
                BackColor = primary ? Color.FromArgb(0, 120, 212) : SystemColors.Control,
                ForeColor = primary ? Color.White : SystemColors.ControlText
            };
            button.FlatAppearance.BorderColor = primary ? Color.FromArgb(0, 100, 180) : Color.FromArgb(200, 198, 196);
            return button;
        }
    }
}
