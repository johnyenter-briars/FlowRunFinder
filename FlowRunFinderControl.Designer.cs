using System.Drawing;
using System.Windows.Forms;

namespace FlowRunFinder
{
    partial class FlowRunFinderControl
    {
        private System.ComponentModel.IContainer components = null;

        // ── Controls ──────────────────────────────────────────────────────────
        private ToolStrip          toolStrip;
        private ToolStripButton    tsbReloadFlows;
        private ToolStripSeparator tsSep1;
        private ToolStripButton    tsbClose;

        private Panel              pnlTop;
        private Label              lblEnvironmentHeader;
        private Label              lblEnvironment;
        private Label              lblEnvIdHdr;
        private TextBox            txtEnvironmentId;
        private Label              lblFlowSearchHdr;
        private Label              lblFlowCount;
        private ComboBox           cmbFlow;

        private Panel              pnlMiddle;
        private Label              lblFromHdr;
        private DateTimePicker     dtpStartDate;
        private DateTimePicker     dtpStartTime;
        private Label              lblToHdr;
        private DateTimePicker     dtpEndDate;
        private DateTimePicker     dtpEndTime;
        private Button             btnToday;
        private Button             btnLastHour;
        private Button             btnAddColumns;
        private Button             btnFindRuns;

        private Panel              pnlResults;
        private Label              lblResultCount;
        private Button             btnSelectAll;
        private Button             btnDeleteSelected;
        private DataGridView       dgvResults;

        private Panel              pnlStatus;
        private Label              lblStatus;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();

            // ── ToolStrip ──────────────────────────────────────────────────
            toolStrip      = new ToolStrip();
            tsbReloadFlows = new ToolStripButton();
            tsSep1         = new ToolStripSeparator();
            tsbClose       = new ToolStripButton();

            tsbReloadFlows.Text         = "Reload Flows";
            tsbReloadFlows.ToolTipText  = "Reload the list of cloud flows from this environment";
            tsbReloadFlows.Image        = SystemIcons.Application.ToBitmap();
            tsbReloadFlows.DisplayStyle = ToolStripItemDisplayStyle.Text;
            tsbReloadFlows.Click       += tsbReloadFlows_Click;

            tsbClose.Text         = "Close";
            tsbClose.DisplayStyle = ToolStripItemDisplayStyle.Text;
            tsbClose.Alignment    = ToolStripItemAlignment.Right;
            tsbClose.Click       += tsbClose_Click;

            toolStrip.Items.AddRange(new ToolStripItem[] { tsbReloadFlows, tsSep1, tsbClose });
            toolStrip.Dock = DockStyle.Top;

            // ── Top panel: Environment + Flow selection ────────────────────
            pnlTop = new Panel { Dock = DockStyle.Top, Height = 136, Padding = new Padding(12, 10, 12, 6) };

            lblEnvironmentHeader = MakeLabel("ENVIRONMENT", 7, bold: true, secondary: true);
            lblEnvironmentHeader.Location = new Point(12, 12);
            lblEnvironmentHeader.AutoSize = true;

            lblEnvironment = MakeLabel("Not connected", 13);
            lblEnvironment.Location = new Point(12, 26);
            lblEnvironment.AutoSize = true;
            lblEnvironment.ForeColor = Color.FromArgb(0, 120, 212);

            lblEnvIdHdr = MakeLabel("ENVIRONMENT ID  (paste from Power Automate URL if blank)", 7, bold: true, secondary: true);
            lblEnvIdHdr.Location = new Point(12, 50);
            lblEnvIdHdr.AutoSize = true;

            txtEnvironmentId = new TextBox
            {
                Location  = new Point(12, 64),
                Width     = 560,
                Font      = new Font("Courier New", 8.5f),
                ForeColor = Color.FromArgb(0, 120, 212)
            };

            lblFlowSearchHdr = MakeLabel("CLOUD FLOW", 7, bold: true, secondary: true);
            lblFlowSearchHdr.Location = new Point(12, 92);
            lblFlowSearchHdr.AutoSize = true;

            lblFlowCount = MakeLabel("", 8, secondary: true);
            lblFlowCount.Location = new Point(110, 92);
            lblFlowCount.AutoSize = true;

            // Single combo box — DropDown style lets the user type to filter.
            // TextChanged filters Items in real time; SelectedIndexChanged enables Find Runs.
            cmbFlow = new ComboBox
            {
                Location         = new Point(12, 108),
                Width            = 560,
                DropDownStyle    = ComboBoxStyle.DropDown,
                Font             = new Font("Segoe UI", 9f),
                DropDownWidth    = 560,
                MaxDropDownItems = 12
            };
            cmbFlow.SelectedIndexChanged += cmbFlow_SelectedIndexChanged;
            cmbFlow.TextChanged          += cmbFlow_TextChanged;

            pnlTop.Controls.AddRange(new Control[]
            {
                lblEnvironmentHeader, lblEnvironment,
                lblEnvIdHdr, txtEnvironmentId,
                lblFlowSearchHdr, lblFlowCount, cmbFlow
            });

            // ── Middle panel: Date range + buttons ────────────────────────
            pnlMiddle = new Panel { Dock = DockStyle.Top, Height = 86, Padding = new Padding(12, 6, 12, 6) };

            var lblDateRange = MakeLabel("DATE & TIME RANGE (LOCAL)", 7, bold: true, secondary: true);
            lblDateRange.Location = new Point(12, 6);
            lblDateRange.AutoSize = true;

            lblFromHdr = MakeLabel("From", 9);
            lblFromHdr.Location = new Point(12, 24);
            lblFromHdr.AutoSize = true;

            dtpStartDate = new DateTimePicker
            {
                Format   = DateTimePickerFormat.Short,
                Location = new Point(50, 20),
                Width    = 100
            };
            dtpStartTime = new DateTimePicker
            {
                Format   = DateTimePickerFormat.Time,
                ShowUpDown = true,
                Location = new Point(158, 20),
                Width    = 90
            };

            lblToHdr = MakeLabel("To", 9);
            lblToHdr.Location = new Point(262, 24);
            lblToHdr.AutoSize = true;

            dtpEndDate = new DateTimePicker
            {
                Format   = DateTimePickerFormat.Short,
                Location = new Point(280, 20),
                Width    = 100
            };
            dtpEndTime = new DateTimePicker
            {
                Format     = DateTimePickerFormat.Time,
                ShowUpDown = true,
                Location   = new Point(388, 20),
                Width      = 90
            };

            btnToday = MakeButton("Today", 490, 18);
            btnToday.Width  = 72;
            btnToday.Click += btnToday_Click;

            btnLastHour = MakeButton("Last Hour", 568, 18);
            btnLastHour.Width  = 82;
            btnLastHour.Click += btnLastHour_Click;

            btnAddColumns = MakeButton("Columns...", 656, 18);
            btnAddColumns.Width   = 95;
            btnAddColumns.Enabled = false;
            btnAddColumns.Click  += btnAddColumns_Click;
            new ToolTip().SetToolTip(btnAddColumns, "Choose trigger / input fields to show as extra columns");

            btnFindRuns = MakeButton("Find Runs", 12, 50, primary: true);
            btnFindRuns.Width   = 120;
            btnFindRuns.Enabled = false;
            btnFindRuns.Click  += btnFindRuns_Click;

            pnlMiddle.Controls.AddRange(new Control[]
            {
                lblDateRange,
                lblFromHdr, dtpStartDate, dtpStartTime,
                lblToHdr,   dtpEndDate,   dtpEndTime,
                btnToday, btnLastHour, btnAddColumns, btnFindRuns
            });

            // ── Results panel ─────────────────────────────────────────────
            pnlResults = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 4) };

            lblResultCount = MakeLabel("", 9, secondary: true);
            lblResultCount.Location = new Point(12, 6);
            lblResultCount.AutoSize = true;

            // Select All / None toggle
            btnSelectAll = MakeButton("Select All", 0, 2);
            btnSelectAll.Width   = 82;
            btnSelectAll.Anchor  = AnchorStyles.Top | AnchorStyles.Right;
            btnSelectAll.Enabled = false;
            btnSelectAll.Click  += btnSelectAll_Click;

            // Delete Selected — red, disabled until rows are checked
            btnDeleteSelected = new Button
            {
                Text      = "Delete Selected",
                Location  = new Point(0, 2),
                Width     = 128,
                Height    = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(164, 38, 44),
                ForeColor = Color.White,
                Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
                Anchor    = AnchorStyles.Top | AnchorStyles.Right,
                Enabled   = false
            };
            btnDeleteSelected.FlatAppearance.BorderColor = Color.FromArgb(130, 25, 30);
            btnDeleteSelected.Click += btnDeleteSelected_Click;
            new ToolTip().SetToolTip(btnDeleteSelected, "Permanently delete the checked flow run records from Dataverse");

            dgvResults = new DataGridView
            {
                Location              = new Point(12, 32),
                Anchor                = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode         = DataGridViewSelectionMode.FullRowSelect,
                ReadOnly              = true,
                AllowUserToAddRows    = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible     = false,
                BackgroundColor       = SystemColors.Window,
                BorderStyle           = BorderStyle.Fixed3D,
                Font                  = new Font("Segoe UI", 9f),
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    BackColor = Color.FromArgb(243, 242, 241),
                    ForeColor = Color.FromArgb(96, 94, 92)
                },
                EnableHeadersVisualStyles = false,
                GridColor       = Color.FromArgb(237, 235, 233),
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                RowTemplate     = { Height = 28 },
                MultiSelect     = true
            };

            // colCheck: fixed-width checkbox column (manual toggle — grid stays ReadOnly)
            dgvResults.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name          = "colCheck",
                HeaderText    = "",
                Width         = 32,
                AutoSizeMode  = DataGridViewAutoSizeColumnMode.None,
                Resizable     = DataGridViewTriState.False
            });
            // colFlow: shown only in all-flows mode
            dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "colFlow",     HeaderText = "Flow",     FillWeight = 22, Visible = false });
            dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "colStatus",   HeaderText = "Status",   FillWeight = 15 });
            dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "colStarted",  HeaderText = "Started",  FillWeight = 25 });
            dgvResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "colDuration", HeaderText = "Duration", FillWeight = 12 });
            dgvResults.Columns.Add(new DataGridViewLinkColumn    { Name = "colLink",     HeaderText = "Link",     FillWeight = 28,
                                                                   TrackVisitedState = false, LinkColor = Color.FromArgb(0, 120, 212) });

            dgvResults.CellClick         += dgvResults_CellClick;
            dgvResults.CellContentClick  += dgvResults_CellContentClick;
            dgvResults.CellMouseEnter    += dgvResults_CellMouseEnter;
            dgvResults.CellMouseLeave    += dgvResults_CellMouseLeave;

            pnlResults.Controls.AddRange(new Control[] { lblResultCount, btnSelectAll, btnDeleteSelected, dgvResults });

            // ── Status bar ────────────────────────────────────────────────
            pnlStatus = new Panel { Dock = DockStyle.Bottom, Height = 24, BackColor = Color.FromArgb(243, 242, 241) };
            lblStatus = MakeLabel("Connect to an environment to begin.", 8, secondary: true);
            lblStatus.Location = new Point(8, 5);
            lblStatus.AutoSize = true;
            pnlStatus.Controls.Add(lblStatus);

            // ── Wire up ───────────────────────────────────────────────────
            SuspendLayout();
            Controls.Add(pnlResults);
            Controls.Add(pnlMiddle);
            Controls.Add(pnlTop);
            Controls.Add(toolStrip);
            Controls.Add(pnlStatus);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ResumeLayout(false);
            PerformLayout();

            // Fix results panel size and right-anchor button positions after layout
            dgvResults.Size           = new Size(pnlResults.Width - 24, pnlResults.Height - 40);
            btnDeleteSelected.Left    = pnlResults.Width - 12 - btnDeleteSelected.Width;
            btnSelectAll.Left         = btnDeleteSelected.Left - 6 - btnSelectAll.Width;
        }

        // ── Layout helpers ────────────────────────────────────────────────────
        private static Label MakeLabel(string text, float size, bool bold = false, bool secondary = false)
            => new Label
            {
                Text      = text,
                Font      = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = secondary ? Color.FromArgb(96, 94, 92) : SystemColors.ControlText,
                AutoSize  = true
            };

        private static Button MakeButton(string text, int x, int y, bool primary = false)
            => new Button
            {
                Text      = text,
                Location  = new Point(x, y),
                Width     = 80,
                Height    = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Color.FromArgb(0, 120, 212) : SystemColors.Control,
                ForeColor = primary ? Color.White : SystemColors.ControlText,
                Font      = new Font("Segoe UI", 9f, primary ? FontStyle.Bold : FontStyle.Regular),
                FlatAppearance = { BorderColor = primary ? Color.FromArgb(0, 100, 180) : Color.FromArgb(200, 198, 196) }
            };
    }
}
